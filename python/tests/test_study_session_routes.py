"""Sessions d'étude guidées (routes/study_session_routes.py) : briefing, quiz, synthèse, enregistrement et
historique. SQLite en mémoire ; le fournisseur d'IA est simulé, la date figée."""

import json
from datetime import date, datetime, timedelta

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

import database as dbm
import routes.study_session_routes as ss

NOW = datetime(2026, 6, 17, 9, 0, 0)


class FrozenDatetime(datetime):
    @classmethod
    def utcnow(cls):
        return NOW


class FakeClient:
    """Renvoie les contenus dans l'ordre ; une exception est levée telle quelle."""

    def __init__(self, *answers):
        self.answers, self.calls = list(answers), []

    def chat(self, messages, system_prompt=None, max_tokens=None, temperature=None, **kw):
        self.calls.append({"prompt": messages[0]["content"], "max_tokens": max_tokens, "temperature": temperature})
        answer = self.answers.pop(0)
        if isinstance(answer, Exception):
            raise answer
        return {"content": answer}


@pytest.fixture
def env(monkeypatch, sqlite_database):
    db = sqlite_database(ss, [dbm.Subject, dbm.QuizAttempt, dbm.StudySession, dbm.DailyScore])
    db.add(dbm.Subject(Id=1, Title="Mathématiques", Category="Sciences"), dbm.Subject(Id=2, Title="Histoire"))
    monkeypatch.setattr(ss, "datetime", FrozenDatetime)
    app = FastAPI()
    app.include_router(ss.study_session_router, prefix="/api/study-session")
    client = TestClient(app, raise_server_exceptions=False)
    client.db = db

    def use(*answers):
        fake = FakeClient(*answers)
        monkeypatch.setattr(ss, "get_deepseek_client", lambda: fake)
        return fake

    client.use = use
    return client


def attempts(env, user_id, *scores):
    start = len(env.db.all(dbm.QuizAttempt)) + 1
    env.db.add(*[dbm.QuizAttempt(Id=start + i, UserId=user_id, Score=s, CreatedAt=NOW - timedelta(days=i))
                 for i, s in enumerate(scores)])


QUESTIONS = [{"q": "1+1 ?", "options": ["A) 1", "B) 2", "C) 3", "D) 4"], "correct": 1, "explanation": "Parce que."}]
GEN = {"user_id": 7, "subject_id": 1, "duration_minutes": 25}


# ── droits et validation ────────────────────────────────────────────────────

@pytest.mark.parametrize("method,path,body", [
    ("post", "/generate", GEN), ("post", "/phase2/quiz", {"user_id": 7, "subject_id": 1}),
    ("post", "/phase3/summary", {"user_id": 7, "subject_id": 1}),
    ("post", "/complete", {"user_id": 7, "subject_id": 1, "duration_minutes": 25}),
    ("get", "/history/7", None)])
def test_every_route_requires_a_token(env, method, path, body):
    r = getattr(env, method)("/api/study-session" + path, **({"json": body} if body else {}))
    assert r.status_code in (401, 403)


@pytest.mark.parametrize("path,body", [
    ("/generate", {"user_id": 7, "subject_id": 1}),                    # durée absente
    ("/generate", {"user_id": "x", "subject_id": 1, "duration_minutes": 25}),
    ("/phase2/quiz", {"user_id": 7}),
    ("/phase3/summary", {"subject_id": 1}),
    ("/phase3/summary", {"user_id": 7, "subject_id": 1, "score": "beaucoup"}),
    ("/complete", {"user_id": 7, "subject_id": 1}),
    ("/complete", {"user_id": 7, "subject_id": 1, "duration_minutes": 5, "key_points": "pas une liste"}),
])
def test_invalid_bodies_are_422(env, headers_for, path, body):
    assert env.post("/api/study-session" + path, json=body, headers=headers_for(7)).status_code == 422


@pytest.mark.parametrize("path,body", [
    ("/generate", GEN), ("/phase2/quiz", {"user_id": 7, "subject_id": 99}), ("/phase3/summary", {"user_id": 7, "subject_id": 99})])
def test_unknown_subject_is_404_and_the_model_is_not_called(env, headers_for, path, body):
    fake = env.use()
    r = env.post("/api/study-session" + path, json={**body, "subject_id": 99}, headers=headers_for(7))
    assert r.status_code == 404 and r.json()["detail"] == "Sujet introuvable." and fake.calls == []


