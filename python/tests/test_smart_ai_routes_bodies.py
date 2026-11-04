"""Routes IA intelligentes (routes/smart_ai_routes.py) : notification personnalisée, résumé de notifications,
adéquation d'un contenu, fiche de révision, quiz généré. Fournisseur d'IA simulé, SQLite en mémoire, horloge figée."""

import json
from datetime import datetime, timedelta
from types import SimpleNamespace

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

import database as dbm
import routes.smart_ai_routes as sa

NOW = datetime(2026, 6, 17, 9, 0, 0)


class FrozenDatetime(datetime):
    @classmethod
    def utcnow(cls):
        return NOW


class FakeClient:
    """Une réponse (str, dict ou exception) par appel ; la dernière est répétée."""

    def __init__(self, *answers):
        self.answers, self.calls = list(answers), []

    def chat(self, messages, system_prompt=None, max_tokens=None, temperature=None, **kw):
        self.calls.append({"prompt": messages[0]["content"], "system": system_prompt, "max_tokens": max_tokens,
                           "temperature": temperature})
        answer = self.answers.pop(0) if len(self.answers) > 1 else self.answers[0]
        if isinstance(answer, Exception):
            raise answer
        return answer if isinstance(answer, dict) else {"content": answer}


@pytest.fixture(autouse=True)
def clean_state(monkeypatch):
    sa._notif_cache.clear()
    sa._notif_daily.clear()
    monkeypatch.setattr(sa, "datetime", FrozenDatetime)
    monkeypatch.setattr(sa, "time", SimpleNamespace(time=lambda: 1_000_000.0))
    yield
    sa._notif_cache.clear()
    sa._notif_daily.clear()


@pytest.fixture
def env(monkeypatch, sqlite_database):
    db = sqlite_database(sa, [dbm.User, dbm.Subject, dbm.QuizAttempt, dbm.QuizMistake, dbm.DownloadHistory, dbm.Goal,
                              dbm.ChatbotContext, dbm.Enrollment])
    app = FastAPI()
    app.include_router(sa.smart_ai_router, prefix="/api")
    client = TestClient(app, raise_server_exceptions=False)
    client.db = db

    def use(*answers):
        fake = FakeClient(*answers)
        monkeypatch.setattr(sa, "get_deepseek_client", lambda: fake)
        return fake

    client.use = use
    return client


# ── _load_student_generation_signals ────────────────────────────────────────

def test_signals_return_the_learning_style_text_and_enrolled_subjects(env):
    style = next(iter(sa._VARK_INSTRUCTIONS))
    env.db.add(dbm.ChatbotContext(Id=1, UserId=7, LearningStyle=style),
               *[dbm.Subject(Id=i, Title=f"M{i}") for i in range(1, 9)],
               *[dbm.Enrollment(Id=i, UserId=7, SubjectId=i) for i in range(1, 9)],
               dbm.Enrollment(Id=20, UserId=8, SubjectId=1))
    instruction, subjects = sa._load_student_generation_signals(7)
    assert instruction == sa._VARK_INSTRUCTIONS[style] and len(subjects) == 6        # six matières au plus


def test_signals_ignore_deleted_enrollments_unknown_styles_and_other_students(env):
    env.db.add(dbm.ChatbotContext(Id=1, UserId=7, LearningStyle="inconnu"), dbm.Subject(Id=1, Title="Maths"),
               dbm.Enrollment(Id=1, UserId=7, SubjectId=1, IsDeleted=True))
    assert sa._load_student_generation_signals(7) == ("", [])
    assert sa._load_student_generation_signals(99) == ("", [])


def test_signals_never_raise(monkeypatch):
    class Boom:
        def SessionLocal(self):
            raise RuntimeError("base HS")

    class Session:
        def query(self, *a):
            raise RuntimeError("base HS")

        def close(self):
            pass

    monkeypatch.setattr(sa, "Database", lambda: SimpleNamespace(SessionLocal=lambda: Session()))
    assert sa._load_student_generation_signals(7) == ("", [])


# ── POST /ai/generate-notification ──────────────────────────────────────────

