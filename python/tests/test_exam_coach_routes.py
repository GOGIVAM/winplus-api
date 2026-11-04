"""Coach d'examen (routes/exam_coach_routes.py) : calendrier de révision, génération, recalibrage, séance du
jour, prédiction de note, micro-intervention. SQLite en mémoire, fournisseur d'IA et analyseur simulés, date figée."""

import json
from datetime import date, datetime, timedelta

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

import database as dbm
import routes.exam_coach_routes as ec

NOW = datetime(2026, 6, 17, 9, 0, 0)            # mercredi
TODAY = NOW.date()


class FrozenDate(date):
    @classmethod
    def today(cls):
        return TODAY


class FrozenDatetime(datetime):
    @classmethod
    def utcnow(cls):
        return NOW

    @classmethod
    def now(cls, tz=None):
        return NOW


def iso(days):
    return (TODAY + timedelta(days=days)).isoformat()


class FakeClient:
    def __init__(self, answer="", error=None):
        self.answer, self.error, self.calls = answer, error, []

    def chat(self, messages, system_prompt=None, max_tokens=None, temperature=None, **kw):
        self.calls.append({"prompt": messages[0]["content"], "max_tokens": max_tokens, "temperature": temperature})
        if self.error:
            raise self.error
        return {"content": self.answer}


@pytest.fixture(autouse=True)
def frozen(monkeypatch):
    monkeypatch.setattr(ec, "date", FrozenDate)
    monkeypatch.setattr(ec, "datetime", FrozenDatetime)


@pytest.fixture
def env(monkeypatch, sqlite_database):
    db = sqlite_database(ec, [dbm.ExamCoachPlanAI, dbm.QuizAttempt, dbm.DailyScore, dbm.LearningHistory,
                              dbm.Enrollment, dbm.Subject])
    app = FastAPI()
    app.include_router(ec.exam_coach_router, prefix="/api/exam-coach")
    client = TestClient(app, raise_server_exceptions=False)
    client.db = db

    def use(answer="", error=None):
        fake = FakeClient(answer, error)
        monkeypatch.setattr(ec, "get_deepseek_client", lambda: fake)
        return fake

    def analyzer(progress=None, error=None):
        class Fake:
            def __init__(self, **kw):
                pass

            def analyze_user_progress(self, user_id):
                if error:
                    raise error
                return progress

        monkeypatch.setattr(ec, "NLPAnalyzer", lambda: None)
        monkeypatch.setattr(ec, "Recommender", lambda db: None)
        monkeypatch.setattr(ec, "UserPerformanceAnalyzer", Fake)

    client.use, client.analyzer = use, analyzer
    return client


# ── _generate_plan ──────────────────────────────────────────────────────────

def _days(plan):
    return [d for w in plan["weeks"] for d in w["days"]]


def test_plan_starts_with_the_weak_areas_and_marks_sunday_as_rest():
    plan = ec._generate_plan(7, "BAC", iso(10), 1.5, ["Maths", "Physique"], ["Anglais"])
    days = _days(plan)
    assert [(d["focus_area"], d["activity_type"]) for d in days[:4]] == [
        ("Maths", "revision"), ("Physique", "revision"), ("Anglais", "revision"), ("Maths", "quiz")]
    assert [d["priority"] for d in days[:3]] == ["high", "high", "low"]
    assert days[4]["activity_type"] == "rest" and days[4]["duration_minutes"] == 0       # 21 juin = dimanche
    assert days[4]["date"] == "2026-06-21" and days[4]["subject_ids"] == []
    assert days[0]["duration_minutes"] == 90 and days[0]["date"] == TODAY.isoformat()


def test_plan_groups_days_by_weeks_of_seven_and_reports_the_exam_info():
    plan = ec._generate_plan(7, "BEPC", iso(10), 1.5, ["Maths"], [])
    assert [(w["week"], len(w["days"])) for w in plan["weeks"]] == [(1, 7), (2, 3)]
    assert plan["total_days"] == 10
    assert plan["exam_info"] == {"exam_type": "BEPC", "exam_date": iso(10), "days_remaining": 10,
                                 "hours_per_day": 1.5, "focus_subjects": ["Maths"]}


