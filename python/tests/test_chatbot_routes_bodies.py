"""Routes de chat WinAI (routes/chatbot_routes.py) : /chat, /stream (SSE), /complete, chargeurs de contexte
et extraction des mémoires. Fournisseur d'IA, mur de quota et recherche documentaire simulés ; SQLite en mémoire."""

import json
from datetime import datetime, timedelta, timezone
from types import SimpleNamespace

import pytest
from fastapi import FastAPI, HTTPException
from fastapi.testclient import TestClient

import database as dbm
import routes.chatbot_routes as cb

UTC = timezone.utc
REAL_EXTRACT = cb._extract_and_save_memories          # gardée avant que les fixtures ne la remplacent


class FakeProvider:
    def __init__(self, result=None, error=None, chunks=None, stream_error=None):
        self.result = result if result is not None else {"content": "Bonjour !", "tokens_used": 42,
                                                         "generation_time_ms": 800, "success": True}
        self.error, self.chunks, self.stream_error = error, chunks, stream_error
        self.chat_calls, self.stream_calls = [], []

    def chat(self, **kwargs):
        self.chat_calls.append(kwargs)
        if self.error:
            raise self.error
        return self.result

    def chat_stream(self, **kwargs):
        self.stream_calls.append(kwargs)
        for chunk in self.chunks or []:
            yield chunk
        if self.stream_error:
            raise self.stream_error


def delta(text):
    return "data: " + json.dumps({"delta": text}) + "\n\n"


def usage(tokens):
    return "data: " + json.dumps({"usage_final": True, "tokens_used": tokens}) + "\n\n"


DONE = "data: [DONE]\n\n"


@pytest.fixture
def env(monkeypatch, sqlite_database):
    db = sqlite_database(cb, [dbm.Conversation, dbm.ChatMessage, dbm.UserAIMemory, dbm.QuizAttempt, dbm.QuizMistake,
                              dbm.Subject, dbm.Exam, dbm.User])
    state = SimpleNamespace(provider=FakeProvider(), quota=[], tasks=[], memories=[], rag=("", 0), prompt_calls=[],
                            role="student")

    monkeypatch.setattr(cb, "get_deepseek_client", lambda: state.provider)

    def quota(current_user, client_message_id, label, conversation_id=None):
        state.quota.append((current_user.user_id, client_message_id, label, conversation_id))
        if isinstance(state.quota_error, Exception):
            raise state.quota_error

    state.quota_error = None
    monkeypatch.setattr(cb, "_enforce_ai_quota", quota)

    def build_prompt(user_context, token, text=""):
        state.prompt_calls.append((user_context, token.user_id, text))
        return "SYS", state.role

    monkeypatch.setattr(cb, "_build_prompt_from_request", build_prompt)

    async def rag(user_context, messages, user_id):
        return state.rag

    monkeypatch.setattr(cb, "build_rag_context_block", rag)

    async def fake_format(messages, user_id=None):
        return [{"role": m.role, "content": m.content} for m in messages]

    monkeypatch.setattr(cb, "format_messages_for_deepseek", fake_format)
    monkeypatch.setattr(cb, "_extract_and_save_memories", lambda uid, content, session: state.memories.append((uid, content)))
    monkeypatch.setattr(cb, "asyncio", SimpleNamespace(
        to_thread=lambda fn, *a: ("thread", fn, a), create_task=lambda t: state.tasks.append(t)))

    app = FastAPI()
    app.include_router(cb.chatbot_router, prefix="/api/chatbot")
    client = TestClient(app, raise_server_exceptions=False)
    client.db, client.state = db, state
    return client


def sse(response):
    """Découpe un flux SSE en événements bruts."""
    return [e + "\n\n" for e in response.text.split("\n\n") if e]


CHAT = {"messages": [{"role": "user", "content": "Explique les dérivées"}]}


# ── POST /chat ──────────────────────────────────────────────────────────────

def test_chat_requires_a_user_token(env, headers_for):
    assert env.post("/api/chatbot/chat", json=CHAT).status_code in (401, 403)
    assert env.post("/api/chatbot/chat", json=CHAT, headers=headers_for(7)).status_code == 200


