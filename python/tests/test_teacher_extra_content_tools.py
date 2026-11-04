"""Routes IA du professeur, première moitié (routes/teacher_extra_routes.py) : génération de QCM, titre, description,
analyse de classe, impact d'un contenu, correction, prédiction de popularité, analyse d'une copie. SQLite en mémoire,
fournisseur d'IA simulé, date figée."""

import json
from datetime import datetime, timedelta, timezone
from types import SimpleNamespace

import pytest
from fastapi import FastAPI, HTTPException
from fastapi.testclient import TestClient

import database as dbm
import routes.teacher_extra_routes as tx

UTC = timezone.utc


class Clock:
    current = datetime(2026, 6, 17, 12, 0, 0)


class FrozenDatetime(datetime):
    @classmethod
    def now(cls, tz=None):
        return Clock.current.replace(tzinfo=tz) if tz else Clock.current


@pytest.fixture(autouse=True)
def frozen(monkeypatch):
    Clock.current = datetime(2026, 6, 17, 12, 0, 0)
    monkeypatch.setattr(tx, "datetime", FrozenDatetime)


@pytest.fixture
def env(monkeypatch, sqlite_database):
    db = sqlite_database(tx, [dbm.Subject, dbm.Enrollment, dbm.DailyScore, dbm.QuizAttempt, dbm.TeacherClass,
                              dbm.TeacherClassStudent, dbm.User])
    app = FastAPI()
    app.include_router(tx.teacher_ai_router, prefix="/api")
    client = TestClient(app, raise_server_exceptions=False)
    client.db = db
    return client


class Calls(list):
    pass


def fake_json(monkeypatch, value):
    calls = Calls()

    def fake(prompt, system, max_tokens=600):
        calls.append({"prompt": prompt, "system": system, "max_tokens": max_tokens})
        return value

    monkeypatch.setattr(tx, "_deepseek_json", fake)
    return calls


def fake_text(monkeypatch, value):
    calls = Calls()

    def fake(prompt, system, max_tokens=200):
        calls.append({"prompt": prompt, "system": system, "max_tokens": max_tokens})
        return value

    monkeypatch.setattr(tx, "_deepseek_text", fake)
    return calls


def teacher(headers_for, uid=3):
    return headers_for(uid, "teacher")


# ── aides communes ──────────────────────────────────────────────────────────

class Client:
    def __init__(self, content=None, error=None):
        self.content, self.error, self.calls = content, error, []

    def chat(self, **kw):
        self.calls.append(kw)
        if self.error:
            raise self.error
        return {"content": self.content}


@pytest.mark.parametrize("content,expected", [('{"a": 1}', {"a": 1}), ('```json\n[1]\n```', [1]), ("rien", None), ("", None)])
def test_json_helper_reads_fenced_json_and_asks_for_json_mode(monkeypatch, content, expected):
    client = Client(content)
    monkeypatch.setattr(tx, "get_deepseek_client", lambda: client)
    assert tx._deepseek_json("p", "s", 123) == expected
    assert client.calls[0]["json_mode"] is True and client.calls[0]["max_tokens"] == 123


def test_helpers_swallow_provider_errors(monkeypatch):
    monkeypatch.setattr(tx, "get_deepseek_client", lambda: Client(error=TimeoutError("lent")))
    assert tx._deepseek_json("p", "s") is None and tx._deepseek_text("p", "s") == ""


def test_text_helper_strips(monkeypatch):
    monkeypatch.setattr(tx, "get_deepseek_client", lambda: Client("  texte \n"))
    assert tx._deepseek_text("p", "s") == "texte"


# ── _parse_quiz_questions ───────────────────────────────────────────────────

GOOD_Q = {"id": "q1", "text": "2+2 ?", "options": [{"id": "a", "text": "3"}, {"id": "b", "text": "4"}],
          "correctOptionId": "b", "explanation": "Addition."}


def test_parse_keeps_well_formed_questions_and_defaults_missing_fields():
    parsed = tx._parse_quiz_questions([GOOD_Q, {"options": []}], 10)
    assert parsed[0].correctOptionId == "b" and parsed[0].options[1].text == "4"
    assert parsed[1].id == "q2" and parsed[1].correctOptionId == "a" and parsed[1].text == ""


def test_parse_drops_malformed_entries_and_respects_the_limit():
    raw = [GOOD_Q, "texte", {"options": [{"id": "a"}]}, None, GOOD_Q, GOOD_Q]
    assert len(tx._parse_quiz_questions(raw, 10)) == 3
    assert len(tx._parse_quiz_questions([GOOD_Q] * 5, 2)) == 2
    assert tx._parse_quiz_questions({"questions": []}, 10) == [] and tx._parse_quiz_questions(None, 10) == []