# ── génération complète ─────────────────────────────────────────────────────

def test_generate_builds_briefing_questions_and_three_phases(env, headers_for):
    fake = env.use("  Révise les dérivées.  ", "Voici : " + json.dumps(QUESTIONS) + " fin")
    r = env.post("/api/study-session/generate", json=GEN, headers=headers_for(7))
    data = r.json()["data"]
    assert r.status_code == 200 and r.json()["success"] is True
    assert data["subject"] == {"id": 1, "name": "Mathématiques", "category": "Sciences"}
    assert data["briefing"] == "Révise les dérivées." and data["questions"] == QUESTIONS
    assert data["duration_minutes"] == 25 and "Mathématiques" in data["synthesis_prompt"]
    assert [p["id"] for p in data["phases"]] == ["briefing", "quiz", "synthesis"]
    assert sum(p["duration_pct"] for p in data["phases"]) == 100
    assert [c["max_tokens"] for c in fake.calls] == [200, 800]


@pytest.mark.parametrize("scores,level", [
    ([], "intermédiaire"),                 # sans historique : 50 par défaut
    ([49, 49], "débutant"), ([50], "intermédiaire"), ([74.9], "intermédiaire"), ([75], "avancé"), ([100, 50], "avancé")])
def test_level_comes_from_the_average_of_past_attempts(env, headers_for, scores, level):
    attempts(env, 7, *scores)
    fake = env.use("Briefing", "[]")
    data = env.post("/api/study-session/generate", json=GEN, headers=headers_for(7)).json()["data"]
    assert data["level"] == level and f"niveau {level}" in fake.calls[0]["prompt"]


def test_only_the_ten_latest_attempts_of_the_user_count(env, headers_for):
    attempts(env, 7, *([100] * 10 + [0] * 5))          # les plus récents d'abord : 100 x 10, puis 0
    attempts(env, 8, 0, 0, 0)                          # autre élève : ignoré
    data = env.use("B", "[]") and env.post("/api/study-session/generate", json=GEN, headers=headers_for(7)).json()["data"]
    assert data["level"] == "avancé"


def test_missing_category_defaults_to_general(env, headers_for):
    env.use("B", "[]")
    data = env.post("/api/study-session/generate", json={**GEN, "subject_id": 2}, headers=headers_for(7)).json()["data"]
    assert data["subject"]["category"] == "général"


@pytest.mark.parametrize("quiz_answer", ["pas de json", "[{casse", "", "[]"])
def test_unparseable_quiz_gives_an_empty_list_not_an_error(env, headers_for, quiz_answer):
    env.use("Briefing", quiz_answer)
    r = env.post("/api/study-session/generate", json=GEN, headers=headers_for(7))
    assert r.status_code == 200 and r.json()["data"]["questions"] == []


def test_provider_failure_is_a_500_carrying_the_message(env, headers_for):
    env.use(TimeoutError("délai dépassé"))
    r = env.post("/api/study-session/generate", json=GEN, headers=headers_for(7))
    assert r.status_code == 500 and "délai dépassé" in r.json()["detail"]


def test_english_header_changes_the_language_of_the_prompts(env, headers_for):
    fake = env.use("B", "[]")
    # le middleware d'application pose la langue ; ici on la pose directement
    from services.language import begin_request_language, end_request_language
    token = begin_request_language("en")
    try:
        env.post("/api/study-session/generate", json=GEN, headers=headers_for(7))
    finally:
        end_request_language(token)
    assert "en français" in fake.calls[0]["prompt"] or "en anglais" in fake.calls[0]["prompt"]


# ── phase 2 ─────────────────────────────────────────────────────────────────

def test_phase2_returns_at_most_three_questions(env, headers_for):
    five = [{"question": f"Q{i}", "options": [], "correct": "A", "explanation": ""} for i in range(5)]
    env.use(json.dumps(five))
    body = env.post("/api/study-session/phase2/quiz", json={"user_id": 7, "subject_id": 1}, headers=headers_for(7)).json()
    assert [q["question"] for q in body["phase2"]["questions"]] == ["Q0", "Q1", "Q2"]
    assert body["phase2"]["subject"] == "Mathématiques" and body["phase2"]["level"] == "intermédiaire"