@pytest.mark.parametrize("body", [
    {}, {"messages": "texte"},
    {"messages": [{"role": "robot", "content": "x"}]},
    {"messages": [{"role": "user", "content": ""}]},
    {**CHAT, "max_tokens": 50}, {**CHAT, "max_tokens": 4001},
    {**CHAT, "temperature": 1.5}, {**CHAT, "temperature": -0.1},
])
def test_chat_schema_violations_are_422_before_any_work(env, headers_for, body):
    assert env.post("/api/chatbot/chat", json=body, headers=headers_for(7)).status_code == 422
    assert env.state.quota == [] and env.state.provider.chat_calls == []


def test_chat_without_messages_is_400(env, headers_for):
    r = env.post("/api/chatbot/chat", json={"messages": []}, headers=headers_for(7))
    assert r.status_code == 400 and r.json()["detail"] == "Messages are required"


def test_chat_maps_the_provider_answer_and_passes_the_quota_id(env, headers_for):
    body = {**CHAT, "client_message_id": "msg-9", "max_tokens": 500, "temperature": 0.2}
    r = env.post("/api/chatbot/chat", json=body, headers=headers_for(7))
    assert r.json() == {"content": "Bonjour !", "tokens_used": 42, "generation_time_ms": 800, "success": True,
                        "error": None}
    assert env.state.quota == [(7, "msg-9", "Chat", None)]
    call = env.state.provider.chat_calls[0]
    assert call["system_prompt"] == "SYS" and call["max_tokens"] == 500 and call["temperature"] == 0.2
    assert call["messages"] == [{"role": "user", "content": "Explique les dérivées"}]


def test_chat_builds_the_prompt_from_the_last_user_message(env, headers_for):
    body = {"messages": [{"role": "user", "content": "premier"}, {"role": "assistant", "content": "réponse"},
                         {"role": "user", "content": "dernier"}, {"role": "assistant", "content": "fin"}],
            "user_context": {"role": "teacher"}}
    env.post("/api/chatbot/chat", json=body, headers=headers_for(7, "teacher"))
    context, user_id, text = env.state.prompt_calls[0]
    assert text == "dernier" and user_id == 7 and context.role == "teacher"


def test_chat_uses_the_given_system_prompt_without_building_one(env, headers_for):
    env.post("/api/chatbot/chat", json={**CHAT, "system_prompt": "Sois bref."}, headers=headers_for(7))
    assert env.state.prompt_calls == [] and env.state.provider.chat_calls[0]["system_prompt"] == "Sois bref."


def test_chat_refused_by_the_quota_wall_never_reaches_the_provider(env, headers_for):
    env.state.quota_error = HTTPException(status_code=402, detail={"error": "quota_exceeded"})
    r = env.post("/api/chatbot/chat", json=CHAT, headers=headers_for(7))
    assert r.status_code == 402 and r.json()["detail"]["error"] == "quota_exceeded"
    assert env.state.provider.chat_calls == []


def test_chat_searches_the_knowledge_base_only_when_asked(env, headers_for):
    env.state.rag = ("\n[RAG]", 15)
    env.post("/api/chatbot/chat", json=CHAT, headers=headers_for(7))
    assert env.state.provider.chat_calls[0]["system_prompt"] == "SYS"
    r = env.post("/api/chatbot/chat", json={**CHAT, "knowledge_base": True}, headers=headers_for(7))
    assert env.state.provider.chat_calls[1]["system_prompt"] == "SYS\n[RAG]"
    assert r.json()["tokens_used"] == 42 + 15


def test_chat_tolerates_a_missing_token_count(env, headers_for):
    env.state.provider = FakeProvider({"content": "ok", "tokens_used": None, "success": True})
    body = env.post("/api/chatbot/chat", json=CHAT, headers=headers_for(7)).json()
    assert body["tokens_used"] == 0 and body["generation_time_ms"] == 0


def test_chat_returns_a_provider_level_failure_as_success_false(env, headers_for):
    env.state.provider = FakeProvider({"content": "", "success": False, "error": "quota fournisseur"})
    r = env.post("/api/chatbot/chat", json=CHAT, headers=headers_for(7))
    assert r.status_code == 200 and r.json()["success"] is False and r.json()["error"] == "quota fournisseur"