def test_every_session_in_the_last_week_is_a_quiz():
    plan = ec._generate_plan(7, "BAC", iso(20), 1, ["Maths", "SVT"], [])
    work = [d for d in _days(plan) if d["activity_type"] != "rest"]
    last_week = [d for d in work if plan["total_days"] - d["day"] < 7]
    assert last_week and all(d["activity_type"] == "quiz" for d in last_week)


def test_revision_cycle_goes_revision_quiz_adaptive_quiz():
    plan = ec._generate_plan(7, "BAC", iso(60), 1, [], ["Seule"])
    seen = [d["activity_type"] for d in _days(plan) if d["focus_area"] == "Seule"][:5]
    assert seen == ["revision", "quiz", "adaptive_quiz", "quiz", "adaptive_quiz"]


def test_duration_is_capped_at_two_hours():
    plan = ec._generate_plan(7, "BAC", iso(5), 5, [], ["Maths"])
    assert {d["duration_minutes"] for d in _days(plan) if d["activity_type"] != "rest"} == {120}


def test_default_subjects_when_there_is_nothing_else():
    plan = ec._generate_plan(7, "BAC", iso(5), 1, [], [])
    assert plan["exam_info"]["focus_subjects"] == ["Mathématiques", "Français", "Physique-Chimie", "Sciences de la Vie"]


def test_subjects_are_deduplicated_and_limited_to_eight_with_priorities():
    weak = ["A", "B", "C", "D"]
    plan = ec._generate_plan(7, "BAC", iso(30), 1, weak, ["A", "E", "F", "G", "H", "I", "J"])
    assert plan["exam_info"]["focus_subjects"] == ["A", "B", "C", "D", "E"]       # cinq premiers de huit
    by_focus = {d["focus_area"]: d["priority"] for d in _days(plan) if d["activity_type"] != "rest"}
    assert by_focus["A"] == "high" and by_focus["D"] == "medium" and by_focus["E"] == "low"
    assert set(by_focus) <= {"A", "B", "C", "D", "E", "F", "G", "H"}               # I et J coupés


@pytest.mark.parametrize("weak,days,hours,expected", [
    ([], 10, 1, 0.5), (["x"], 10, 1, 0.65), ([], 40, 1, 0.7), ([], 10, 2, 0.6), (["x"], 40, 2, 0.95), ([], 40, 2, 0.8)])
def test_confidence_score(weak, days, hours, expected):
    assert ec._generate_plan(7, "BAC", iso(days), hours, weak, ["M"])["confidence_score"] == expected


# ── POST /generate ──────────────────────────────────────────────────────────

URL_GEN = "/api/exam-coach/generate"
GEN = {"user_id": 7, "exam_type": "BAC", "exam_date": None, "hours_per_day": 2}


def _gen(env, headers_for, **kw):
    return env.post(URL_GEN, json={**GEN, "exam_date": iso(40), **kw}, headers=headers_for(7))


def test_generate_requires_a_token_and_valid_fields(env, headers_for):
    assert env.post(URL_GEN, json={**GEN, "exam_date": iso(40)}).status_code in (401, 403)
    assert env.post(URL_GEN, json={"user_id": 7}, headers=headers_for(7)).status_code == 422
    assert _gen(env, headers_for, hours_per_day="beaucoup").status_code == 422


@pytest.mark.parametrize("bad", ["15/06/2027", "demain", "", "2026-13-40"])
def test_generate_rejects_a_badly_formatted_date(env, headers_for, bad):
    r = _gen(env, headers_for, exam_date=bad)
    assert r.status_code == 400 and "YYYY-MM-DD" in r.json()["detail"]


@pytest.mark.parametrize("days", [0, -1, -30])
def test_generate_rejects_an_exam_today_or_in_the_past(env, headers_for, days):
    r = _gen(env, headers_for, exam_date=iso(days))
    assert r.status_code == 400 and "futur" in r.json()["detail"]
    assert env.db.all(dbm.ExamCoachPlanAI) == []