URL_NOTIF = "/api/ai/generate-notification"
NOTIF = {"user_id": 7, "notification_type": "quiz_ready", "context_data": {"first_name": "Awa", "quiz": "Maths"}}


def test_notification_accepts_a_user_or_the_notification_service_token(env, headers_for, service_headers):
    env.use('{"title": "t", "body": "b"}')
    assert env.post(URL_NOTIF, json=NOTIF).status_code in (401, 403)
    assert env.post(URL_NOTIF, json=NOTIF, headers=headers_for(7)).status_code == 200
    assert env.post(URL_NOTIF, json={**NOTIF, "user_id": 8}, headers=service_headers("ai.notification")).status_code == 200
    assert env.post(URL_NOTIF, json={**NOTIF, "user_id": 9}, headers=service_headers("ai.task")).status_code in (401, 403)


def test_notification_body_validation(env, headers_for):
    assert env.post(URL_NOTIF, json={"user_id": 7}, headers=headers_for(7)).status_code == 422
    assert env.post(URL_NOTIF, json={**NOTIF, "context_data": "texte"}, headers=headers_for(7)).status_code == 422


def test_notification_is_cut_to_sixty_and_one_hundred_twenty_characters(env, headers_for):
    fake = env.use(json.dumps({"title": "T" * 100, "body": "B" * 200}))
    body = env.post(URL_NOTIF, json=NOTIF, headers=headers_for(7)).json()
    assert body == {"success": True, "title": "T" * 60, "body": "B" * 120}
    assert fake.calls[0]["max_tokens"] == 200 and fake.calls[0]["temperature"] == 0.8


def test_notification_accepts_a_fenced_json_answer(env, headers_for):
    env.use('```json\n{"title": "Salut", "body": "Corps"}\n```')
    assert env.post(URL_NOTIF, json=NOTIF, headers=headers_for(7)).json()["title"] == "Salut"


def test_notification_prompt_prefers_the_first_name_in_the_base(env, headers_for):
    env.db.add(dbm.User(Id=7, Email="a@x.test", FirstName="Marie"))
    fake = env.use('{"title": "t", "body": "b"}')
    env.post(URL_NOTIF, json=NOTIF, headers=headers_for(7))
    assert "Prénom de l'utilisateur : Marie" in fake.calls[0]["prompt"]
    assert "  quiz: Maths" in fake.calls[0]["prompt"] and "Type de notification : quiz_ready" in fake.calls[0]["prompt"]


def test_notification_prompt_falls_back_on_the_context_name_then_a_generic_word(env, headers_for):
    fake = env.use('{"title": "t", "body": "b"}')
    env.post(URL_NOTIF, json=NOTIF, headers=headers_for(7))
    env.post(URL_NOTIF, json={"user_id": 8, "notification_type": "x"}, headers=headers_for(8))
    assert "Prénom de l'utilisateur : Awa" in fake.calls[0]["prompt"]
    assert "Prénom de l'utilisateur : l'utilisateur" in fake.calls[1]["prompt"]


def test_notification_second_identical_call_is_served_from_the_cache(env, headers_for):
    fake = env.use('{"title": "t", "body": "b"}')
    first = env.post(URL_NOTIF, json=NOTIF, headers=headers_for(7)).json()
    second = env.post(URL_NOTIF, json=NOTIF, headers=headers_for(7)).json()
    assert first == second and len(fake.calls) == 1


def test_notification_cache_is_keyed_by_type_and_context(env, headers_for):
    fake = env.use('{"title": "t", "body": "b"}')
    env.post(URL_NOTIF, json=NOTIF, headers=headers_for(7))
    env.post(URL_NOTIF, json={**NOTIF, "notification_type": "autre"}, headers=headers_for(7))
    env.post(URL_NOTIF, json={**NOTIF, "context_data": {"quiz": "SVT"}}, headers=headers_for(7))
    assert len(fake.calls) == 3