def test_chat_provider_exception_is_a_500_carrying_the_message(env, headers_for):
    env.state.provider = FakeProvider(error=TimeoutError("délai dépassé"))
    r = env.post("/api/chatbot/chat", json=CHAT, headers=headers_for(7))
    assert r.status_code == 500 and "délai dépassé" in r.json()["detail"]


@pytest.mark.parametrize("role,result,scheduled", [
    ("student", {"content": "ok", "success": True}, 1),
    ("parent", {"content": "ok", "success": True}, 1),
    ("teacher", {"content": "ok", "success": True}, 0),
    ("student", {"content": "ok", "success": False}, 0),
    ("student", {"content": "", "success": True}, 0),
])
def test_chat_schedules_memory_extraction_for_students_and_parents_on_success(env, headers_for, role, result, scheduled):
    env.state.provider = FakeProvider(result)
    env.state.role = role
    env.post("/api/chatbot/chat", json=CHAT, headers=headers_for(7, role))
    assert len(env.state.tasks) == scheduled
    if scheduled:
        kind, fn, args = env.state.tasks[0]
        assert kind == "thread" and fn is cb._extract_memories_background and args == (7, "ok")


# ── POST /chat avec un jeton technique ──────────────────────────────────────

def test_service_token_needs_a_system_prompt(env, service_headers):
    r = env.post("/api/chatbot/chat", json=CHAT, headers=service_headers("ai.parent-report"))
    assert r.status_code == 400 and "system_prompt" in r.json()["detail"]
    assert env.state.provider.chat_calls == []


def test_service_token_skips_quota_prompt_building_and_memories(env, service_headers):
    r = env.post("/api/chatbot/chat", json={**CHAT, "system_prompt": "Rapport hebdomadaire", "knowledge_base": True},
                 headers=service_headers("ai.parent-report"))
    assert r.status_code == 200 and r.json()["content"] == "Bonjour !"
    assert env.state.quota == [] and env.state.prompt_calls == [] and env.state.tasks == []
    assert env.state.provider.chat_calls[0]["system_prompt"] == "Rapport hebdomadaire"       # pas de bloc RAG


def test_service_token_of_another_scope_is_refused(env, service_headers):
    r = env.post("/api/chatbot/chat", json={**CHAT, "system_prompt": "x"}, headers=service_headers("ai.task"))
    assert r.status_code in (401, 403) and env.state.provider.chat_calls == []


# ── POST /complete ──────────────────────────────────────────────────────────

URL_COMPLETE = "/api/chatbot/complete"


def test_complete_requires_a_user_token_and_a_prompt(env, headers_for, service_headers):
    assert env.post(URL_COMPLETE, json={"prompt": "x"}).status_code in (401, 403)
    assert env.post(URL_COMPLETE, json={"prompt": "x"}, headers=service_headers("ai.parent-report")).status_code in (401, 403)
    for body in ({}, {"prompt": ""}):
        r = env.post(URL_COMPLETE, json=body, headers=headers_for(7))
        assert r.status_code == 400 and r.json()["detail"] == "Prompt is required"
    assert env.post(URL_COMPLETE, json=["liste"], headers=headers_for(7)).status_code == 422


def test_complete_sends_a_single_user_message_with_default_settings(env, headers_for):
    r = env.post(URL_COMPLETE, json={"prompt": "Définis un nombre premier"}, headers=headers_for(7))
    assert r.json()["content"] == "Bonjour !" and r.json()["tokens_used"] == 42
    call = env.state.provider.chat_calls[0]
    assert call["messages"] == [{"role": "user", "content": "Définis un nombre premier"}]
    assert call["max_tokens"] == 2000 and call["temperature"] == 0.7
    assert env.state.quota == [(7, None, "Complete", None)]


def test_complete_forwards_custom_settings_and_both_spellings_of_the_reservation_id(env, headers_for):
    env.post(URL_COMPLETE, json={"prompt": "x", "maxTokens": 300, "temperature": 0.1, "client_message_id": "a"},
             headers=headers_for(7))
    env.post(URL_COMPLETE, json={"prompt": "x", "clientMessageId": "b"}, headers=headers_for(7))
    env.post(URL_COMPLETE, json={"prompt": "x", "client_message_id": 123}, headers=headers_for(7))
    assert [q[1] for q in env.state.quota] == ["a", "b", None]            # un identifiant non textuel est ignoré
    assert env.state.provider.chat_calls[0]["max_tokens"] == 300 and env.state.provider.chat_calls[0]["temperature"] == 0.1