# ── droits (toutes les routes de ce fichier) ────────────────────────────────

ROLE_ROUTES = [
    ("post", "/api/ai/generate-quiz-questions", {}), ("post", "/api/ai/optimize-title", {"title": "t"}),
    ("post", "/api/ai/generate-description", {"title": "t"}),
    ("post", "/api/teacher/class-analysis", {"teacher_id": 1, "content_id": 1}),
    ("get", "/api/teacher/content-impact/1", None), ("post", "/api/teacher/generate-correction", {"exam_text": "x"}),
    ("post", "/api/teacher/predict-popularity", {}), ("post", "/api/teacher/analyze-submission", {"question_type": "mcq"}),
]


@pytest.mark.parametrize("method,url,body", ROLE_ROUTES)
def test_routes_need_a_token_and_the_teacher_or_admin_role(env, headers_for, monkeypatch, method, url, body):
    fake_json(monkeypatch, None)
    fake_text(monkeypatch, "")
    send = lambda **kw: getattr(env, method)(url, **({"json": body} if body is not None else {}), **kw)
    assert send().status_code in (401, 403)
    for role in ("student", "parent", "institution"):
        assert send(headers=headers_for(1, role)).status_code == 403
    assert send(headers=headers_for(1, "teacher")).status_code in (200, 404)
    assert send(headers=headers_for(1, "admin")).status_code in (200, 404)


# ── POST /ai/generate-quiz-questions ────────────────────────────────────────

URL_QUIZ = "/api/ai/generate-quiz-questions"


def test_quiz_returns_parsed_questions_and_builds_the_prompt(env, headers_for, monkeypatch):
    calls = fake_json(monkeypatch, [GOOD_Q])
    r = env.post(URL_QUIZ, json={"topic": "Limites", "subject": "Maths", "level": "Tle C"}, headers=teacher(headers_for))
    assert r.status_code == 200 and r.json()["questions"][0]["correctOptionId"] == "b"
    assert "niveau Tle C en Maths" in calls[0]["prompt"] and "thème : Limites" in calls[0]["prompt"]
    assert calls[0]["max_tokens"] == 3000


def test_quiz_joins_the_topics_list_when_no_single_topic_is_given(env, headers_for, monkeypatch):
    calls = fake_json(monkeypatch, [GOOD_Q])
    env.post(URL_QUIZ, json={"topics": ["Limites", "Suites"]}, headers=teacher(headers_for))
    assert "thème : Limites, Suites" in calls[0]["prompt"]


@pytest.mark.parametrize("answer", [None, [], "texte", [{"options": [{"id": "a"}]}]])
def test_quiz_falls_back_on_five_placeholder_questions_that_say_so(env, headers_for, monkeypatch, answer):
    fake_json(monkeypatch, answer)
    questions = env.post(URL_QUIZ, json={"topic": "Limites"}, headers=teacher(headers_for)).json()["questions"]
    assert len(questions) == 5 and "Limites" in questions[0]["text"] and "n'a pas pu générer" in questions[0]["explanation"]


def test_quiz_for_a_student_of_another_teachers_class_is_403(env, headers_for, monkeypatch):
    calls = fake_json(monkeypatch, [GOOD_Q])
    env.db.add(dbm.TeacherClass(Id=1, TeacherId=99), dbm.TeacherClassStudent(Id=1, TeacherClassId=1, StudentId=50))
    r = env.post(URL_QUIZ, json={"topic": "x", "student_id": 50}, headers=teacher(headers_for))
    assert r.status_code == 403 and "aucune de vos classes" in r.json()["detail"] and calls == []


def test_quiz_for_a_student_of_my_class_uses_the_student_profile(env, headers_for, monkeypatch):
    calls = fake_json(monkeypatch, [GOOD_Q])
    monkeypatch.setattr(tx, "_load_student_generation_signals", lambda uid: ("STYLE VISUEL", ["Maths", "SVT"]))
    env.db.add(dbm.TeacherClass(Id=1, TeacherId=3), dbm.TeacherClassStudent(Id=1, TeacherClassId=1, StudentId=50))
    r = env.post(URL_QUIZ, json={"topic": "x", "student_id": 50}, headers=teacher(headers_for, 3))
    assert r.status_code == 200
    assert "STYLE VISUEL" in calls[0]["prompt"] and "inscrit : Maths, SVT." in calls[0]["prompt"]


def test_admin_can_target_any_student_without_a_class_link(env, headers_for, monkeypatch):
    calls = fake_json(monkeypatch, [GOOD_Q])
    monkeypatch.setattr(tx, "_load_student_generation_signals", lambda uid: ("", []))
    r = env.post(URL_QUIZ, json={"topic": "x", "student_id": 50}, headers=headers_for(1, "admin"))
    assert r.status_code == 200 and "Style d'apprentissage" not in calls[0]["prompt"]