def test_generate_uses_weak_areas_and_enrollments_then_stores_the_plan(env, headers_for):
    env.analyzer({"success": True, "analysis": {"weak_areas": ["Chimie"]},
                  "overview": {"enrollments": [{"subject_title": "Anglais"}, {"subject_title": ""}, {}]}})
    r = _gen(env, headers_for)
    data = r.json()["data"]
    assert r.status_code == 200 and r.json()["success"] is True and data["plan_id"] == 1
    assert data["exam_info"]["focus_subjects"] == ["Chimie", "Anglais"]
    row = env.db.all(dbm.ExamCoachPlanAI)[0]
    assert (row.UserId, row.ExamType, float(row.HoursPerDay), row.IsActive) == (7, "BAC", 2.0, True)
    assert row.ExamDate == datetime.fromisoformat(iso(40)) and json.loads(row.PlanJson)["total_days"] == 40


@pytest.mark.parametrize("progress,error", [
    ({"success": False}, None), (None, RuntimeError("analyseur HS")), ({"success": True}, None)])
def test_generate_still_works_when_the_performance_analysis_is_unavailable(env, headers_for, progress, error):
    env.analyzer(progress if progress is not None else {}, error)
    r = _gen(env, headers_for)
    assert r.status_code == 200 and len(r.json()["data"]["exam_info"]["focus_subjects"]) == 4


def test_generate_database_failure_is_a_500_with_a_fixed_message(env, headers_for, monkeypatch):
    env.analyzer({"success": False})

    class Boom:
        engine = None

        def SessionLocal(self):
            raise RuntimeError("secret interne")

    monkeypatch.setattr(ec, "Database", lambda: Boom())
    r = _gen(env, headers_for)
    assert r.status_code == 500 and r.json()["detail"] == "Erreur lors de la sauvegarde du plan."


# ── POST /recalibrate ───────────────────────────────────────────────────────

URL_RECAL = "/api/exam-coach/recalibrate"


def _stored_plan(env, user_id=7, active=True, exam_in=20):
    plan = ec._generate_plan(user_id, "BAC", iso(exam_in), 1.5, ["Maths", "Physique"], ["Anglais"])
    env.db.add(dbm.ExamCoachPlanAI(Id=1, UserId=user_id, ExamType="BAC", ExamDate=datetime.fromisoformat(iso(exam_in)),
                                   HoursPerDay=1.5, PlanJson=json.dumps(plan), ConfidenceScore=0.7,
                                   CreatedAt=NOW, IsActive=active))
    return plan


def _results(*triples):
    return [{"day": d, "activity_completed": done, "quiz_score_if_applicable": score} for d, done, score in triples]


def test_recalibrate_validates_the_body(env, headers_for):
    assert env.post(URL_RECAL, json={"user_id": 7}, headers=headers_for(7)).status_code == 422
    bad = {"user_id": 7, "plan_id": 1, "week_results": [{"day": "x", "activity_completed": True}]}
    assert env.post(URL_RECAL, json=bad, headers=headers_for(7)).status_code == 422
    assert env.post(URL_RECAL, json={"user_id": 7, "plan_id": 1, "week_results": []}).status_code in (401, 403)


def test_recalibrate_unknown_or_inactive_plan_is_404(env, headers_for):
    body = {"user_id": 7, "plan_id": 1, "week_results": []}
    assert env.post(URL_RECAL, json=body, headers=headers_for(7)).status_code == 404
    _stored_plan(env, active=False)
    assert env.post(URL_RECAL, json=body, headers=headers_for(7)).status_code == 404


def test_recalibrate_refuses_someone_elses_plan(env, headers_for):
    _stored_plan(env, user_id=8)
    r = env.post(URL_RECAL, json={"user_id": 7, "plan_id": 1, "week_results": []}, headers=headers_for(7))
    assert r.status_code == 403 and "appartient pas" in r.json()["detail"]