def test_complete_quota_refusal_and_provider_failure(env, headers_for):
    env.state.quota_error = HTTPException(status_code=403, detail={"error": "reservation_required"})
    assert env.post(URL_COMPLETE, json={"prompt": "x"}, headers=headers_for(7)).status_code == 403
    assert env.state.provider.chat_calls == []
    env.state.quota_error = None
    env.state.provider = FakeProvider(error=RuntimeError("fournisseur HS"))
    r = env.post(URL_COMPLETE, json={"prompt": "x"}, headers=headers_for(7))
    assert r.status_code == 500 and "fournisseur HS" in r.json()["detail"]


# ── POST /stream ────────────────────────────────────────────────────────────

URL_STREAM = "/api/chatbot/stream"
STREAM = {"messages": [{"role": "user", "content": "Bonjour"}], "conversation_id": 3}


def _conversation(env, conv_id=3):
    env.db.add(dbm.Conversation(Id=conv_id, UserId=7))


def test_stream_requires_a_user_token_and_valid_body(env, headers_for, service_headers):
    assert env.post(URL_STREAM, json=STREAM).status_code in (401, 403)
    assert env.post(URL_STREAM, json=STREAM, headers=service_headers("ai.parent-report")).status_code in (401, 403)
    assert env.post(URL_STREAM, json={}, headers=headers_for(7)).status_code == 422
    assert env.post(URL_STREAM, json={"messages": "x"}, headers=headers_for(7)).status_code == 422


def test_stream_quota_refusal_happens_before_any_streaming(env, headers_for):
    env.state.quota_error = HTTPException(status_code=402, detail={"error": "quota_exceeded"})
    r = env.post(URL_STREAM, json=STREAM, headers=headers_for(7))
    assert r.status_code == 402 and env.state.provider.stream_calls == []


def test_stream_relays_chunks_in_order_and_ends_with_done(env, headers_for):
    _conversation(env)
    env.state.provider = FakeProvider(chunks=[delta("Bon"), delta("jour"), usage(30), DONE])
    r = env.post(URL_STREAM, json={**STREAM, "client_message_id": "m1"}, headers=headers_for(7))
    assert r.status_code == 200 and r.headers["content-type"].startswith("text/event-stream")
    assert r.headers["cache-control"] == "no-cache" and r.headers["x-accel-buffering"] == "no"
    events = sse(r)
    assert events[0] == delta("Bon") and events[1] == delta("jour") and events[-1] == DONE
    assert env.state.quota == [(7, "m1", "Stream", 3)]
    call = env.state.provider.stream_calls[0]
    assert call["emit_usage"] is True and call["system_prompt"] == "SYS" and call["max_tokens"] == 2000


def test_stream_saves_the_assistant_message_with_the_real_token_count(env, headers_for):
    _conversation(env)
    env.state.provider = FakeProvider(chunks=[delta("Bon"), delta("jour"), usage(30), DONE])
    env.post(URL_STREAM, json=STREAM, headers=headers_for(7))
    saved = env.db.all(dbm.ChatMessage)
    assert len(saved) == 1
    assert (saved[0].ConversationId, saved[0].Role, saved[0].Content, saved[0].TokensUsed) == (3, "assistant", "Bonjour", 30)
    assert saved[0].GenerationTimeMs is not None


def test_stream_without_conversation_saves_nothing(env, headers_for):
    env.state.provider = FakeProvider(chunks=[delta("Salut"), DONE])
    env.post(URL_STREAM, json={"messages": STREAM["messages"]}, headers=headers_for(7))
    assert env.db.all(dbm.ChatMessage) == []


def test_stream_adds_the_search_cost_to_the_final_usage_event(env, headers_for):
    env.state.rag = ("\n[RAG]", 12)
    env.state.provider = FakeProvider(chunks=[delta("ok"), usage(30), DONE])
    r = env.post(URL_STREAM, json={**STREAM, "knowledge_base": True}, headers=headers_for(7))
    final = json.loads(sse(r)[1][6:])
    assert final["tokens_used"] == 42 and final["rag_tokens"] == 12
    assert env.state.provider.stream_calls[0]["system_prompt"] == "SYS\n[RAG]"