def test_notification_fourth_distinct_request_of_the_day_is_429(env, headers_for):
    env.use('{"title": "t", "body": "b"}')
    for i in range(3):
        assert env.post(URL_NOTIF, json={**NOTIF, "notification_type": f"t{i}"}, headers=headers_for(7)).status_code == 200
    r = env.post(URL_NOTIF, json={**NOTIF, "notification_type": "t3"}, headers=headers_for(7))
    assert r.status_code == 429 and "max 3" in r.json()["detail"]
    assert env.post(URL_NOTIF, json={**NOTIF, "user_id": 8}, headers=headers_for(8)).status_code == 200


def test_notification_cached_answers_do_not_use_the_daily_allowance(env, headers_for):
    env.use('{"title": "t", "body": "b"}')
    for _ in range(6):
        assert env.post(URL_NOTIF, json=NOTIF, headers=headers_for(7)).status_code == 200


@pytest.mark.parametrize("answer", ["pas du json", "[1, 2]", RuntimeError("fournisseur HS"), TimeoutError("délai")])
def test_notification_provider_trouble_gives_a_soft_failure_not_an_error(env, headers_for, answer):
    env.use(answer)
    r = env.post(URL_NOTIF, json=NOTIF, headers=headers_for(7))
    assert r.status_code == 200 and r.json() == {"success": False, "title": None, "body": None}
    assert sa._notif_cache == {}                          # un échec n'est jamais mis en cache


# ── POST /ai/summarize-notifications ────────────────────────────────────────

URL_SUM = "/api/ai/summarize-notifications"


def test_summary_requires_texts(env, headers_for):
    env.use("• x")
    assert env.post(URL_SUM, json={}, headers=headers_for(7)).status_code == 422
    r = env.post(URL_SUM, json={"notification_texts": []}, headers=headers_for(7))
    assert r.status_code == 400 and "notification_texts" in r.json()["detail"]
    assert env.post(URL_SUM, json={"notification_texts": ["a"]}).status_code in (401, 403)


def test_summary_keeps_only_bullet_lines_and_counts_all_notifications(env, headers_for):
    fake = env.use("Voici :\n• Point un\n• Point deux\nConclusion")
    texts = [f"n{i}" for i in range(60)]
    body = env.post(URL_SUM, json={"notification_texts": texts}, headers=headers_for(7)).json()
    assert body == {"success": True, "bullets": ["• Point un", "• Point deux"], "count": 60}
    prompt = fake.calls[0]["prompt"]
    assert "50 notifications" in prompt and "- n49" in prompt and "- n50" not in prompt      # 50 premières seulement


def test_summary_without_bullets_prefixes_each_line_and_keeps_five(env, headers_for):
    env.use("\n".join(f"ligne {i}" for i in range(8)))
    bullets = env.post(URL_SUM, json={"notification_texts": ["a"]}, headers=headers_for(7)).json()["bullets"]
    assert bullets == [f"• ligne {i}" for i in range(5)]


def test_summary_provider_failure_is_a_clean_500(env, headers_for):
    env.use(TimeoutError("lent"))
    r = env.post(URL_SUM, json={"notification_texts": ["a"]}, headers=headers_for(7))
    assert r.status_code == 500 and r.json()["detail"] == "Impossible de générer le résumé."


# ── POST /ai/content-fit-analysis ───────────────────────────────────────────

URL_FIT = "/api/ai/content-fit-analysis"
FIT_JSON = {"fit_score": 0.8, "explanation": "Adapté.", "warning": None, "cta": "Très recommandé pour toi"}


def _subject(env):
    env.db.add(dbm.Subject(Id=5, Title="Physique", Category="Sciences", Description="d" * 300))


def test_fit_validates_and_requires_an_existing_content(env, headers_for):
    env.use(json.dumps(FIT_JSON))
    assert env.post(URL_FIT, json={"user_id": 7}, headers=headers_for(7)).status_code == 422
    r = env.post(URL_FIT, json={"user_id": 7, "content_id": 5}, headers=headers_for(7))
    assert r.status_code == 404 and r.json()["detail"] == "Contenu introuvable."


