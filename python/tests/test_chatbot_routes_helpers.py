"""Routes de chat WinAI (routes/chatbot_routes.py) : mur de quota côté Python, mise en forme des messages
avec pièces jointes, construction du prompt système selon le rôle, route de santé. Base, quota, RAG et
fournisseur d'IA sont simulés."""

import asyncio
from datetime import datetime, timezone
from types import SimpleNamespace

import pytest
from fastapi import FastAPI, HTTPException
from fastapi.testclient import TestClient

import routes.chatbot_routes as cb
from auth import UserTokenData
from schemas import ChatbotContextRequest, ChatMessage
from services.ai_quota import QuotaCheck

UTC = timezone.utc


def _user(user_id=7, role="student"):
    return UserTokenData(user_id=user_id, email="u@example.test", role=role)


# ── similarité de texte ─────────────────────────────────────────────────────

def test_text_similarity_ignores_case_and_outer_spaces():
    assert cb._text_similarity("  Les Dérivées ", "les dérivées") == 1.0
    assert cb._text_similarity("abc", "xyz") == 0.0
    assert 0.0 < cb._text_similarity("les dérivées", "les intégrales") < 1.0


# ── mur de quota ────────────────────────────────────────────────────────────

class FakeQuotaSession:
    def __init__(self, owns_conversation=True, execute_error=None):
        self.owns, self.execute_error = owns_conversation, execute_error
        self.queries, self.closed = [], False

    def execute(self, statement, params=None):
        self.queries.append((str(statement), params))
        if self.execute_error:
            raise self.execute_error
        return SimpleNamespace(first=lambda: (1,) if self.owns else None)

    def close(self):
        self.closed = True


@pytest.fixture
def quota(monkeypatch):
    state = SimpleNamespace(session=FakeQuotaSession(), result=QuotaCheck(True, "reserved"), calls=[])
    monkeypatch.setattr(cb, "Database", lambda: SimpleNamespace(SessionLocal=lambda: state.session))

    def fake_check(session, user_id, client_message_id=None):
        state.calls.append((session, user_id, client_message_id))
        if isinstance(state.result, Exception):
            raise state.result
        return state.result

    monkeypatch.setattr(cb, "check_quota", fake_check)
    return state


def test_quota_allows_a_valid_reservation(quota):
    assert cb._enforce_ai_quota(_user(7), "msg-1", "stream") is None
    assert quota.calls == [(quota.session, 7, "msg-1")] and quota.session.closed is True


def test_quota_checks_conversation_ownership_when_provided(quota):
    cb._enforce_ai_quota(_user(7), "m", "stream", conversation_id=12)
    sql, params = quota.session.queries[0]
    assert '"Conversations"' in sql and '"IsDeleted" = FALSE' in sql
    assert params == {"cid": 12, "uid": 7}


def test_quota_refuses_someone_elses_conversation_with_404(quota):
    quota.session.owns = False
    with pytest.raises(HTTPException) as info:
        cb._enforce_ai_quota(_user(7), "m", "stream", conversation_id=12)
    assert info.value.status_code == 404
    assert quota.calls == [] and quota.session.closed is True


def test_quota_without_conversation_does_not_query_conversations(quota):
    cb._enforce_ai_quota(_user(7), "m", "chat")
    assert quota.session.queries == []


def test_quota_limit_reached_is_402_with_reset_time_and_no_token_count(quota):
    resets = datetime(2026, 3, 15, 15, 0, tzinfo=UTC)
    quota.result = QuotaCheck(False, "limit_reached", "session", resets, "Limite de session atteinte.")
    with pytest.raises(HTTPException) as info:
        cb._enforce_ai_quota(_user(7), "m", "stream")
    assert info.value.status_code == 402
    assert info.value.detail == {"error": "quota_exceeded", "limit": "session",
                                 "resetsAt": "2026-03-15T15:00:00+00:00", "message": "Limite de session atteinte."}


def test_quota_limit_reached_without_reset_time(quota):
    quota.result = QuotaCheck(False, "limit_reached", "week", None, "msg")
    with pytest.raises(HTTPException) as info:
        cb._enforce_ai_quota(_user(7), "m", "stream")
    assert info.value.detail["resetsAt"] is None


def test_quota_without_reservation_is_403(quota):
    quota.result = QuotaCheck(False, "reservation_required", message="Ce message doit passer par l'API WinPlus.")
    with pytest.raises(HTTPException) as info:
        cb._enforce_ai_quota(_user(7), None, "complete")
    assert info.value.status_code == 403
    assert info.value.detail == {"error": "reservation_required", "message": "Ce message doit passer par l'API WinPlus."}


def test_quota_check_failure_is_fail_closed_503(quota):
    quota.result = RuntimeError("table absente")
    with pytest.raises(HTTPException) as info:
        cb._enforce_ai_quota(_user(7), "m", "stream")
    assert info.value.status_code == 503 and info.value.detail["error"] == "quota_unavailable"
    assert quota.session.closed is True