def test_recalibrate_boosts_missed_and_failed_subjects_and_keeps_completed_days(env, headers_for):
    old = _stored_plan(env)
    old_days = _days(old)
    results = _results((1, True, 90), (2, False, None), (3, True, 40))         # jour 2 manqué, jour 3 quiz raté
    r = env.post(URL_RECAL, json={"user_id": 7, "plan_id": 1, "week_results": results}, headers=headers_for(7))
    plan = r.json()["data"]
    assert r.status_code == 200 and r.json()["success"] is True
    kept = [d for w in plan["weeks"] for d in w["days"] if d["day"] <= 3]
    assert [d["day"] for d in kept] == [1, 2, 3] and kept == old_days[:3]
    boosted = [old_days[1]["focus_area"], old_days[2]["focus_area"]]
    assert plan["exam_info"]["focus_subjects"][:2] == boosted
    assert plan["weeks"][0]["week"] == 1 and plan["weeks"][1]["week"] == 2
    future = [d["day"] for d in plan["weeks"][1]["days"]]
    assert future[0] == 4                                                       # décalé du nombre de jours faits
    stored = json.loads(env.db.all(dbm.ExamCoachPlanAI)[0].PlanJson)
    assert stored["exam_info"]["focus_subjects"] == plan["exam_info"]["focus_subjects"]


def test_recalibrate_does_not_boost_a_completed_day_with_a_good_score(env, headers_for):
    old = _stored_plan(env)
    r = env.post(URL_RECAL, json={"user_id": 7, "plan_id": 1, "week_results": _results((1, True, 60))},
                 headers=headers_for(7))
    assert r.json()["data"]["exam_info"]["focus_subjects"] == old["exam_info"]["focus_subjects"]


def test_recalibrate_with_no_result_keeps_nothing_and_regenerates_the_whole_period(env, headers_for):
    _stored_plan(env)
    r = env.post(URL_RECAL, json={"user_id": 7, "plan_id": 1, "week_results": []}, headers=headers_for(7))
    assert r.status_code == 200 and r.json()["data"]["weeks"][0]["days"][0]["day"] == 1


def test_recalibrate_ignores_results_for_unknown_days(env, headers_for):
    old = _stored_plan(env)
    r = env.post(URL_RECAL, json={"user_id": 7, "plan_id": 1, "week_results": _results((99, False, None))},
                 headers=headers_for(7))
    assert r.json()["data"]["exam_info"]["focus_subjects"] == old["exam_info"]["focus_subjects"]


def test_recalibrate_corrupt_plan_is_a_500(env, headers_for):
    env.db.add(dbm.ExamCoachPlanAI(Id=1, UserId=7, ExamType="BAC", ExamDate=NOW, HoursPerDay=1, PlanJson="{pas json",
                                   CreatedAt=NOW))
    r = env.post(URL_RECAL, json={"user_id": 7, "plan_id": 1, "week_results": []}, headers=headers_for(7))
    assert r.status_code == 500


# ── GET /today/{user_id} ────────────────────────────────────────────────────

def _today_plan(env, created, user_id=7, active=True):
    plan = ec._generate_plan(user_id, "BAC", iso(30), 1, ["Maths"], [])
    env.db.add(dbm.ExamCoachPlanAI(Id=1, UserId=user_id, ExamType="BAC", ExamDate=datetime.fromisoformat(iso(30)),
                                   HoursPerDay=1, PlanJson=json.dumps(plan), CreatedAt=created, IsActive=active))
    return plan


def test_today_requires_a_token(env):
    assert env.get("/api/exam-coach/today/7").status_code in (401, 403)


def test_today_without_active_plan_is_404(env, headers_for):
    _today_plan(env, NOW, active=False)
    r = env.get("/api/exam-coach/today/7", headers=headers_for(7))
    assert r.status_code == 404 and "Aucun plan actif" in r.json()["detail"]