def test_fit_reports_the_model_verdict_and_the_user_history(env, headers_for):
    _subject(env)
    env.db.add(*[dbm.QuizAttempt(Id=i, UserId=7, Score=60 + i, CreatedAt=NOW - timedelta(days=i)) for i in range(1, 4)],
               dbm.QuizAttempt(Id=10, UserId=7, Score=0, CreatedAt=NOW - timedelta(days=200)),       # trop ancien
               dbm.QuizAttempt(Id=11, UserId=8, Score=0, CreatedAt=NOW))
    fake = env.use(json.dumps(FIT_JSON))
    body = env.post(URL_FIT, json={"user_id": 7, "content_id": 5}, headers=headers_for(7)).json()
    assert body["confidence_note"].startswith("Données limitées (3 quiz")
    assert {k: v for k, v in body.items() if k != "confidence_note"} == {
        "success": True, "fit_score": 0.8, "explanation": "Adapté.", "warning": None,
        "cta": "Très recommandé pour toi", "quiz_count": 3}
    assert "3 quiz tentés, score moyen 62%" in fake.calls[0]["prompt"] and "d" * 200 in fake.calls[0]["prompt"]
    assert "d" * 201 not in fake.calls[0]["prompt"]


def test_fit_without_history_and_with_enough_history(env, headers_for):
    _subject(env)
    fake = env.use(json.dumps(FIT_JSON))
    body = env.post(URL_FIT, json={"user_id": 7, "content_id": 5}, headers=headers_for(7)).json()
    assert body["quiz_count"] == 0 and body["confidence_note"].startswith("Données limitées (0 quiz")
    assert "Historique insuffisant" in fake.calls[0]["prompt"]
    env.db.add(*[dbm.QuizAttempt(Id=i, UserId=7, Score=50, CreatedAt=NOW - timedelta(days=1)) for i in range(1, 11)])
    body = env.post(URL_FIT, json={"user_id": 7, "content_id": 5}, headers=headers_for(7)).json()
    assert body["confidence_note"] == "Confiance élevée (10 données)"


@pytest.mark.parametrize("raw,expected", [(1.7, 1.0), (-0.4, 0.0), (0.456, 0.46), (0.5, 0.5)])
def test_fit_score_is_bounded_and_rounded(env, headers_for, raw, expected):
    _subject(env)
    env.use(json.dumps({**FIT_JSON, "fit_score": raw}))
    assert env.post(URL_FIT, json={"user_id": 7, "content_id": 5}, headers=headers_for(7)).json()["fit_score"] == expected


def test_fit_applies_defaults_and_cuts_the_explanation(env, headers_for):
    _subject(env)
    env.use('```json\n{"explanation": "' + "e" * 400 + '"}\n```')
    body = env.post(URL_FIT, json={"user_id": 7, "content_id": 5}, headers=headers_for(7)).json()
    assert body["fit_score"] == 0.5 and body["cta"] == "Recommandé" and body["warning"] is None
    assert len(body["explanation"]) == 300


@pytest.mark.parametrize("answer", ["pas du json", '{"fit_score": "beaucoup"}', TimeoutError("lent")])
def test_fit_unusable_answer_is_a_500(env, headers_for, answer):
    _subject(env)
    env.use(answer)
    r = env.post(URL_FIT, json={"user_id": 7, "content_id": 5}, headers=headers_for(7))
    assert r.status_code == 500 and r.json()["detail"] == "Analyse impossible pour le moment."


# ── POST /revisions/generate-content ────────────────────────────────────────

URL_REV = "/api/revisions/generate-content"
FICHE = {"title": "Les dérivées", "content_markdown": "## Cours\nTexte", "difficulty": "hard",
         "estimated_duration_minutes": 20}


def _rev(env, headers_for, **kw):
    return env.post(URL_REV, json={"user_id": 7, **kw}, headers=headers_for(7))


def test_revision_requires_a_token_and_a_user_id(env, headers_for):
    env.use(json.dumps(FICHE))
    assert env.post(URL_REV, json={"user_id": 7}).status_code in (401, 403)
    assert env.post(URL_REV, json={}, headers=headers_for(7)).status_code == 422