# ── POST /ai/optimize-title ─────────────────────────────────────────────────

URL_TITLE = "/api/ai/optimize-title"


def test_title_uses_the_model_answer(env, headers_for, monkeypatch):
    calls = fake_json(monkeypatch, {"optimized_title": "Épreuves BAC C 2023", "rationale": "Plus clair."})
    r = env.post(URL_TITLE, json={"title": "bac", "type": "epreuve", "subject": "Maths"}, headers=teacher(headers_for))
    assert r.json() == {"optimized_title": "Épreuves BAC C 2023", "rationale": "Plus clair."}
    assert "Type : Épreuve" in calls[0]["prompt"] and 'Titre actuel : "bac"' in calls[0]["prompt"]


def test_title_rationale_has_a_default(env, headers_for, monkeypatch):
    fake_json(monkeypatch, {"optimized_title": "T"})
    assert "référencement" in env.post(URL_TITLE, json={"title": "t"}, headers=teacher(headers_for)).json()["rationale"]


@pytest.mark.parametrize("answer", [None, {}, {"optimized_title": ""}, [1]])
def test_title_fallback_prefixes_type_subject_and_level(env, headers_for, monkeypatch, answer):
    fake_json(monkeypatch, answer)
    body = env.post(URL_TITLE, json={"title": "Probabilités", "type": "correction", "subject": "Maths", "level": "Tle"},
                    headers=teacher(headers_for)).json()
    assert body["optimized_title"].startswith("Corrigé Maths Tle") and body["optimized_title"].endswith("Probabilités")


def test_title_fallback_for_an_unknown_type_says_contenu(env, headers_for, monkeypatch):
    fake_json(monkeypatch, None)
    assert env.post(URL_TITLE, json={"title": "X", "type": "inconnu"}, headers=teacher(headers_for)
                    ).json()["optimized_title"].startswith("Contenu")
    assert env.post(URL_TITLE, json={}, headers=teacher(headers_for)).status_code == 422


# ── POST /ai/generate-description ───────────────────────────────────────────

URL_DESC = "/api/ai/generate-description"


def test_description_prompt_and_result(env, headers_for, monkeypatch):
    calls = fake_text(monkeypatch, "Une belle description.")
    r = env.post(URL_DESC, json={"title": "BAC 2023", "type": "livre", "difficulty": "hard", "year": "2023"},
                 headers=teacher(headers_for))
    assert r.json() == {"description": "Une belle description."}
    prompt = calls[0]["prompt"]
    assert "Type : manuel" in prompt and "Difficulté : avancé" in prompt and "Année : 2023" in prompt
    assert calls[0]["max_tokens"] == 150


def test_description_is_cut_at_five_hundred_characters(env, headers_for, monkeypatch):
    fake_text(monkeypatch, "d" * 900)
    assert len(env.post(URL_DESC, json={"title": "t"}, headers=teacher(headers_for)).json()["description"]) == 500


def test_description_fallback_when_the_model_is_silent(env, headers_for, monkeypatch):
    fake_text(monkeypatch, "")
    text = env.post(URL_DESC, json={"title": "t", "type": "epreuve", "subject": "Maths", "level": "3e", "year": "2024"},
                    headers=teacher(headers_for)).json()["description"]
    assert text.startswith("Ce épreuve de Maths 3e") and "l'examen de 2024" in text


# ── POST /teacher/class-analysis ────────────────────────────────────────────

URL_CLASS = "/api/teacher/class-analysis"


def _ago(days):
    return Clock.current - timedelta(days=days)


def _enroll(env, *user_ids, subject=5):
    env.db.add(dbm.Subject(Id=subject, Title="Analyse"),
               *[dbm.Enrollment(Id=i + 1, UserId=u, SubjectId=subject) for i, u in enumerate(user_ids)])


def _score(env, id_, user, value, subject=5, days=5):
    env.db.add(dbm.DailyScore(Id=id_, UserId=user, Date=Clock.current.date(), AverageScore=value, SubjectId=subject,
                              CreatedAt=_ago(days)))


def test_class_analysis_without_students(env, headers_for, monkeypatch):
    calls = fake_json(monkeypatch, {})
    body = env.post(URL_CLASS, json={"teacher_id": 3, "content_id": 5}, headers=teacher(headers_for)).json()
    assert body["student_count"] == 0 and body["avg_score"] == 0.0 and "Aucun apprenant" in body["common_mistakes"][0]
    assert calls == []