def test_quota_database_error_on_ownership_check_is_503(quota):
    quota.session.execute_error = RuntimeError("base inaccessible")
    with pytest.raises(HTTPException) as info:
        cb._enforce_ai_quota(_user(7), "m", "stream", conversation_id=3)
    assert info.value.status_code == 503


# ── messages et pièces jointes ──────────────────────────────────────────────

def _message(content, attachments=None, role="user"):
    return ChatMessage(role=role, content=content, attachments=attachments or [])


def test_messages_without_attachment_are_passed_through():
    out = asyncio.run(cb.format_messages_for_deepseek([_message("Bonjour"), _message("Salut", role="assistant")]))
    assert out == [{"role": "user", "content": "Bonjour"}, {"role": "assistant", "content": "Salut"}]


def test_image_equation_and_unreadable_file_are_described():
    msg = _message("Regarde", [
        {"type": "image", "data": "data:image/png;base64,AAAA"},
        {"type": "equation", "data": "x^2"},
        {"type": "file", "file_name": "devoir.pdf"},        # sans contenu : simplement nommé
        {"type": "file"},                                     # ni contenu ni nom
    ])
    out = asyncio.run(cb.format_messages_for_deepseek([msg], user_id=7))
    assert out[0]["content"] == "Regarde\n\n[Image attachée] [Équation: x^2] [Fichier: devoir.pdf] [Fichier: fichier]"


def test_file_content_goes_through_the_attachment_processor(monkeypatch):
    seen = []

    async def fake_process(data, name, user_id):
        seen.append((data, name, user_id))
        return "[Contenu du fichier joint]"

    monkeypatch.setattr(cb, "process_chat_attachment_async", fake_process)
    msg = _message("Lis ça", [{"type": "file", "data": "QUJD", "file_name": "a.txt"}])
    out = asyncio.run(cb.format_messages_for_deepseek([msg], user_id=7))
    assert out[0]["content"] == "Lis ça\n\n[Contenu du fichier joint]"
    assert seen == [("QUJD", "a.txt", 7)]


def test_file_content_is_not_read_without_a_user(monkeypatch):
    async def fail(*args):
        pytest.fail("le fichier ne doit pas être traité sans identifiant utilisateur")

    monkeypatch.setattr(cb, "process_chat_attachment_async", fail)
    msg = _message("Lis ça", [{"type": "file", "data": "QUJD", "file_name": "a.txt"}])
    out = asyncio.run(cb.format_messages_for_deepseek([msg]))
    assert out[0]["content"].endswith("[Fichier: a.txt]")


# ── prompt système ──────────────────────────────────────────────────────────

@pytest.fixture
def loaders(monkeypatch):
    calls = {"memories": [], "mistakes": [], "performance": [], "children": [], "teacher": [], "catalog": 0}

    def memories(user_id, message=""):
        calls["memories"].append((user_id, message))
        return [{"type": "exam_context", "content": "BEPC en juin"}]

    def mistakes(user_id, limit=10):
        calls["mistakes"].append(user_id)
        return [{"subject": "Maths", "question": "2+2", "given_answer": "5", "correct_answer": "4"}]

    def performance(user_id):
        calls["performance"].append(user_id)
        return {"Maths": 11.0}

    def children(child_ids, parent_id=None):
        calls["children"].append((child_ids, parent_id))
        return [{"name": "Marie", "level": "3e", "avg_score": 14.0}]

    def teacher(teacher_id):
        calls["teacher"].append(teacher_id)
        return {"subjects": ["Physique"], "levels": ["Seconde", "Première"]}

    def catalog(limit=40):
        calls["catalog"] += 1
        return "\n[Catalogue]"

    monkeypatch.setattr(cb, "_load_user_memories", memories)
    monkeypatch.setattr(cb, "_load_quiz_mistakes", mistakes)
    monkeypatch.setattr(cb, "_load_performance_history", performance)
    monkeypatch.setattr(cb, "_load_parent_children_data", children)
    monkeypatch.setattr(cb, "_load_teacher_profile_data", teacher)
    monkeypatch.setattr(cb, "_load_catalog_block", catalog)
    return calls


def test_student_prompt_uses_server_data_when_the_front_sends_none(loaders):
    prompt, role = cb._build_prompt_from_request(ChatbotContextRequest(first_name="Paul"), _user(7), "Bonjour")
    assert role == "student"
    assert "Tu t'adresses à Paul." in prompt
    assert "[Préférence" not in prompt and "BEPC en juin" in prompt
    assert "Maths : 11.0/20" in prompt and "Lacunes identifiées" in prompt
    assert loaders["memories"] == [(7, "Bonjour")] and loaders["performance"] == [7]


def test_front_performance_history_wins_over_the_database(loaders):
    ctx = ChatbotContextRequest(performance_history={"Physique": 15.5})
    prompt, _ = cb._build_prompt_from_request(ctx, _user(7), "")
    assert "Physique : 15.5/20" in prompt and "Maths : 11.0/20" not in prompt
    assert loaders["performance"] == []