def test_revision_returns_the_sheet_with_the_requested_subject(env, headers_for):
    fake = env.use("Voici la fiche : " + json.dumps(FICHE) + " Bonne étude.")
    r = _rev(env, headers_for, subject="Maths")
    assert r.json() == {"success": True, "subject": "Maths", "title": "Les dérivées",
                        "content_markdown": "## Cours\nTexte", "difficulty": "hard", "estimated_duration_minutes": 20}
    assert fake.calls[0]["max_tokens"] == 3200 and fake.calls[0]["temperature"] == 0.5


def test_revision_prompt_lists_recent_mistakes_downloads_and_goals(env, headers_for):
    env.db.add(dbm.QuizMistake(Id=1, UserId=7, Subject="Maths", Question="Q" * 300, GivenAnswer="A", CorrectAnswer="B",
                               CreatedAt=NOW - timedelta(days=3)),
               dbm.QuizMistake(Id=2, UserId=7, Subject="Maths", Question="Ancienne", CreatedAt=NOW - timedelta(days=90)),
               dbm.QuizMistake(Id=3, UserId=7, Subject="SVT", Question="Autre matière", CreatedAt=NOW),
               dbm.QuizMistake(Id=4, UserId=8, Subject="Maths", Question="Autre élève", CreatedAt=NOW),
               dbm.Subject(Id=1, Title="BAC 2023 Maths"), dbm.DownloadHistory(Id=1, UserId=7, SubjectId=1, CreatedAt=NOW),
               dbm.Goal(Id=1, UserId=7, Title="Mention bien", Status="active"),
               dbm.Goal(Id=2, UserId=7, Title="Terminé", Status="done"))
    fake = env.use(json.dumps(FICHE))
    _rev(env, headers_for, subject="Maths", level="Terminale", topic="Limites", difficulty=" HARD ")
    prompt = fake.calls[0]["prompt"]
    assert "- " + "Q" * 200 + " (réponse donnée : A) (bonne réponse : B)" in prompt and "Q" * 201 not in prompt
    for absent in ("Ancienne", "Autre matière", "Autre élève", "Terminé"):
        assert absent not in prompt
    assert "BAC 2023 Maths" in prompt and "Mention bien" in prompt
    assert "Niveau scolaire de l'élève : Terminale." in prompt and "Sous-thème demandé : Limites" in prompt
    assert "difficulté hard" in prompt


def test_revision_prompt_defaults_when_the_student_has_no_history(env, headers_for):
    fake = env.use(json.dumps(FICHE))
    _rev(env, headers_for, subject="Maths", difficulty="impossible")
    prompt = fake.calls[0]["prompt"]
    assert "Aucune erreur récente enregistrée" in prompt and "aucune épreuve téléchargée récemment" in prompt
    assert "aucun objectif défini" in prompt and "Calibre la fiche" not in prompt


def test_revision_without_subject_lets_the_model_choose_it(env, headers_for):
    fake = env.use(json.dumps({**FICHE, "subject": "Chimie"}))
    body = _rev(env, headers_for, context_hint="Prépare le BEPC").json()
    assert body["subject"] == "Chimie"
    assert "Prépare le BEPC" in fake.calls[0]["prompt"] and '"subject": "..."' in fake.calls[0]["prompt"]
    fake2 = env.use(json.dumps(FICHE))
    _rev(env, headers_for)
    assert "aucun indice disponible" in fake2.calls[0]["prompt"]


def test_revision_includes_style_and_enrolled_subjects_when_known(env, headers_for):
    style = next(iter(sa._VARK_INSTRUCTIONS))
    env.db.add(dbm.ChatbotContext(Id=1, UserId=7, LearningStyle=style), dbm.Subject(Id=1, Title="Anglais"),
               dbm.Enrollment(Id=1, UserId=7, SubjectId=1))
    fake = env.use(json.dumps(FICHE))
    _rev(env, headers_for, subject="Maths")
    assert sa._VARK_INSTRUCTIONS[style] in fake.calls[0]["prompt"]
    assert "inscrit : Anglais." in fake.calls[0]["prompt"]