def test_today_returns_the_session_of_the_day_and_the_model_message(env, headers_for):
    plan = _today_plan(env, NOW - timedelta(days=2))                            # jour 3 du plan
    fake = env.use("  Courage !  ")
    data = env.get("/api/exam-coach/today/7", headers=headers_for(7)).json()["data"]
    assert data["day_number"] == 3 and data["activity"] == _days(plan)[2]
    assert data["winai_message"] == "Courage !" and data["days_remaining"] == 30
    assert data["focus_area"] == data["subject"] == _days(plan)[2]["focus_area"] and data["is_completed"] is False
    assert "BAC dans 30 jours" in fake.calls[0]["prompt"] and fake.calls[0]["max_tokens"] == 120


def test_today_falls_back_on_a_fixed_message_when_the_provider_fails(env, headers_for):
    _today_plan(env, NOW)
    env.use(error=TimeoutError("délai dépassé"))
    r = env.get("/api/exam-coach/today/7", headers=headers_for(7))
    assert r.status_code == 200 and "Bon courage pour" in r.json()["data"]["winai_message"]


@pytest.mark.parametrize("created", [NOW - timedelta(days=100), NOW + timedelta(days=5)])
def test_today_outside_the_plan_is_404(env, headers_for, created):
    _today_plan(env, created)
    r = env.get("/api/exam-coach/today/7", headers=headers_for(7))
    assert r.status_code == 404 and "Pas de session" in r.json()["detail"]


def test_today_picks_the_most_recent_active_plan(env, headers_for):
    _today_plan(env, NOW - timedelta(days=3))
    other = ec._generate_plan(7, "BEPC", iso(30), 1, ["Latin"], [])
    env.db.add(dbm.ExamCoachPlanAI(Id=2, UserId=7, ExamType="BEPC", ExamDate=NOW, HoursPerDay=1,
                                   PlanJson=json.dumps(other), CreatedAt=NOW))
    env.use("ok")
    assert env.get("/api/exam-coach/today/7", headers=headers_for(7)).json()["data"]["focus_area"] == "Latin"


@pytest.mark.xfail(strict=False, reason="GET /today/{user_id} (comme /micro-intervention et /predict-grade) ne compare "
                   "pas user_id au jeton : un élève lit le plan et le message d'un autre. Un 403 est attendu.")
def test_a_student_cannot_read_another_students_plan(env, headers_for):
    _today_plan(env, NOW)
    env.use("ok")
    assert env.get("/api/exam-coach/today/7", headers=headers_for(8)).status_code == 403


# ── POST /predict-grade ─────────────────────────────────────────────────────

URL_PRED = "/api/exam-coach/predict-grade"


def _attempts(env, user_id, scores, start=0, step=1):
    n = len(env.db.all(dbm.QuizAttempt))
    env.db.add(*[dbm.QuizAttempt(Id=n + i + 1, UserId=user_id, Score=s, CreatedAt=NOW - timedelta(days=start + i * step))
                 for i, s in enumerate(scores)])


def _predict(env, headers_for, **kw):
    return env.post(URL_PRED, json={"user_id": 7, "subject": "Maths", **kw}, headers=headers_for(7))


def test_predict_validates_the_body(env, headers_for):
    assert env.post(URL_PRED, json={"user_id": 7}, headers=headers_for(7)).status_code == 422
    assert env.post(URL_PRED, json={"user_id": 7, "subject": "x"}).status_code in (401, 403)


def test_predict_without_attempts_gives_a_neutral_estimate_and_no_model_call(env, headers_for):
    fake = env.use("[]")
    data = _predict(env, headers_for).json()["data"]
    assert data == {"predicted_grade": "10/20", "confidence": 0.35, "grade_range": "8-13/20", "improvement_points": 4,
                    "key_topics_to_master": [], "data_points": 0}
    assert fake.calls == []


def test_predict_ignores_old_attempts_other_students_and_missing_scores(env, headers_for):
    env.use("[]")
    _attempts(env, 7, [90], start=100)                 # trop ancien (> 90 jours)
    _attempts(env, 8, [90])
    _attempts(env, 7, [None])
    assert _predict(env, headers_for).json()["data"]["data_points"] == 0