def test_class_analysis_validates_its_body(env, headers_for):
    assert env.post(URL_CLASS, json={"teacher_id": 3}, headers=teacher(headers_for)).status_code == 422


def test_class_analysis_distribution_average_and_hardest_quizzes(env, headers_for, monkeypatch):
    _enroll(env, 1, 2, 3, 4)
    _score(env, 1, 1, 90), _score(env, 2, 2, 70), _score(env, 3, 3, 40), _score(env, 4, 3, 60)
    _score(env, 5, 4, 10, days=200)                                           # trop ancien : élève sans activité
    env.db.add(dbm.QuizAttempt(Id=1, UserId=1, QuizId=11, Score=20, CompletedAt=_ago(3)),
               dbm.QuizAttempt(Id=2, UserId=2, QuizId=11, Score=0, CompletedAt=_ago(3)),          # doublon de quiz
               dbm.QuizAttempt(Id=3, UserId=2, QuizId=12, Score=50, CompletedAt=_ago(3)),
               dbm.QuizAttempt(Id=4, UserId=3, QuizId=13, Score=90, CompletedAt=_ago(3)),
               dbm.QuizAttempt(Id=5, UserId=3, QuizId=14, Score=10, CompletedAt=_ago(3)),
               dbm.QuizAttempt(Id=6, UserId=3, QuizId=15, Score=5, CompletedAt=_ago(3)),
               dbm.QuizAttempt(Id=7, UserId=3, QuizId=16, Score=40, CompletedAt=_ago(300)))       # hors fenêtre
    calls = fake_json(monkeypatch, {"common_mistakes": ["a", "b", "c", "d"], "recommended_actions": ["x", "y", "z", "w"]})
    body = env.post(URL_CLASS, json={"teacher_id": 3, "content_id": 5}, headers=teacher(headers_for)).json()
    assert body["student_count"] == 4 and body["avg_score"] == 70.0
    assert body["mastery_distribution"] == {"excellent": 1, "good": 1, "struggling": 0, "at_risk": 1 + 1} or \
        body["mastery_distribution"] == {"excellent": 1, "good": 1, "struggling": 1, "at_risk": 1}
    hardest = body["hardest_questions"]
    assert [(q["question_id"], q["wrong_answer_rate"]) for q in hardest] == [(15, 0.95), (14, 0.9), (11, 0.8)]
    assert body["common_mistakes"] == ["a", "b", "c"] and body["recommended_actions"] == ["x", "y", "z"]
    assert "Classe de 4 élèves" in calls[0]["prompt"] and "contenu « Analyse »" in calls[0]["prompt"]
    assert calls[0]["max_tokens"] == 400


def test_class_analysis_falls_back_on_all_scores_of_the_students_when_none_matches_the_content(env, headers_for, monkeypatch):
    _enroll(env, 1)
    _score(env, 1, 1, 85, subject=None)
    fake_json(monkeypatch, None)
    body = env.post(URL_CLASS, json={"teacher_id": 3, "content_id": 5}, headers=teacher(headers_for)).json()
    assert body["avg_score"] == 85.0 and body["mastery_distribution"]["excellent"] == 1
    assert len(body["common_mistakes"]) == 3 and len(body["recommended_actions"]) == 3          # textes de repli


def test_class_analysis_ignores_deleted_enrollments(env, headers_for, monkeypatch):
    env.db.add(dbm.Subject(Id=5, Title="A"), dbm.Enrollment(Id=1, UserId=1, SubjectId=5, IsDeleted=True))
    fake_json(monkeypatch, None)
    assert env.post(URL_CLASS, json={"teacher_id": 3, "content_id": 5}, headers=teacher(headers_for)).json()["student_count"] == 0


def test_class_analysis_database_failure_is_a_500(env, headers_for, monkeypatch):
    class Boom:
        def query(self, *a):
            raise RuntimeError("base HS")

        def close(self):
            pass

    monkeypatch.setattr(tx, "Database", lambda: SimpleNamespace(SessionLocal=lambda: Boom()))
    r = env.post(URL_CLASS, json={"teacher_id": 3, "content_id": 5}, headers=teacher(headers_for))
    assert r.status_code == 500 and r.json()["detail"] == "Analyse collective indisponible"


@pytest.mark.xfail(strict=False, reason="class-analysis lit teacher_id dans le corps sans le comparer au jeton ni vérifier que "
                   "le professeur est propriétaire du contenu : tout professeur analyse la classe d'un autre. 403 attendu.")
def test_a_teacher_cannot_analyse_a_content_he_does_not_own(env, headers_for, monkeypatch):
    _enroll(env, 1)
    fake_json(monkeypatch, None)
    r = env.post(URL_CLASS, json={"teacher_id": 99, "content_id": 5}, headers=teacher(headers_for, 3))
    assert r.status_code == 403