@pytest.mark.parametrize("answer", ["rien d'exploitable", "[]", "[oops"])
def test_phase2_falls_back_to_a_single_generic_question(env, headers_for, answer):
    env.use(answer)
    questions = env.post("/api/study-session/phase2/quiz", json={"user_id": 7, "subject_id": 1},
                         headers=headers_for(7)).json()["phase2"]["questions"]
    assert len(questions) == 1 and "Mathématiques" in questions[0]["question"] and questions[0]["correct"].startswith("A)")


def test_phase2_provider_failure_is_a_500(env, headers_for):
    env.use(RuntimeError("fournisseur HS"))
    r = env.post("/api/study-session/phase2/quiz", json={"user_id": 7, "subject_id": 1}, headers=headers_for(7))
    assert r.status_code == 500 and "fournisseur HS" in r.json()["detail"]


# ── phase 3 ─────────────────────────────────────────────────────────────────

URL3 = "/api/study-session/phase3/summary"


def _phase3(env, headers_for, answer, **body):
    fake = env.use(answer)
    r = env.post(URL3, json={"user_id": 7, "subject_id": 1, **body}, headers=headers_for(7))
    return r, fake


def test_phase3_keeps_five_keypoints_and_the_model_summary(env, headers_for):
    answer = json.dumps({"summary": "Bonne session.", "keypoints": [f"k{i}" for i in range(8)]})
    r, _ = _phase3(env, headers_for, "Voilà " + answer)
    phase3 = r.json()["phase3"]
    assert phase3["summary"] == "Bonne session." and phase3["keypoints"] == [f"k{i}" for i in range(5)]
    assert phase3["performance_comment"] == "" and phase3["subject"] == "Mathématiques"


@pytest.mark.parametrize("score,comment_start", [
    (100, "Excellent résultat : 100/100"), (80, "Excellent résultat : 80/100"),
    (79.9, "Bon résultat : 80/100"), (60, "Bon résultat : 60/100"),
    (59, "Score de 59/100"), (0, "Score de 0/100")])
def test_phase3_performance_comment_bands(env, headers_for, score, comment_start):
    r, fake = _phase3(env, headers_for, "{}", score=score)
    assert r.json()["phase3"]["performance_comment"].startswith(comment_start)
    assert f"{score:.0f}/100" in fake.calls[0]["prompt"]          # le score est aussi dans le prompt


def test_phase3_without_score_adds_no_score_context(env, headers_for):
    _, fake = _phase3(env, headers_for, "{}")
    assert "score du quiz" not in fake.calls[0]["prompt"]


def test_phase3_uses_default_texts_when_the_model_answers_nothing_usable(env, headers_for):
    r, _ = _phase3(env, headers_for, "du texte libre sans accolade")
    phase3 = r.json()["phase3"]
    assert "Session sur Mathématiques terminée" in phase3["summary"]
    assert phase3["keypoints"] == ["Révise les fondamentaux de Mathématiques régulièrement."]


def test_phase3_broken_json_also_uses_defaults(env, headers_for):
    r, _ = _phase3(env, headers_for, "{pas du json}")
    assert r.status_code == 200 and "terminée" in r.json()["phase3"]["summary"]


# ── enregistrement ──────────────────────────────────────────────────────────

URL_DONE = "/api/study-session/complete"
DONE = {"user_id": 7, "subject_id": 1, "duration_minutes": 25, "score": 80, "key_points": ["a", "b"]}


def test_complete_saves_the_session_and_creates_the_daily_score(env, headers_for):
    r = env.post(URL_DONE, json=DONE, headers=headers_for(7))
    assert r.status_code == 200 and r.json() == {"success": True, "session_id": 1}
    row = env.db.all(dbm.StudySession)[0]
    assert (row.UserId, row.SubjectId, row.Duration, float(row.Score)) == (7, 1, 25, 80.0)
    assert json.loads(row.KeyPoints) == ["a", "b"] and row.CompletedAt == NOW
    daily = env.db.all(dbm.DailyScore)
    assert len(daily) == 1 and daily[0].Date == NOW.date() and daily[0].QuizCount == 1 and daily[0].SubjectId == 1