def test_predict_computes_the_grade_range_and_confidence(env, headers_for):
    env.use('Voici ["Dérivées", "Limites", "Suites", "Intégrales", "Probabilités", "Trop"] fin')
    _attempts(env, 7, [80, 60, 40])
    env.db.add(dbm.DailyScore(Id=1, UserId=7, Date=date(2026, 6, 16), AverageScore=70),
               dbm.DailyScore(Id=2, UserId=7, Date=date(2026, 6, 1), AverageScore=50))
    data = _predict(env, headers_for).json()["data"]
    assert data["predicted_grade"] == "12/20" and data["grade_range"] == "10-14/20"
    assert data["confidence"] == pytest.approx(0.475, abs=0.006) and data["data_points"] == 3
    assert data["avg_score_pct"] == 60.0 and data["trend"] == 0.0 and data["improvement_points"] == 4
    assert data["key_topics_to_master"] == ["Dérivées", "Limites", "Suites", "Intégrales", "Probabilités"]


def test_predict_adds_a_share_of_the_recent_trend(env, headers_for):
    env.use("[]")
    _attempts(env, 7, [90] * 5 + [10] * 3)                  # moyenne 60 %, tendance +80 points
    data = _predict(env, headers_for).json()["data"]
    assert data["predicted_grade"] == "17/20" and data["grade_range"] == "15-19/20" and data["trend"] == 16.0


def test_predict_is_bounded_between_zero_and_twenty(env, headers_for):
    env.use("[]")
    _attempts(env, 7, [100] * 5 + [0] * 3)
    data = _predict(env, headers_for).json()["data"]
    assert data["predicted_grade"] == "20/20" and data["grade_range"].endswith("-20/20")


def test_predict_confidence_is_capped(env, headers_for):
    env.use("[]")
    _attempts(env, 7, [50] * 50)
    assert _predict(env, headers_for).json()["data"]["confidence"] == 0.95


def test_predict_improvement_points_are_clamped_to_zero_and_ten(env, headers_for):
    env.use("[]")
    _attempts(env, 7, [50])
    env.db.add(dbm.DailyScore(Id=1, UserId=7, Date=date(2026, 6, 16), AverageScore=20),
               dbm.DailyScore(Id=2, UserId=7, Date=date(2026, 6, 1), AverageScore=90))
    assert _predict(env, headers_for).json()["data"]["improvement_points"] == 0


def test_predict_prompt_names_the_exam_and_the_subject(env, headers_for):
    fake = env.use("[]")
    _attempts(env, 7, [50])
    _predict(env, headers_for)
    _predict(env, headers_for, exam_type="BEPC")
    assert "(BAC)" in fake.calls[0]["prompt"] and "(BEPC)" in fake.calls[1]["prompt"] and "Maths" in fake.calls[0]["prompt"]


@pytest.mark.parametrize("answer", ["aucune liste", "[pas, json]"])
def test_predict_survives_a_useless_model_answer(env, headers_for, answer):
    env.use(answer)
    _attempts(env, 7, [50])
    r = _predict(env, headers_for)
    assert r.status_code == 200 and r.json()["data"]["key_topics_to_master"] == []


def test_predict_survives_a_provider_timeout(env, headers_for):
    env.use(error=TimeoutError("lent"))
    _attempts(env, 7, [50])
    assert _predict(env, headers_for).json()["data"]["key_topics_to_master"] == []


# ── GET /micro-intervention/{user_id} ───────────────────────────────────────

def _situation(env, inactive_days=5, exam_in=20, subject=True, scores=None):
    if inactive_days is not None:
        env.db.add(dbm.LearningHistory(Id=1, UserId=7, SubjectId=1, Timestamp=NOW - timedelta(days=inactive_days)))
    if exam_in is not None:
        env.db.add(dbm.ExamCoachPlanAI(Id=1, UserId=7, ExamType="BEPC", ExamDate=NOW + timedelta(days=exam_in),
                                       HoursPerDay=1, PlanJson="{}", CreatedAt=NOW))
    if subject:
        env.db.add(dbm.Subject(Id=1, Title="Chimie"), dbm.Enrollment(Id=1, UserId=7, SubjectId=1, EnrolledAt=NOW))
    for i, s in enumerate(scores or []):
        env.db.add(dbm.DailyScore(Id=i + 1, UserId=7, Date=TODAY - timedelta(days=i + 1), AverageScore=s))