# ── GET /teacher/content-impact/{id} ────────────────────────────────────────

def _impact(env, headers_for, content_id=5):
    return env.get(f"/api/teacher/content-impact/{content_id}", headers=teacher(headers_for))


def test_impact_of_an_unknown_content_is_404(env, headers_for):
    r = _impact(env, headers_for)
    assert r.status_code == 404 and r.json()["detail"] == "Contenu introuvable"
    assert env.get("/api/teacher/content-impact/abc", headers=teacher(headers_for)).status_code == 422


def test_impact_without_learners_is_zero_but_reports_the_rating(env, headers_for):
    env.db.add(dbm.Subject(Id=5, Title="A", AverageRating=3.5))
    body = _impact(env, headers_for).json()
    assert body["impact_score"] == 0 and body["avg_rating"] == 3.5 and "Aucun apprenant" in body["interpretation"]


def test_impact_weighs_completion_improvement_retention_and_rating(env, headers_for):
    enrolled = _ago(40)
    env.db.add(dbm.Subject(Id=5, Title="A", AverageRating=4.0),
               dbm.Enrollment(Id=1, UserId=1, SubjectId=5, EnrolledAt=enrolled, IsCompleted=True),
               dbm.Enrollment(Id=2, UserId=2, SubjectId=5, EnrolledAt=enrolled),
               dbm.DailyScore(Id=1, UserId=1, Date=Clock.current.date(), AverageScore=50, CreatedAt=enrolled - timedelta(days=10)),
               dbm.DailyScore(Id=2, UserId=1, Date=Clock.current.date(), AverageScore=70, CreatedAt=enrolled + timedelta(days=10)),
               dbm.QuizAttempt(Id=1, UserId=1, QuizId=1, Score=50), dbm.QuizAttempt(Id=2, UserId=1, QuizId=2, Score=60),
               dbm.QuizAttempt(Id=3, UserId=2, QuizId=1, Score=60))
    body = _impact(env, headers_for).json()
    assert body["completion_rate"] == 0.5 and body["avg_score_improvement"] == 20.0 and body["student_retention"] == 0.5
    assert body["impact_score"] == 15 + 30 + 10 + 16 == 71 and body["interpretation"].startswith("Excellent")


@pytest.mark.parametrize("completed,rating,band", [(True, 5.0, "Bon impact"), (False, 0.0, "Impact limité")])
def test_impact_interpretation_bands(env, headers_for, completed, rating, band):
    env.db.add(dbm.Subject(Id=5, Title="A", AverageRating=rating),
               dbm.Enrollment(Id=1, UserId=1, SubjectId=5, EnrolledAt=_ago(5), IsCompleted=completed))
    body = _impact(env, headers_for).json()
    assert body["interpretation"].startswith(band)
    assert body["impact_score"] == (50 if completed else 0)


def test_impact_score_stays_between_zero_and_one_hundred(env, headers_for):
    enrolled = _ago(40)
    env.db.add(dbm.Subject(Id=5, Title="A", AverageRating=5.0),
               dbm.Enrollment(Id=1, UserId=1, SubjectId=5, EnrolledAt=enrolled, IsCompleted=True),
               dbm.DailyScore(Id=1, UserId=1, Date=Clock.current.date(), AverageScore=0, CreatedAt=enrolled - timedelta(days=1)),
               dbm.DailyScore(Id=2, UserId=1, Date=Clock.current.date(), AverageScore=100, CreatedAt=enrolled + timedelta(days=1)),
               dbm.QuizAttempt(Id=1, UserId=1, QuizId=1, Score=1), dbm.QuizAttempt(Id=2, UserId=1, QuizId=2, Score=1))
    assert _impact(env, headers_for).json()["impact_score"] == 100


# ── POST /teacher/generate-correction ───────────────────────────────────────

URL_CORR = "/api/teacher/generate-correction"


def test_correction_keeps_valid_items_and_stringifies_fields(env, headers_for, monkeypatch):
    fake_json(monkeypatch, [{"question": "Q1", "correct_answer": 4, "explanation": "E", "scoring_guide": "2 pts"}, "texte"])
    body = env.post(URL_CORR, json={"exam_text": "Q1 ..."}, headers=teacher(headers_for)).json()
    assert len(body["correction_by_question"]) == 1 and body["correction_by_question"][0]["correct_answer"] == "4"