def test_stream_does_not_search_without_the_knowledge_base_mode(env, headers_for):
    env.state.rag = ("\n[RAG]", 12)
    env.state.provider = FakeProvider(chunks=[usage(30), DONE])
    r = env.post(URL_STREAM, json=STREAM, headers=headers_for(7))
    assert json.loads(sse(r)[0][6:]) == {"usage_final": True, "tokens_used": 30}


def test_stream_provider_error_midway_emits_an_error_event_then_done_and_keeps_the_partial_text(env, headers_for):
    _conversation(env)
    env.state.provider = FakeProvider(chunks=[delta("Début")], stream_error=ConnectionError("coupure réseau"))
    r = env.post(URL_STREAM, json=STREAM, headers=headers_for(7))
    events = sse(r)
    assert events[0] == delta("Début") and "coupure réseau" in events[1] and events[-1] == DONE
    assert [m.Content for m in env.db.all(dbm.ChatMessage)] == ["Début"]


def test_stream_provider_failure_before_any_chunk_still_closes_the_stream(env, headers_for):
    env.state.provider = FakeProvider(stream_error=TimeoutError("délai dépassé"))
    r = env.post(URL_STREAM, json=STREAM, headers=headers_for(7))
    assert r.status_code == 200 and sse(r)[-1] == DONE and "délai dépassé" in r.text
    assert env.db.all(dbm.ChatMessage) == []


@pytest.mark.parametrize("role,extracted", [("student", 1), ("parent", 1), ("teacher", 0)])
def test_stream_extracts_memories_for_students_and_parents_only(env, headers_for, role, extracted):
    env.state.role = role
    env.state.provider = FakeProvider(chunks=[delta("Une réponse"), DONE])
    env.post(URL_STREAM, json={"messages": STREAM["messages"]}, headers=headers_for(7, role))
    assert env.state.memories == ([(7, "Une réponse")] if extracted else [])


def test_stream_uses_the_given_system_prompt(env, headers_for):
    env.state.provider = FakeProvider(chunks=[DONE])
    env.post(URL_STREAM, json={**STREAM, "system_prompt": "Réponds en vers."}, headers=headers_for(7))
    assert env.state.prompt_calls == [] and env.state.provider.stream_calls[0]["system_prompt"] == "Réponds en vers."


def test_stream_extracts_the_text_of_block_messages_and_forces_the_detected_language(env, headers_for, monkeypatch):
    monkeypatch.setattr(cb, "detect_language", lambda text: "english" if "hello" in text else "french")
    env.state.provider = FakeProvider(chunks=[DONE])
    body = {"messages": [{"role": "user", "content": [{"type": "text", "text": "hello"},
                                                      {"type": "image_url", "image_url": {"url": "data:..."}},
                                                      {"type": "text", "text": "teacher"}]}],
            "user_context": {"role": "student"}}
    env.post(URL_STREAM, json=body, headers=headers_for(7))
    context, _, text = env.state.prompt_calls[0]
    assert text == "hello teacher" and context.force_language == "english"


def test_stream_keeps_an_explicit_language_choice(env, headers_for, monkeypatch):
    monkeypatch.setattr(cb, "detect_language", lambda text: pytest.fail("auto-détection non attendue"))
    env.state.provider = FakeProvider(chunks=[DONE])
    body = {"messages": STREAM["messages"], "user_context": {"role": "student", "force_language": "pidgin"}}
    env.post(URL_STREAM, json=body, headers=headers_for(7))
    assert env.state.prompt_calls[0][0].force_language == "pidgin"


def test_stream_with_only_assistant_messages_has_an_empty_last_user_text(env, headers_for):
    env.state.provider = FakeProvider(chunks=[DONE])
    env.post(URL_STREAM, json={"messages": [{"role": "assistant", "content": "x"}]}, headers=headers_for(7))
    assert env.state.prompt_calls[0][2] == ""


# ── chargeurs de contexte (SQLite) ──────────────────────────────────────────