def _micro(env, headers_for, user=7):
    return env.get(f"/api/exam-coach/micro-intervention/{user}", headers=headers_for(7)).json()


def test_micro_intervention_requires_a_token(env):
    assert env.get("/api/exam-coach/micro-intervention/7").status_code in (401, 403)


def test_micro_intervention_triggers_for_an_inactive_student_close_to_the_exam(env, headers_for):
    fake = env.use("Courage. Commence par 25 minutes de Chimie.")
    _situation(env)
    body = _micro(env, headers_for)
    assert body["trigger"] is True and body["success"] is True
    assert body["data"] == {"message": "Courage. Commence par 25 minutes de Chimie.", "suggested_action": "Reprendre Chimie",
                            "subject": "Chimie", "duration_minutes": 25, "urgency": "medium", "days_inactive": 5,
                            "days_to_exam": 20}
    assert "BEPC dans 20 jours" in fake.calls[0]["prompt"] and "inactif depuis 5 jours" in fake.calls[0]["prompt"]


@pytest.mark.parametrize("exam_in,urgency", [(5, "high"), (13, "high"), (14, "medium"), (29, "medium"), (30, "low"), (59, "low")])
def test_micro_intervention_urgency_bands(env, headers_for, exam_in, urgency):
    env.use("ok")
    _situation(env, exam_in=exam_in)
    assert _micro(env, headers_for)["data"]["urgency"] == urgency


@pytest.mark.parametrize("kwargs", [
    {"inactive_days": 3},               # actif il y a 3 jours : pas d'intervention
    {"inactive_days": 0},
    {"exam_in": 60},                    # examen trop lointain
    {"exam_in": None},                  # aucun plan : 999 jours
])
def test_micro_intervention_stays_quiet_when_the_conditions_are_not_met(env, headers_for, kwargs):
    fake = env.use("ok")
    _situation(env, **kwargs)
    assert _micro(env, headers_for) == {"success": True, "data": None, "trigger": False}
    assert fake.calls == []


def test_without_any_activity_the_student_counts_as_inactive_for_thirty_days(env, headers_for):
    env.use("ok")
    _situation(env, inactive_days=None, subject=False)
    data = _micro(env, headers_for)["data"]
    assert data["days_inactive"] == 30 and data["subject"] == "BEPC"        # sans inscription : le nom de l'examen


@pytest.mark.parametrize("scores,trend", [
    ([80, 80, 80, 50, 50, 50, 50], "up"), ([40, 40, 40, 70, 70, 70, 70], "down"),
    ([60, 60, 60, 59, 59, 59, 59], "stable"), ([80, 80, 20], "stable")])
def test_micro_intervention_prompt_carries_the_score_trend(env, headers_for, scores, trend):
    fake = env.use("ok")
    _situation(env, scores=scores)
    _micro(env, headers_for)
    assert f"tendance de notes est : {trend}" in fake.calls[0]["prompt"]


def test_micro_intervention_uses_a_fixed_message_when_the_provider_fails(env, headers_for):
    env.use(error=TimeoutError("lent"))
    _situation(env)
    message = _micro(env, headers_for)["data"]["message"]
    assert message.startswith("Tu y es presque ! 20 jours avant BEPC")


def test_micro_intervention_reports_failure_without_an_http_error(env, headers_for, monkeypatch):
    class Session:
        def query(self, *a, **k):
            raise RuntimeError("base HS")

        def close(self):
            pass

    class Db:
        SessionLocal = staticmethod(lambda: Session())

    monkeypatch.setattr(ec, "Database", lambda: Db())
    r = env.get("/api/exam-coach/micro-intervention/7", headers=headers_for(7))
    assert r.status_code == 200 and r.json() == {"success": False, "trigger": False, "data": None}