def test_correction_prompt_is_cut_at_four_thousand_characters_with_defaults(env, headers_for, monkeypatch):
    calls = fake_json(monkeypatch, [])
    env.post(URL_CORR, json={"exam_text": "a" * 4000 + "ZZZ"}, headers=teacher(headers_for))
    assert "a" * 4000 in calls[0]["prompt"] and "ZZZ" not in calls[0]["prompt"]
    assert "mathématiques niveau BAC" in calls[0]["prompt"] and calls[0]["max_tokens"] == 3000


@pytest.mark.parametrize("answer", [None, [], {"question": "x"}, "texte"])
def test_correction_falls_back_on_one_explanatory_item(env, headers_for, monkeypatch, answer):
    fake_json(monkeypatch, answer)
    items = env.post(URL_CORR, json={"exam_text": "x"}, headers=teacher(headers_for)).json()["correction_by_question"]
    assert len(items) == 1 and "n'a pas pu structurer" in items[0]["correct_answer"]


def test_correction_requires_the_exam_text(env, headers_for):
    assert env.post(URL_CORR, json={}, headers=teacher(headers_for)).status_code == 422


# ── POST /teacher/predict-popularity ────────────────────────────────────────

URL_POP = "/api/teacher/predict-popularity"


def _catalog(env):
    env.db.add(dbm.Subject(Id=1, Title="Petit", Category="Maths", Price=2000, EnrollmentCount=10, IsPublished=True),
               dbm.Subject(Id=2, Title="Grand", Category="Maths", Price=4000, EnrollmentCount=30, IsPublished=True),
               dbm.Subject(Id=3, Title="Gratuit", Category="Maths", Price=0, EnrollmentCount=20, IsPublished=True),
               dbm.Subject(Id=4, Title="Brouillon", Category="Maths", Price=9999, EnrollmentCount=99, IsPublished=False),
               dbm.Subject(Id=5, Title="Histoire 1", Category="Histoire", Price=1000, EnrollmentCount=5, IsPublished=True))


def _pop(env, headers_for, **body):
    return env.post(URL_POP, json=body, headers=teacher(headers_for)).json()


def test_popularity_averages_similar_published_contents(env, headers_for):
    _catalog(env)
    body = _pop(env, headers_for, subject="math")                    # filtre insensible à la casse sur la catégorie
    assert body["price_recommendation"] == 3000 and body["predicted_downloads_30d"] == 20
    assert body["similar_top_sellers"] == ["Grand", "Gratuit", "Petit"]


@pytest.mark.parametrize("kind,price,downloads", [("pack", 4200, 16), ("livre", 4200, 20), ("quiz", 1800, 20),
                                                  ("epreuve", 3000, 26), (None, 3000, 20)])
def test_popularity_adjusts_price_and_downloads_by_content_type(env, headers_for, kind, price, downloads):
    _catalog(env)
    body = _pop(env, headers_for, subject="math", type=kind)
    assert (body["price_recommendation"], body["predicted_downloads_30d"]) == (price, downloads)


def test_popularity_without_data_uses_defaults_and_a_floor_on_quiz_price(env, headers_for):
    body = _pop(env, headers_for)
    assert body["price_recommendation"] == 2500 and body["predicted_downloads_30d"] == 15 and body["similar_top_sellers"] == []
    assert _pop(env, headers_for, type="quiz")["price_recommendation"] == 1500
    env.db.add(dbm.Subject(Id=1, Title="Pas cher", Price=100, EnrollmentCount=1, IsPublished=True))
    assert _pop(env, headers_for, type="quiz")["price_recommendation"] == 500
    assert _pop(env, headers_for)["predicted_downloads_30d"] == 5


def test_popularity_timing_before_the_exam_season(env, headers_for):
    msg = _pop(env, headers_for, level="BAC")["timing_advice"]            # juin : hors saison
    assert "février et mars" in msg and "Publiez en janvier" in msg


def test_popularity_timing_inside_the_exam_season_boosts_downloads(env, headers_for):
    _catalog(env)
    Clock.current = datetime(2026, 3, 10, 12, 0, 0)
    body = _pop(env, headers_for, subject="math", level="BAC C")
    assert body["timing_advice"].startswith("Excellente période") and "mars" in body["timing_advice"]
    assert body["predicted_downloads_30d"] == int(20 * 1.6)


def test_popularity_timing_for_a_level_without_season(env, headers_for):
    assert "toute l'année" in _pop(env, headers_for, level="6e")["timing_advice"]


def test_popularity_failure_returns_a_safe_default_not_an_error(env, headers_for, monkeypatch):
    class Boom:
        def query(self, *a):
            raise RuntimeError("base HS")

        def close(self):
            pass

    monkeypatch.setattr(tx, "Database", lambda: SimpleNamespace(SessionLocal=lambda: Boom()))
    assert _pop(env, headers_for) == {"predicted_downloads_30d": 20, "price_recommendation": 2500,
                                      "similar_top_sellers": [], "timing_advice":
                                      "Publiez maintenant pour commencer à construire votre audience."}