def _memory(env, id_, mtype, content, days_ago, user_id=7):
    env.db.add(dbm.UserAIMemory(Id=id_, UserId=user_id, MemoryType=mtype, Content=content,
                                CreatedAt=datetime.now(UTC) - timedelta(days=days_ago),
                                UpdatedAt=datetime.now(UTC) - timedelta(days=days_ago)))


def test_memories_are_returned_newest_first_without_a_message(env):
    _memory(env, 1, "exam_context", "Ancienne", 10), _memory(env, 2, "exam_context", "Récente", 1)
    _memory(env, 3, "exam_context", "Autre élève", 0, user_id=8)
    assert [m["content"] for m in cb._load_user_memories(7)] == ["Récente", "Ancienne"]


def test_memories_sharing_words_with_the_message_come_first(env):
    _memory(env, 1, "struggling_topics", "Difficulté avec les limites", 20)
    _memory(env, 2, "learning_preference", "Aime les schémas colorés", 1)
    _memory(env, 3, "exam_context", "Prépare le BEPC", 2)
    memories = cb._load_user_memories(7, "Je ne comprends pas les limites")
    assert memories[0] == {"type": "struggling_topics", "content": "Difficulté avec les limites"}
    assert [m["content"] for m in memories[1:]] == ["Aime les schémas colorés", "Prépare le BEPC"]    # récence conservée


def test_memories_keep_ten_out_of_the_thirty_latest(env):
    for i in range(35):
        _memory(env, i + 1, "exam_context", f"mem{i}", i)
    result = cb._load_user_memories(7)
    assert len(result) == 10 and result[0]["content"] == "mem0"


def test_memory_loader_never_raises(monkeypatch):
    monkeypatch.setattr(cb, "Database", lambda: (_ for _ in ()).throw(RuntimeError("base HS")))
    assert cb._load_user_memories(7) == [] and cb._load_quiz_mistakes(7) == []
    assert cb._load_performance_history(7) == {} and cb._load_catalog_block() == ""
    assert cb._load_teacher_profile_data(7) == {}


def test_quiz_mistakes_are_unresolved_only_newest_first_and_limited(env):
    env.db.add(dbm.QuizMistake(Id=1, UserId=7, Subject="Maths", Question="Q1", GivenAnswer="a", CorrectAnswer="b",
                               CreatedAt=datetime.now(UTC) - timedelta(days=5)),
               dbm.QuizMistake(Id=2, UserId=7, Subject=None, Question="Q2", CreatedAt=datetime.now(UTC) - timedelta(days=1)),
               dbm.QuizMistake(Id=3, UserId=7, Subject="SVT", Question="Q3", IsResolved=True),
               dbm.QuizMistake(Id=4, UserId=8, Subject="SVT", Question="Q4"))
    result = cb._load_quiz_mistakes(7)
    assert result == [{"subject": "Général", "question": "Q2", "given_answer": None, "correct_answer": None},
                      {"subject": "Maths", "question": "Q1", "given_answer": "a", "correct_answer": "b"}]
    assert len(cb._load_quiz_mistakes(7, limit=1)) == 1


def test_performance_history_converts_percentages_to_marks_out_of_twenty(env):
    now = datetime.now(UTC)
    env.db.add(dbm.QuizAttempt(Id=1, UserId=7, Score=80, CompletedAt=now - timedelta(days=2)),
               dbm.QuizAttempt(Id=2, UserId=7, Score=60, CompletedAt=now - timedelta(days=3)),
               dbm.QuizAttempt(Id=3, UserId=7, Score=0, CompletedAt=now - timedelta(days=90)),       # hors fenêtre de 30 jours
               dbm.QuizAttempt(Id=4, UserId=8, Score=100, CompletedAt=now))
    values = list(cb._load_performance_history(7).values())
    assert values == [14.0]                                       # (80 + 60) / 2 % = 70 % = 14/20


def test_performance_history_is_empty_without_recent_attempts(env):
    assert cb._load_performance_history(7) == {}