def test_revision_defaults_for_title_difficulty_and_duration(env, headers_for):
    env.use(json.dumps({"content_markdown": "texte", "difficulty": "extrême", "estimated_duration_minutes": None}))
    body = _rev(env, headers_for, subject="Maths", difficulty="easy").json()
    assert body["title"].startswith("Révision") and body["title"].endswith("Maths")
    assert body["difficulty"] == "easy" and body["estimated_duration_minutes"] == 15
    env.use(json.dumps({"content_markdown": "texte"}))
    assert _rev(env, headers_for).json()["title"] == "Fiche de révision"
    assert _rev(env, headers_for).json()["difficulty"] == "medium"


def test_revision_title_is_cut_at_255_characters(env, headers_for):
    env.use(json.dumps({**FICHE, "title": "t" * 400}))
    assert len(_rev(env, headers_for, subject="Maths").json()["title"]) == 255


def test_revision_retries_once_with_a_larger_budget(env, headers_for):
    fake = env.use("réponse tronquée {\"title\": \"coupé", json.dumps(FICHE))
    r = _rev(env, headers_for, subject="Maths")
    assert r.status_code == 200 and [c["max_tokens"] for c in fake.calls] == [3200, 4200]


@pytest.mark.parametrize("answers", [
    ("rien", "rien"), (json.dumps({"title": "x", "content_markdown": "  "}),) * 2,
    (TimeoutError("lent"), RuntimeError("HS")), ("{pas json}", "{pas json}")])
def test_revision_fails_with_500_after_two_attempts(env, headers_for, answers):
    fake = env.use(*answers)
    r = _rev(env, headers_for, subject="Maths")
    assert r.status_code == 500 and r.json()["detail"] == "Génération de la fiche impossible pour le moment."
    assert len(fake.calls) == 2


# ── POST /quizzes/generate-content ──────────────────────────────────────────

URL_QUIZ = "/api/quizzes/generate-content"


def _q(n, correct=None, **kw):
    options = [f"A) {n}a", f"B) {n}b", f"C) {n}c", f"D) {n}d"]
    q = {"id": f"q{n}", "question": f" Question {n} ? ", "options": options, "correctAnswer": correct or options[1],
         "explanation": " Parce que. "}
    q.update(kw)
    return q


def _quiz(env, headers_for, **kw):
    return env.post(URL_QUIZ, json={"user_id": 7, **kw}, headers=headers_for(7))


def test_quiz_requires_a_token_and_a_user_id(env, headers_for):
    env.use(json.dumps({"questions": [_q(1)]}))
    assert env.post(URL_QUIZ, json={"user_id": 7}).status_code in (401, 403)
    assert env.post(URL_QUIZ, json={}, headers=headers_for(7)).status_code == 422
    assert env.post(URL_QUIZ, json={"user_id": 7, "recent_question_texts": "x"}, headers=headers_for(7)).status_code == 422


def test_quiz_returns_cleaned_questions_and_the_difficulty(env, headers_for):
    fake = env.use("Voici : " + json.dumps({"questions": [_q(1), _q(2)]}))
    body = _quiz(env, headers_for, subject="Maths").json()
    assert body["success"] is True and body["data"]["subject"] == "Maths" and body["data"]["difficulty"] == "medium"
    assert body["data"]["questions"][0] == {"id": "q1", "question": "Question 1 ?", "options": _q(1)["options"],
                                            "correctAnswer": "B) 1b", "explanation": "Parce que."}
    assert fake.calls[0]["max_tokens"] == 2800 and "WinAI" in fake.calls[0]["system"]
    assert "exactement 8 questions" in fake.calls[0]["prompt"]


def test_quiz_drops_questions_whose_answer_is_not_one_of_the_options(env, headers_for):
    payload = {"questions": [_q(1), _q(2, correct="Z) inconnue"), _q(3, options=[], correctAnswer=""), _q(4)]}
    env.use(json.dumps(payload))
    ids = [q["id"] for q in _quiz(env, headers_for, subject="Maths").json()["data"]["questions"]]
    assert ids == ["q1", "q4"]