def test_catalog_block_is_added_only_for_students_asking_about_it(loaders):
    plain, _ = cb._build_prompt_from_request(None, _user(7), "Explique-moi les dérivées")
    assert "[Catalogue]" not in plain and loaders["catalog"] == 0
    asked, _ = cb._build_prompt_from_request(None, _user(7), "Tu as des annales de maths ?")
    assert asked.endswith("[Catalogue]") and loaders["catalog"] == 1
    teacher, _ = cb._build_prompt_from_request(ChatbotContextRequest(role="teacher"), _user(9, "teacher"), "annales")
    assert "[Catalogue]" not in teacher and loaders["catalog"] == 1


def test_role_comes_from_the_context_then_from_the_token(loaders):
    assert cb._build_prompt_from_request(ChatbotContextRequest(role="parent"), _user(7, "student"), "")[1] == "parent"
    assert cb._build_prompt_from_request(None, _user(7, "teacher"), "")[1] == "teacher"
    assert cb._build_prompt_from_request(ChatbotContextRequest(role=None), _user(7, "admin"), "")[1] == "admin"


def test_quiz_mistakes_are_loaded_for_students_only(loaders):
    cb._build_prompt_from_request(None, _user(7, "teacher"), "")
    assert loaders["mistakes"] == []
    cb._build_prompt_from_request(None, _user(7, "student"), "")
    assert loaders["mistakes"] == [7]


def test_parent_prompt_injects_children_only_when_ids_are_given(loaders):
    with_ids = ChatbotContextRequest(role="parent", child_ids=[10, 11])
    prompt, _ = cb._build_prompt_from_request(with_ids, _user(3, "parent"), "")
    assert loaders["children"] == [([10, 11], 3)]
    assert "[Enfants suivis]" in prompt and "Marie" in prompt
    loaders["children"].clear()
    prompt, _ = cb._build_prompt_from_request(ChatbotContextRequest(role="parent"), _user(3, "parent"), "")
    assert loaders["children"] == [] and "[Enfants suivis]" not in prompt


def test_teacher_profile_from_the_server_replaces_front_subjects_and_levels(loaders):
    ctx = ChatbotContextRequest(role="teacher", grade="Terminale", enrolled_subjects=[{"id": 1, "title": "Chimie"}])
    prompt, _ = cb._build_prompt_from_request(ctx, _user(9, "teacher"), "")
    assert loaders["teacher"] == [9]
    assert "Matières concernées : Physique." in prompt and "Chimie" not in prompt
    assert "Niveau : Seconde, Première." in prompt


def test_teacher_without_server_profile_falls_back_to_the_front(loaders, monkeypatch):
    monkeypatch.setattr(cb, "_load_teacher_profile_data", lambda teacher_id: {})
    ctx = ChatbotContextRequest(role="teacher", grade="Terminale", enrolled_subjects=[{"id": 1, "title": "Chimie"}])
    prompt, _ = cb._build_prompt_from_request(ctx, _user(9, "teacher"), "")
    assert "Matières concernées : Chimie." in prompt and "Niveau : Terminale." in prompt


def test_forced_language_and_session_context_reach_the_prompt(loaders):
    ctx = ChatbotContextRequest(force_language="english", navigation_history=[{"path": "/subjects/1", "title": "Trigo"}],
                                recent_activity=[{"type": "quiz", "subjectTitle": "Maths", "score": 58}])
    prompt, _ = cb._build_prompt_from_request(ctx, _user(7), "")
    assert "réponds en anglais" in prompt and "Page consultée : Trigo" in prompt and "58/100" in prompt


def test_anonymous_token_loads_nothing_from_the_database(loaders):
    prompt, role = cb._build_prompt_from_request(None, _user(0), "")
    assert role == "student" and loaders["memories"] == [] and loaders["performance"] == []


# ── route de santé ──────────────────────────────────────────────────────────

@pytest.fixture
def health_client(monkeypatch):
    app = FastAPI()
    app.include_router(cb.chatbot_router, prefix="/api/chatbot")
    return TestClient(app, raise_server_exceptions=False)


def test_health_is_public_and_reports_the_provider_state(monkeypatch, health_client):
    monkeypatch.setattr(cb, "get_deepseek_client", lambda: SimpleNamespace(
        health_check=lambda: {"status": "healthy", "service": "deepseek", "base_url": "http://llm.test"}))
    r = health_client.get("/api/chatbot/health")
    assert r.status_code == 200
    assert r.json() == {"status": "healthy", "service": "chatbot-fastapi",
                        "ai_service": {"status": "healthy", "service": "deepseek", "base_url": "http://llm.test"}}


def test_health_reports_an_unreachable_provider_without_failing(monkeypatch, health_client):
    monkeypatch.setattr(cb, "get_deepseek_client", lambda: SimpleNamespace(
        health_check=lambda: {"status": "unreachable", "service": "deepseek", "error": "refusé"}))
    r = health_client.get("/api/chatbot/health")
    assert r.status_code == 200 and r.json()["ai_service"]["status"] == "unreachable"


def test_health_failure_is_500(monkeypatch, health_client):
    def boom():
        raise RuntimeError("client introuvable")

    monkeypatch.setattr(cb, "get_deepseek_client", boom)
    r = health_client.get("/api/chatbot/health")
    assert r.status_code == 500 and r.json()["detail"] == "client introuvable"