def test_catalog_block_lists_published_subjects_with_prices(env):
    env.db.add(dbm.Subject(Id=1, Title="BAC C 2023", Category="Maths", Price=0, IsPublished=True, EnrollmentCount=10),
               dbm.Subject(Id=2, Title="BEPC 2022", Category="Français", Price=1500, IsPublished=True, EnrollmentCount=50),
               dbm.Subject(Id=3, Title="Brouillon", IsPublished=False),
               dbm.Subject(Id=4, Title="Supprimé", IsPublished=True, IsDeleted=True))
    block = cb._load_catalog_block()
    assert block.startswith("\n\n[Catalogue WinPlus")
    assert block.index("BEPC 2022") < block.index("BAC C 2023")          # triés par inscriptions décroissantes
    assert "BEPC 2022 (Français) · 1500 XAF" in block and "BAC C 2023 (Maths) · gratuit" in block
    assert "Brouillon" not in block and "Supprimé" not in block
    assert "UNIQUEMENT dans cette liste" in block


def test_catalog_block_respects_its_limit_and_is_empty_without_subjects(env):
    assert cb._load_catalog_block() == ""
    env.db.add(*[dbm.Subject(Id=i, Title=f"S{i}", IsPublished=True, EnrollmentCount=i) for i in range(1, 6)])
    block = cb._load_catalog_block(limit=2)
    assert block.count("\n  - ") == 2 and "S5" in block and "S4" in block


def test_teacher_profile_of_an_unknown_user_is_empty(env):
    assert cb._load_teacher_profile_data(404) == {}


def test_parent_children_data_requires_children_and_a_parent():
    assert cb._load_parent_children_data([], 5) == [] and cb._load_parent_children_data([1], None) == []


def test_parent_children_data_skips_unlinked_and_empty_children_and_keeps_five(monkeypatch):
    from services.parent_child_context import ParentAccessDenied
    ctx = {"name": "Marie", "level": "3e", "avg_score": 12, "avg_score_trend": "up", "subjects": ["Maths"],
           "quiz_gaps": [], "pending_homework": [], "late_homework": [], "bulletin_extract": None}

    def build(parent_id, child_id):
        if child_id == 2:
            raise ParentAccessDenied("non lié")
        if child_id == 3:
            return None
        return {**ctx, "exam_watch": {"days": 5}} if child_id == 1 else dict(ctx)

    monkeypatch.setattr("services.parent_child_context.build_child_context", build)
    result = cb._load_parent_children_data([1, 2, 3, 4, 5, 6, 7], parent_id=9)
    assert len(result) == 2                                      # 1 et 4 ; 6 et 7 sont coupés avant d'être lus
    assert result[0]["exam_watch"] == {"days": 5} and "exam_watch" not in result[1]
    assert set(result[0]) == {"name", "level", "avg_score", "avg_score_trend", "subjects", "quiz_gaps",
                              "pending_homework", "late_homework", "bulletin_extract", "exam_watch"}


# ── extraction des mémoires ─────────────────────────────────────────────────

LONG = "x" * 120


class MemoryClient:
    def __init__(self, content=None, success=True, error=None):
        self.content, self.success, self.error, self.calls = content, success, error, []

    def chat(self, **kw):
        self.calls.append(kw)
        if self.error:
            raise self.error
        return {"success": self.success, "content": self.content}


def _extract(env, monkeypatch, content, user_id=7, **client_kw):
    """Appelle la vraie fonction d'extraction (l'enregistreur de `env` est contourné)."""
    client = MemoryClient(content, **client_kw)
    monkeypatch.setattr(cb, "get_deepseek_client", lambda: client)
    session = env.db.get_session()
    try:
        REAL_EXTRACT(user_id, LONG, session)
    finally:
        session.close()
    return client


def _stored(env):
    return sorted((m.MemoryType, m.Content) for m in env.db.all(dbm.UserAIMemory))


def test_extraction_ignores_short_answers_without_calling_the_provider(env, monkeypatch):
    client = MemoryClient("[]")
    monkeypatch.setattr(cb, "get_deepseek_client", lambda: client)
    REAL_EXTRACT(7, "court", env.db.get_session())
    REAL_EXTRACT(7, "", env.db.get_session())
    assert client.calls == []