def test_quiz_keeps_at_most_eight_questions_and_generates_missing_ids(env, headers_for):
    questions = [_q(i) for i in range(1, 12)]
    questions[0]["id"] = None
    env.use(json.dumps({"questions": questions}))
    result = _quiz(env, headers_for, subject="Maths").json()["data"]["questions"]
    assert len(result) == 8 and result[0]["id"] == "q1"


def test_quiz_without_subject_takes_the_one_chosen_by_the_model(env, headers_for):
    fake = env.use(json.dumps({"subject": "Géographie", "questions": [_q(1)]}))
    body = _quiz(env, headers_for, context_hint="Objectif BEPC").json()
    assert body["data"]["subject"] == "Géographie" and "Objectif BEPC" in fake.calls[0]["prompt"]


@pytest.mark.parametrize("given,expected", [("easy", "easy"), ("HARD", "hard"), (" Medium ", "medium"),
                                            ("impossible", "medium"), (None, "medium")])
def test_quiz_difficulty_is_normalised(env, headers_for, given, expected):
    fake = env.use(json.dumps({"questions": [_q(1)]}))
    body = _quiz(env, headers_for, subject="Maths", difficulty=given).json()
    assert body["data"]["difficulty"] == expected
    marker = {"easy": "FACILES", "hard": "DIFFICILES", "medium": "Mélange"}[expected]
    assert marker in fake.calls[0]["prompt"]


def test_quiz_prompt_targets_recent_mistakes_and_avoids_recent_questions(env, headers_for):
    env.db.add(dbm.QuizMistake(Id=1, UserId=7, Subject="Maths", Question="Erreur récente", CreatedAt=NOW - timedelta(days=2)),
               dbm.QuizMistake(Id=2, UserId=7, Subject="Maths", Question="Erreur ancienne", CreatedAt=NOW - timedelta(days=90)),
               dbm.QuizMistake(Id=3, UserId=8, Subject="Maths", Question="Erreur d'un autre", CreatedAt=NOW))
    fake = env.use(json.dumps({"questions": [_q(1)]}))
    _quiz(env, headers_for, subject="Maths", level="3e", topic="Fractions",
          recent_question_texts=[f"déjà vu {i}" + "x" * 300 for i in range(20)])
    prompt = fake.calls[0]["prompt"]
    assert "- Erreur récente" in prompt and "Erreur ancienne" not in prompt and "Erreur d'un autre" not in prompt
    assert "déjà vu 14" in prompt and "déjà vu 15" not in prompt            # quinze énoncés au plus
    assert "x" * 201 not in prompt
    assert "Niveau scolaire de l'élève : 3e." in prompt and "Sous-thème : Fractions" in prompt


def test_quiz_prompt_without_mistakes_asks_for_fundamentals(env, headers_for):
    fake = env.use(json.dumps({"questions": [_q(1)]}))
    _quiz(env, headers_for, subject="Maths")
    assert "Aucune erreur récente enregistrée" in fake.calls[0]["prompt"]


@pytest.mark.parametrize("answer", [json.dumps({"questions": []}), json.dumps({"questions": [_q(1, correct="Z")]}),
                                    "du texte", "{pas json}", json.dumps({"subject": "x"})])
def test_quiz_without_any_valid_question_is_a_422_to_retry(env, headers_for, answer):
    env.use(answer)
    r = _quiz(env, headers_for, subject="Maths")
    assert r.status_code == 422 and "Réessaie" in r.json()["detail"]


@pytest.mark.parametrize("answer", [TimeoutError("lent"), json.dumps({"questions": ["pas un objet"]}),
                                    json.dumps({"questions": 5})])
def test_quiz_provider_failure_or_malformed_questions_is_a_500(env, headers_for, answer):
    env.use(answer)
    r = _quiz(env, headers_for, subject="Maths")
    assert r.status_code == 500 and r.json()["detail"] == "Génération du quiz impossible pour le moment."