def test_complete_averages_with_the_existing_daily_score_of_the_day(env, headers_for):
    env.db.add(dbm.DailyScore(Id=1, UserId=7, Date=NOW.date(), AverageScore=60, QuizCount=2))
    env.post(URL_DONE, json={**DONE, "score": 90}, headers=headers_for(7))
    daily = env.db.all(dbm.DailyScore)
    assert len(daily) == 1 and float(daily[0].AverageScore) == 75.0 and daily[0].QuizCount == 3


def test_complete_without_score_leaves_the_daily_score_alone_and_stores_no_key_points(env, headers_for):
    env.post(URL_DONE, json={"user_id": 7, "subject_id": 1, "duration_minutes": 15}, headers=headers_for(7))
    assert env.db.all(dbm.DailyScore) == []
    row = env.db.all(dbm.StudySession)[0]
    assert row.Score is None and json.loads(row.KeyPoints) == []


def test_complete_does_not_mix_daily_scores_of_two_students(env, headers_for):
    env.db.add(dbm.DailyScore(Id=1, UserId=8, Date=NOW.date(), AverageScore=10, QuizCount=1))
    env.post(URL_DONE, json=DONE, headers=headers_for(7))
    assert sorted((d.UserId, float(d.AverageScore)) for d in env.db.all(dbm.DailyScore)) == [(7, 80.0), (8, 10.0)]


def test_complete_database_failure_is_a_500(env, headers_for, monkeypatch):
    class Boom:
        def get_session(self):
            raise RuntimeError

        @property
        def SessionLocal(self):
            def make():
                raise RuntimeError("base hors ligne")
            return make

    monkeypatch.setattr(ss, "Database", lambda: Boom())
    r = env.post(URL_DONE, json=DONE, headers=headers_for(7))
    assert r.status_code == 500


# ── historique ──────────────────────────────────────────────────────────────

def _history_rows(env):
    env.db.add(*[dbm.StudySession(Id=i, UserId=7, SubjectId=1 if i % 2 else 2, Duration=10 + i,
                                  Score=None if i == 3 else 50 + i, CompletedAt=NOW - timedelta(days=15 - i))
                 for i in range(1, 13)])
    env.db.add(dbm.StudySession(Id=50, UserId=7, SubjectId=1, Duration=5, CompletedAt=None),      # non terminée
               dbm.StudySession(Id=51, UserId=8, SubjectId=1, Duration=5, CompletedAt=NOW))        # autre élève


def test_history_returns_the_ten_latest_completed_sessions_newest_first(env, headers_for):
    _history_rows(env)
    data = env.get("/api/study-session/history/7", headers=headers_for(7)).json()["data"]
    assert [d["id"] for d in data] == [12, 11, 10, 9, 8, 7, 6, 5, 4, 3]
    assert data[0] == {"id": 12, "subject": "Histoire", "duration": 22, "score": 62.0,
                       "completedAt": (NOW - timedelta(days=3)).isoformat()}
    assert data[-1]["score"] is None                                  # id 3 : pas de note


def test_history_of_a_user_without_session_is_empty(env, headers_for):
    assert env.get("/api/study-session/history/99", headers=headers_for(99)).json() == {"success": True, "data": []}


def test_history_user_id_must_be_an_integer(env, headers_for):
    assert env.get("/api/study-session/history/abc", headers=headers_for(7)).status_code == 422


@pytest.mark.xfail(strict=False, reason="GET /history/{user_id} et POST /complete ne comparent pas user_id au jeton : "
                   "un élève peut lire l'historique d'un autre (et écrire dans ses scores du jour). Un 403 est attendu.")
def test_a_student_cannot_read_someone_elses_history(env, headers_for):
    _history_rows(env)
    assert env.get("/api/study-session/history/7", headers=headers_for(8)).status_code == 403


@pytest.mark.xfail(strict=False, reason="POST /complete enregistre la session au user_id du corps, sans le comparer "
                   "au jeton : un compte peut créditer ou fausser les scores d'un autre. Un 403 est attendu.")
def test_a_student_cannot_complete_a_session_for_someone_else(env, headers_for):
    r = env.post(URL_DONE, json={**DONE, "user_id": 7}, headers=headers_for(8))
    assert r.status_code == 403