# ── _coherent_criteria ──────────────────────────────────────────────────────

def test_criteria_scores_are_bounded_by_their_own_maximum_and_zero():
    out = tx._coherent_criteria([{"label": "A", "score": 15, "max_score": 10, "confidence": 1.7, "note": "n"},
                                 {"label": "B", "score": -3, "max_score": 5, "confidence": -1}], 20)
    assert [(c.score, c.max_score, c.confidence) for c in out] == [(10.0, 10.0, 1.0), (0.0, 5.0, 0.0)]


def test_criteria_drop_unusable_entries_and_default_missing_fields():
    out = tx._coherent_criteria(["texte", {"label": "A", "score": "x", "max_score": 5}, {"score": 3, "max_score": 4},
                                 {"label": "L" * 300, "max_score": 2}], 20)
    assert [c.label[:7] for c in out] == ["Critère", "LLLLLLL"] and len(out[1].label) == 200
    assert out[0].confidence == 0.5 and tx._coherent_criteria(None, 20) == [] and tx._coherent_criteria({}, 20) == []


# ── POST /teacher/analyze-submission ────────────────────────────────────────

URL_SUB = "/api/teacher/analyze-submission"


def _submit(env, headers_for, **body):
    return env.post(URL_SUB, json=body, headers=teacher(headers_for))


@pytest.mark.parametrize("correct,max_score,score,kind", [(True, 4, 4, "none"), (False, 4, 0, "conceptual"), (None, 2, 0, "conceptual")])
def test_mcq_is_graded_mechanically_without_calling_the_model(env, headers_for, monkeypatch, correct, max_score, score, kind):
    calls = fake_json(monkeypatch, {})
    body = _submit(env, headers_for, question_type="mcq", is_correct=correct, max_score=max_score).json()
    assert body["score_suggestion"] == score and body["error_type"] == kind and body["overall_score"] == score
    assert body["criteria"] == [{"label": "Réponse", "score": score, "max_score": max_score, "confidence": 1.0, "note": ""}]
    assert calls == []


def test_an_empty_copy_is_never_graded(env, headers_for, monkeypatch):
    calls = fake_json(monkeypatch, {})
    for payload in ({}, {"submission_text": "   "}):
        body = _submit(env, headers_for, **payload).json()
        assert body["score_suggestion"] == 0 and body["criteria"] == [] and "Copie vide" in body["extraction_warning"]
    assert calls == []


def test_a_scanned_copy_is_downloaded_and_its_text_extracted(env, headers_for, monkeypatch):
    calls = fake_json(monkeypatch, {"error_type": "none", "score_suggestion": 15})
    seen = []
    monkeypatch.setattr(tx, "_download_for_analysis", lambda url: seen.append(url) or (b"%PDF", "application/pdf"))
    monkeypatch.setattr(tx, "_extract_text_for_analysis", lambda raw, name: seen.append(name) or ("Texte lu de la copie", None, "Lecture OCR partielle"))
    body = _submit(env, headers_for, file_url="https://files.test/copies/eleve1.pdf").json()
    assert seen == ["https://files.test/copies/eleve1.pdf", "eleve1.pdf"] and body["extraction_warning"] == "Lecture OCR partielle"
    assert "Texte lu de la copie" in calls[0]["prompt"]


def test_download_failure_is_reported_as_a_warning_and_the_copy_stays_ungraded(env, headers_for, monkeypatch):
    fake_json(monkeypatch, {})

    def refuse(url):
        raise HTTPException(status_code=413, detail="Fichier trop volumineux")

    monkeypatch.setattr(tx, "_download_for_analysis", refuse)
    body = _submit(env, headers_for, file_url="https://files.test/x.pdf").json()
    assert body["score_suggestion"] == 0 and body["extraction_warning"] == "Fichier trop volumineux"

    def crash(url):
        raise RuntimeError("réseau")

    monkeypatch.setattr(tx, "_download_for_analysis", crash)
    assert "Copie illisible" in _submit(env, headers_for, file_url="https://files.test/x.pdf").json()["extraction_warning"]


def test_a_given_text_wins_over_the_file(env, headers_for, monkeypatch):
    fake_json(monkeypatch, {"error_type": "none"})
    monkeypatch.setattr(tx, "_download_for_analysis", lambda url: pytest.fail("pas de téléchargement attendu"))
    assert _submit(env, headers_for, submission_text="Ma réponse", file_url="https://x.test/a.pdf").status_code == 200