def test_extraction_stores_valid_memories_only_and_at_most_three(env, monkeypatch):
    payload = [{"type": "exam_context", "content": "Prépare le BAC C 2027"},
               {"type": "inconnu", "content": "ignoré"}, {"type": "exam_context", "content": "  "},
               "pas un objet", {"type": "learning_preference", "content": "Aime les schémas"},
               {"type": "motivation_style", "content": "Quatrième, ignoré car au-delà de trois"}]
    client = _extract(env, monkeypatch, json.dumps(payload))
    assert _stored(env) == [("exam_context", "Prépare le BAC C 2027"), ("learning_preference", "Aime les schémas")]
    assert client.calls[0]["max_tokens"] == 300 and client.calls[0]["temperature"] == 0.2
    assert client.calls[0]["messages"][0]["content"] == LONG


def test_extraction_accepts_a_fenced_json_block(env, monkeypatch):
    _extract(env, monkeypatch, '```json\n[{"type": "exam_context", "content": "BEPC"}]\n```')
    assert _stored(env) == [("exam_context", "BEPC")]


def test_extraction_updates_a_near_duplicate_and_keeps_the_longer_text(env, monkeypatch):
    _memory(env, 1, "struggling_topics", "Difficulté avec les limites", 10)
    _extract(env, monkeypatch, json.dumps([{"type": "struggling_topics", "content": "Difficulté avec les limites de fonctions"}]))
    rows = env.db.all(dbm.UserAIMemory)
    assert len(rows) == 1 and rows[0].Content == "Difficulté avec les limites de fonctions"


def test_extraction_keeps_the_existing_text_when_the_new_one_is_shorter(env, monkeypatch):
    _memory(env, 1, "struggling_topics", "Difficulté avec les limites de fonctions", 10)
    _extract(env, monkeypatch, json.dumps([{"type": "struggling_topics", "content": "Difficulté avec les limites"}]))
    assert [m.Content for m in env.db.all(dbm.UserAIMemory)] == ["Difficulté avec les limites de fonctions"]


def test_extraction_does_not_merge_different_texts_or_other_students(env, monkeypatch):
    _memory(env, 1, "struggling_topics", "Les probabilités conditionnelles", 10)
    _memory(env, 2, "struggling_topics", "Difficulté avec les limites", 10, user_id=8)
    _extract(env, monkeypatch, json.dumps([{"type": "struggling_topics", "content": "Difficulté avec les limites"}]))
    assert len([m for m in env.db.all(dbm.UserAIMemory) if m.UserId == 7]) == 2


def test_a_mastered_topic_removes_the_matching_difficulty(env, monkeypatch):
    _memory(env, 1, "struggling_topics", "Limites de fonctions", 10)
    _memory(env, 2, "struggling_topics", "Probabilités", 10)
    _extract(env, monkeypatch, json.dumps([{"type": "understood_topics", "content": "Limites de fonctions"}]))
    assert _stored(env) == [("struggling_topics", "Probabilités"), ("understood_topics", "Limites de fonctions")]


def test_only_one_unfinished_topic_is_kept(env, monkeypatch):
    _memory(env, 1, "unfinished_topic", "Équations du second degré", 5)
    _extract(env, monkeypatch, json.dumps([{"type": "unfinished_topic", "content": "Suites numériques"}]))
    assert _stored(env) == [("unfinished_topic", "Suites numériques")]


@pytest.mark.parametrize("content,success", [("[]", False), ("pas du json", True), ('{"type": "exam_context"}', True)])
def test_extraction_with_an_unusable_answer_stores_nothing(env, monkeypatch, content, success):
    _extract(env, monkeypatch, content, success=success)
    assert env.db.all(dbm.UserAIMemory) == []


def test_extraction_swallows_provider_errors(env, monkeypatch):
    _extract(env, monkeypatch, None, error=TimeoutError("lent"))
    assert env.db.all(dbm.UserAIMemory) == []


def test_background_extraction_opens_and_closes_its_own_session(env, monkeypatch):
    seen = {}

    class Session:
        def close(self):
            seen["closed"] = True

    monkeypatch.setattr(cb, "Database", lambda: SimpleNamespace(SessionLocal=lambda: Session()))
    monkeypatch.setattr(cb, "_extract_and_save_memories", lambda uid, content, session: seen.update(args=(uid, content)))
    cb._extract_memories_background(7, "réponse")
    assert seen == {"args": (7, "réponse"), "closed": True}