ESSAY = {"error_type": "calculation", "error_details": "Signe", "suggested_comment": "Bien", "score_suggestion": 99,
         "alternative_comments": ["a", "b", "c", "d"], "highlights_present": ["méthode"], "highlights_absent": ["conclusion"],
         "score_range": "13-15",
         "criteria": [{"label": "Démarche", "score": 8, "max_score": 10, "confidence": 0.9, "note": "ok"},
                      {"label": "Calcul", "score": 4, "max_score": 10, "confidence": 0.3, "note": "faux"}]}


def test_essay_analysis_sums_the_criteria_instead_of_trusting_the_model_total(env, headers_for, monkeypatch):
    calls = fake_json(monkeypatch, dict(ESSAY))
    body = _submit(env, headers_for, submission_text="Réponse", subject="Maths", max_score=20).json()
    assert body["overall_score"] == 12.0 and body["score_suggestion"] == 12          # et non 99
    assert body["low_confidence_criteria"] == ["Calcul"] and body["error_type"] == "calculation"
    assert body["alternative_comments"] == ["a", "b", "c"]
    assert body["highlights_present"] == ["méthode"] and body["highlights_absent"] == ["conclusion"]
    assert body["score_range"] is None and body["extraction_warning"] is None
    assert "développement long" in calls[0]["prompt"] and "Barème sur 20" in calls[0]["prompt"] and calls[0]["max_tokens"] == 4000


def test_short_answer_analysis_keeps_the_range_and_drops_the_highlights(env, headers_for, monkeypatch):
    fake_json(monkeypatch, dict(ESSAY))
    body = _submit(env, headers_for, submission_text="Réponse", question_type="short").json()
    assert body["score_range"] == "13-15" and body["highlights_present"] == [] and body["highlights_absent"] == []


def test_unknown_error_type_becomes_methodological(env, headers_for, monkeypatch):
    fake_json(monkeypatch, {**ESSAY, "error_type": "n'importe quoi"})
    assert _submit(env, headers_for, submission_text="R").json()["error_type"] == "methodological"


def test_missing_criteria_fall_back_on_one_low_confidence_criterion(env, headers_for, monkeypatch):
    fake_json(monkeypatch, {"error_type": "none", "score_suggestion": 17})
    body = _submit(env, headers_for, submission_text="R", max_score=20).json()
    assert body["criteria"][0]["label"] == "Ensemble de la copie" and body["criteria"][0]["confidence"] == 0.4
    assert body["overall_score"] == 17 and body["low_confidence_criteria"] == ["Ensemble de la copie"]


def test_missing_criteria_and_score_use_half_the_scale(env, headers_for, monkeypatch):
    fake_json(monkeypatch, {"error_type": "none"})
    assert _submit(env, headers_for, submission_text="R", max_score=10).json()["overall_score"] == 5


def test_rubric_and_reference_are_put_in_the_prompt(env, headers_for, monkeypatch):
    calls = fake_json(monkeypatch, dict(ESSAY))
    _submit(env, headers_for, submission_text="R" * 3000, reference_answer="Corrigé " + "c" * 3000, expected_answer="Attendu",
            rubric_criteria=[{"label": "Démarche", "max_points": 10}, {"label": "Calcul", "max_points": 10}])
    prompt = calls[0]["prompt"]
    assert "EXACTEMENT ces 2 critères" in prompt and "- Démarche (sur 10.0 points)" in prompt
    assert "CORRIGÉ DE RÉFÉRENCE DU PROFESSEUR" in prompt and "RÉPONSE ATTENDUE :\nAttendu" in prompt
    assert "R" * 2000 in prompt and "R" * 2001 not in prompt
    assert "c" * 2000 in prompt and "c" * 2001 not in prompt
    assert "compare précisément" in prompt


def test_without_rubric_or_reference_the_prompt_asks_for_lower_confidence(env, headers_for, monkeypatch):
    calls = fake_json(monkeypatch, dict(ESSAY))
    _submit(env, headers_for, submission_text="R", max_score=10)
    assert "abaisse la confiance" in calls[0]["prompt"] and "égaler 10" in calls[0]["prompt"]


def test_unavailable_model_gives_a_manual_grading_placeholder(env, headers_for, monkeypatch):
    fake_json(monkeypatch, None)
    body = _submit(env, headers_for, submission_text="R", max_score=20).json()
    assert body["score_suggestion"] == 10 and body["criteria"][0]["confidence"] == 0.2
    assert "temporairement indisponible" in body["extraction_warning"] and body["low_confidence_criteria"]


def test_submission_body_validation(env, headers_for):
    assert _submit(env, headers_for, max_score="beaucoup").status_code == 422
    assert _submit(env, headers_for, rubric_criteria=[{"label": "x"}]).status_code == 422
