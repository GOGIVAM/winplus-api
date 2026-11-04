"""Corps des routes établissement (routes/institution_routes.py) : prédiction de réussite, benchmark, plan
d'action, élèves à risque, appréciation de bulletin. SQLite en mémoire, fournisseur d'IA simulé, date figée."""

from datetime import date, datetime, timedelta

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

import database as dbm
import routes.institution_routes as ins

NOW = datetime(2026, 6, 17, 12, 0, 0)          # un mercredi
TODAY = NOW.date()


class FrozenDate(date):
    @classmethod
    def today(cls):
        return TODAY


class FrozenDatetime(datetime):
    @classmethod
    def utcnow(cls):
        return NOW


def _day(n):
    return TODAY - timedelta(days=n)


@pytest.fixture
def env(monkeypatch, sqlite_database):
    db = sqlite_database(ins, [dbm.User, dbm.DailyScore, dbm.Enrollment, dbm.Subject])
    monkeypatch.setattr(ins, "_db", db)
    monkeypatch.setattr(ins, "date", FrozenDate)
    monkeypatch.setattr(ins, "datetime", FrozenDatetime)
    app = FastAPI()
    app.include_router(ins.institution_router, prefix="/api")
    client = TestClient(app, raise_server_exceptions=False)
    client.db = db
    return client


def fake_ai(monkeypatch, value):
    calls = []

    def fake(prompt, system, max_tokens=700):
        calls.append({"prompt": prompt, "system": system, "max_tokens": max_tokens})
        return value

    monkeypatch.setattr(ins, "_deepseek_json", fake)
    return calls


def scores(env, user_id, *pairs, quiz_count=1):
    """pairs = (jours_avant, note) ; une ligne DailyScore chacune."""
    start = len(env.db.all(dbm.DailyScore)) + 1
    env.db.add(*[dbm.DailyScore(Id=start + i, UserId=user_id, Date=_day(d), AverageScore=s, QuizCount=quiz_count)
                 for i, (d, s) in enumerate(pairs)])


def student(env, uid, first="Ana", last="Bell", role="student"):
    env.db.add(dbm.User(Id=uid, Email=f"s{uid}@x.test", FirstName=first, LastName=last, Role=role))


# ── _deepseek_json ──────────────────────────────────────────────────────────

class Client:
    def __init__(self, content=None, error=None):
        self.content, self.error = content, error

    def chat(self, **kw):
        if self.error:
            raise self.error
        return {"content": self.content}


@pytest.mark.parametrize("content,expected", [
    ('[{"a": 1}]', [{"a": 1}]), ('```json\n{"a": 2}\n```', {"a": 2}), ("nope", None)])
def test_json_helper_parses_fenced_json(monkeypatch, content, expected):
    monkeypatch.setattr(ins, "get_deepseek_client", lambda: Client(content))
    assert ins._deepseek_json("p", "s") == expected


def test_json_helper_returns_none_on_provider_timeout(monkeypatch):
    monkeypatch.setattr(ins, "get_deepseek_client", lambda: Client(error=TimeoutError("lent")))
    assert ins._deepseek_json("p", "s") is None


# ── helpers de calcul ───────────────────────────────────────────────────────

def test_fetch_scores_averages_per_student_and_keeps_the_last_day(env):
    scores(env, 1, (3, 40), (1, 60), quiz_count=2)
    scores(env, 2, (5, 90))
    session = env.db.get_session()
    try:
        rows = {r["user_id"]: r for r in ins._fetch_student_scores(session, [1, 2, 3])}
        assert ins._fetch_student_scores(session, []) == []
    finally:
        session.close()
    assert set(rows) == {1, 2}                                   # 3 n'a aucun score
    assert rows[1]["avg_score"] == 50.0 and rows[1]["quiz_count"] == 4 and rows[1]["last_active"] == _day(1)
    assert rows[2]["avg_score"] == 90.0


def test_resolve_student_ids_prefers_the_provided_list_then_falls_back_on_students(env):
    student(env, 1), student(env, 2), student(env, 3, role="teacher")
    session = env.db.get_session()
    try:
        assert ins._resolve_student_ids(session, 9, [7, 8]) == [7, 8]
        assert sorted(ins._resolve_student_ids(session, 9, [])) == [1, 2]
    finally:
        session.close()


# ── droits ──────────────────────────────────────────────────────────────────

ROLE_ROUTES = [("post", "/api/institution/class-prediction", {"institution_id": 1}),
               ("get", "/api/institution/benchmark/1", None),
               ("get", "/api/institution/at-risk-students/1", None)]


@pytest.mark.parametrize("method,url,body", ROLE_ROUTES)
def test_institution_routes_need_a_token_and_an_institution_or_admin_role(env, headers_for, method, url, body):
    send = lambda **kw: getattr(env, method)(url, **({"json": body} if body else {}), **kw)
    assert send().status_code in (401, 403)
    for role in ("student", "teacher", "parent"):
        assert send(headers=headers_for(1, role)).status_code == 403
    assert send(headers=headers_for(1, "institution")).status_code == 200
    assert send(headers=headers_for(1, "admin")).status_code == 200


def test_prediction_requires_an_institution_id(env, headers_for):
    assert env.post("/api/institution/class-prediction", json={}, headers=headers_for(1, "institution")).status_code == 422


# ── prédiction ──────────────────────────────────────────────────────────────

URL_PRED = "/api/institution/class-prediction"
INST = lambda h: h(2, "institution")


def test_prediction_without_any_student_is_an_empty_report(env, headers_for, monkeypatch):
    calls = fake_ai(monkeypatch, [])
    body = env.post(URL_PRED, json={"institution_id": 1}, headers=INST(headers_for)).json()
    assert body["student_count"] == 0 and body["predicted_pass_rate"] == 0 and body["confidence"] == "low"
    assert calls == []


def test_prediction_without_scores_asks_students_to_do_their_first_quiz(env, headers_for, monkeypatch):
    calls = fake_ai(monkeypatch, [])
    body = env.post(URL_PRED, json={"institution_id": 1, "student_ids": [1, 2]}, headers=INST(headers_for)).json()
    assert body["student_count"] == 2 and body["key_risk_factors"] == ["Aucune donnée de performance disponible"]
    assert calls == []


def test_prediction_computes_rates_counts_and_risk_factors(env, headers_for, monkeypatch):
    # 5 élèves : 80 (excellent), 60, 50 (seuil), 30 (critique, inactif), 20 (critique, inactif)
    scores(env, 1, (1, 80)), scores(env, 2, (2, 60)), scores(env, 3, (3, 50))
    scores(env, 4, (20, 30)), scores(env, 5, (40, 20))
    calls = fake_ai(monkeypatch, [{"intervention": "Soutien"}])
    body = env.post(URL_PRED, json={"institution_id": 1, "student_ids": [1, 2, 3, 4, 5]},
                    headers=INST(headers_for)).json()
    assert body["student_count"] == 5 and body["confidence"] == "medium"
    assert body["predicted_pass_rate"] == 60.0                       # 3 sur 5, 50 compris
    assert body["at_risk_count"] == 2 and body["likely_to_excel_count"] == 1
    assert body["avg_score"] == 48.0 and body["inactive_count"] == 2
    assert body["key_risk_factors"] == ["Score moyen faible (48.0%)  objectif 60%",
                                        "2 étudiant(s) inactifs depuis +14j",
                                        "2 étudiant(s) en zone critique (score <35%)"]
    assert body["recommended_interventions"] == [{"intervention": "Soutien"}]
    assert "5 étudiants" in calls[0]["prompt"] and "60.0%" in calls[0]["prompt"]


def test_prediction_inactivity_is_flagged_only_above_thirty_percent(env, headers_for, monkeypatch):
    fake_ai(monkeypatch, None)
    for uid in range(1, 5):
        scores(env, uid, (1, 70))
    scores(env, 5, (30, 70))                   # 1 inactif sur 5 = 20 %
    body = env.post(URL_PRED, json={"institution_id": 1, "student_ids": [1, 2, 3, 4, 5]},
                    headers=INST(headers_for)).json()
    assert body["inactive_count"] == 1 and body["key_risk_factors"] == []


@pytest.mark.parametrize("count,expected", [(4, "low"), (5, "medium"), (19, "medium"), (20, "high")])
def test_prediction_confidence_depends_on_the_cohort_size(env, headers_for, monkeypatch, count, expected):
    fake_ai(monkeypatch, [])
    ids = list(range(1, count + 1))
    for uid in ids:
        scores(env, uid, (1, 70))
    body = env.post(URL_PRED, json={"institution_id": 1, "student_ids": ids}, headers=INST(headers_for)).json()
    assert body["confidence"] == expected


def test_prediction_uses_three_default_interventions_when_the_model_fails(env, headers_for, monkeypatch):
    fake_ai(monkeypatch, {"pas": "une liste"})
    scores(env, 1, (1, 70))
    body = env.post(URL_PRED, json={"institution_id": 1, "student_ids": [1]}, headers=INST(headers_for)).json()
    assert len(body["recommended_interventions"]) == 3


# ── benchmark ───────────────────────────────────────────────────────────────

def _bench(env, headers_for, ids="1,2", institution=1):
    return env.get(f"/api/institution/benchmark/{institution}", params={"student_ids": ids} if ids else {},
                   headers=INST(headers_for)).json()


def test_benchmark_compares_the_institution_to_the_rest_of_the_base(env, headers_for):
    scores(env, 1, (1, 80)), scores(env, 2, (1, 40), quiz_count=0)       # moyenne 60, réussite 50 %, engagement 50 %
    scores(env, 10, (1, 50)), scores(env, 11, (1, 50))                   # reste de la base : 50
    body = _bench(env, headers_for)
    assert body["institution_avg_score"] == 60.0 and body["national_avg_score"] == 50.0
    assert body["institution_pass_rate"] == 50.0 and body["engagement_rate"] == 50.0
    assert body["student_count"] == 2 and body["national_student_count"] == 2
    assert body["top_quartile_avg"] == 68.0
    assert body["national_pass_rate"] == 55.0                           # 50 x 1,1
    assert body["institution_percentile"] == 85                         # ratio 1,2


@pytest.mark.parametrize("inst_score,percentile", [(60, 85), (57.5, 70), (52.5, 50), (50, 50), (42.5, 35), (30, 20)])
def test_benchmark_percentile_bands(env, headers_for, inst_score, percentile):
    scores(env, 1, (1, inst_score)), scores(env, 10, (1, 50))
    assert _bench(env, headers_for, ids="1")["institution_percentile"] == percentile


def test_benchmark_defaults_without_other_data(env, headers_for):
    body = _bench(env, headers_for, ids="1")
    assert body["institution_avg_score"] == 0 and body["national_avg_score"] == 60.0
    assert body["national_pass_rate"] == 66.0 and body["strongest_subject"] == "" and body["institution_percentile"] == 20


def test_benchmark_national_pass_rate_is_bounded_and_top_quartile_capped(env, headers_for):
    scores(env, 10, (1, 95))
    body = _bench(env, headers_for, ids="1")
    assert body["national_pass_rate"] == 75.0 and body["top_quartile_avg"] == 92.0
    env.db.add(dbm.DailyScore(Id=99, UserId=11, Date=_day(1), AverageScore=5))
    scores(env, 12, (1, 5))


def test_benchmark_ignores_non_numeric_ids_and_names_best_and_worst_subject(env, headers_for):
    env.db.add(dbm.Subject(Id=1, Title="Maths"), dbm.Subject(Id=2, Title="Français"), dbm.Subject(Id=3, Title="Histoire"),
               dbm.Enrollment(Id=1, UserId=1, SubjectId=1), dbm.Enrollment(Id=2, UserId=2, SubjectId=2),
               dbm.Enrollment(Id=3, UserId=3, SubjectId=3))
    scores(env, 1, (1, 90)), scores(env, 2, (1, 30)), scores(env, 3, (1, 60))
    body = _bench(env, headers_for, ids="1,abc,2,,3")
    assert body["student_count"] == 3
    assert body["strongest_subject"] == "Maths" and body["weakest_subject_vs_peers"] == "Français"


# ── plan d'action ───────────────────────────────────────────────────────────

URL_PLAN = "/api/institution/action-plan"
THREE = [{"priority": i, "action": f"a{i}"} for i in (1, 2, 3)]


def test_action_plan_accepts_institution_admin_and_the_service_token_only(env, headers_for, service_headers, monkeypatch):
    fake_ai(monkeypatch, THREE)
    body = {"institution_id": 1, "student_ids": [1]}
    assert env.post(URL_PLAN, json=body).status_code in (401, 403)
    assert env.post(URL_PLAN, json=body, headers=headers_for(1, "student")).status_code == 403
    assert env.post(URL_PLAN, json=body, headers=headers_for(1, "institution")).status_code == 200
    assert env.post(URL_PLAN, json=body, headers=service_headers("ai.institution-report")).status_code == 200
    assert env.post(URL_PLAN, json=body, headers=service_headers("ai.task")).status_code in (401, 403)


def test_action_plan_uses_the_model_answer_only_when_it_has_exactly_three_actions(env, headers_for, monkeypatch):
    scores(env, 1, (1, 40)), scores(env, 2, (30, 20))
    calls = fake_ai(monkeypatch, THREE)
    body = env.post(URL_PLAN, json={"institution_id": 1, "student_ids": [1, 2], "institution_name": "Lycée Leclerc"},
                    headers=INST(headers_for)).json()
    assert body["actions"] == THREE
    assert body["context"] == {"student_count": 2, "avg_score": 30.0, "inactive_count": 1, "at_risk_count": 2}
    assert "« Lycée Leclerc »" in calls[0]["prompt"] and calls[0]["max_tokens"] == 600
    assert body["week_label"] == "Semaine du 15/06/2026"             # lundi de la semaine du mercredi 17 juin


@pytest.mark.parametrize("model_answer", [THREE[:2], THREE + THREE[:1], None, {"x": 1}])
def test_action_plan_falls_back_to_three_generic_actions(env, headers_for, monkeypatch, model_answer):
    scores(env, 1, (1, 40))
    fake_ai(monkeypatch, model_answer)
    body = env.post(URL_PLAN, json={"institution_id": 1, "student_ids": [1]}, headers=INST(headers_for)).json()
    assert [a["priority"] for a in body["actions"]] == [1, 2, 3]
    assert "de 40% à 48%" in body["actions"][1]["estimated_impact"]


def test_action_plan_fallback_names_the_default_establishment_and_caps_the_gain_at_eighty(env, headers_for, monkeypatch):
    scores(env, 1, (1, 78))
    calls = fake_ai(monkeypatch, None)
    body = env.post(URL_PLAN, json={"institution_id": 1, "student_ids": [1]}, headers=INST(headers_for)).json()
    assert "votre établissement" in calls[0]["prompt"]
    assert "de 78% à 80%" in body["actions"][1]["estimated_impact"]


# ── élèves à risque ─────────────────────────────────────────────────────────

def _risk(env, headers_for, ids=None):
    params = {"student_ids": ids} if ids else {}
    return env.get("/api/institution/at-risk-students/1", params=params, headers=INST(headers_for)).json()


def test_at_risk_without_any_student_is_empty(env, headers_for):
    assert _risk(env, headers_for) == {"students": [], "total_at_risk": 0}


def test_at_risk_scores_are_weighted_and_sorted(env, headers_for):
    student(env, 1, "Actif", "Bon"), student(env, 2, "Faible", "Note"), student(env, 3, "Absent", "Total")
    student(env, 4, "En", "Baisse")
    scores(env, 1, (1, 80))                          # aucun risque
    scores(env, 2, (2, 20))                          # note < 35 : 4
    scores(env, 3, (30, 80))                         # inactif : 3
    scores(env, 4, (10, 70), (3, 40))                # baisse récente (40 contre 70) : 2, moyenne 55
    body = _risk(env, headers_for, "1,2,3,4")
    by_id = {s["user_id"]: s for s in body["students"]}
    assert 1 not in by_id and body["total_at_risk"] == 3 and body["total_students"] == 4
    assert [s["user_id"] for s in body["students"]] == [2, 3, 4]
    assert by_id[2]["risk_score"] == 4 and by_id[2]["risk_level"] == "medium" and by_id[2]["name"] == "Faible Note"
    assert by_id[3]["risk_score"] == 3 and by_id[3]["inactive_days"] == 30
    assert by_id[4]["risk_score"] == 2 and by_id[4]["has_downtrend"] is True


def test_at_risk_student_without_any_score_counts_as_inactive_and_in_critical_zone(env, headers_for):
    student(env, 1)
    body = _risk(env, headers_for, "1")
    s = body["students"][0]
    assert s["risk_score"] == 7 and s["risk_level"] == "high"        # 3 + 4
    assert s["inactive_days"] is None and s["quiz_count"] == 0 and s["avg_score"] == 0


def test_recent_enrollment_doubles_the_risk_and_makes_it_critical(env, headers_for):
    student(env, 1)
    env.db.add(dbm.Enrollment(Id=1, UserId=1, SubjectId=1, EnrolledAt=NOW - timedelta(days=5)))
    s = _risk(env, headers_for, "1")["students"][0]
    assert s["has_upcoming_exam"] is True and s["risk_score"] == 14 and s["risk_level"] == "critical"


def test_recent_enrollment_alone_does_not_make_a_healthy_student_at_risk(env, headers_for):
    student(env, 1)
    scores(env, 1, (1, 90))
    env.db.add(dbm.Enrollment(Id=1, UserId=1, SubjectId=1, EnrolledAt=NOW - timedelta(days=5)))
    assert _risk(env, headers_for, "1")["students"] == []


def test_unknown_user_gets_a_placeholder_name(env, headers_for):
    s = _risk(env, headers_for, "42")["students"][0]
    assert s["name"] == "Étudiant #42" and s["email"] is None


def test_at_risk_list_is_capped_at_one_hundred(env, headers_for):
    for uid in range(1, 106):
        student(env, uid)
    body = _risk(env, headers_for, ",".join(str(i) for i in range(1, 106)))
    assert len(body["students"]) == 100 and body["total_at_risk"] == 105


# ── appréciation de bulletin ────────────────────────────────────────────────

URL_APP = "/api/institution/appreciation"


def test_appreciation_requires_a_student_name_and_the_right_caller(env, headers_for, service_headers, monkeypatch):
    fake_ai(monkeypatch, {"appreciation": "Bien."})
    assert env.post(URL_APP, json={}, headers=INST(headers_for)).status_code == 422
    assert env.post(URL_APP, json={"student_name": "A"}).status_code in (401, 403)
    assert env.post(URL_APP, json={"student_name": "A"}, headers=headers_for(1, "teacher")).status_code == 403
    assert env.post(URL_APP, json={"student_name": "A"}, headers=service_headers("ai.institution-report")).status_code == 200


def test_appreciation_prompt_converts_percentages_to_marks_out_of_twenty(env, headers_for, monkeypatch):
    calls = fake_ai(monkeypatch, {"appreciation": "Trimestre sérieux."})
    payload = {"student_name": "Awa", "period_label": "Trimestre 1", "average": 70, "class_average": 55, "rank": 3,
               "class_size": 30, "homework_given": 10, "homework_done": 8, "active_days": 20, "study_minutes": 180,
               "subjects": [{"subject": "Maths", "average": 80, "quizzes": 4, "classAverage": 60}]}
    body = env.post(URL_APP, json=payload, headers=INST(headers_for)).json()
    assert body == {"appreciation": "Trimestre sérieux."}
    prompt = calls[0]["prompt"]
    assert "Élève : Awa. Période : Trimestre 1." in prompt
    assert "Moyenne générale : 14.0/20" in prompt and "moyenne de la classe : 11.0/20" in prompt
    assert "rang : 3 sur 30" in prompt and "Devoirs faits : 8/10" in prompt and "Temps d'étude : 3 h" in prompt
    assert "- Maths: 16.0/20 (classe 12.0/20, 4 quiz)" in prompt and calls[0]["max_tokens"] == 300


def test_appreciation_prompt_defaults_and_ten_subjects_at_most(env, headers_for, monkeypatch):
    calls = fake_ai(monkeypatch, None)
    env.post(URL_APP, json={"student_name": "Awa"}, headers=INST(headers_for))
    assert "la période" in calls[0]["prompt"] and "aucune note sur la période" in calls[0]["prompt"]
    assert "Moyenne générale : None/20" in calls[0]["prompt"]
    subjects = [{"subject": f"M{i}", "average": 50, "quizzes": 1, "classAverage": 50} for i in range(15)]
    env.post(URL_APP, json={"student_name": "Awa", "subjects": subjects}, headers=INST(headers_for))
    assert "M9:" in calls[1]["prompt"] and "M10:" not in calls[1]["prompt"]


@pytest.mark.parametrize("model_answer,expected", [
    ({"appreciation": '  "Bon trimestre."  '}, "Bon trimestre."),
    ({"appreciation": "   "}, None),
    ({"appreciation": 12}, None),
    ({"autre": "x"}, None),
    (["liste"], None),
    (None, None),
])
def test_appreciation_is_cleaned_or_null(env, headers_for, monkeypatch, model_answer, expected):
    fake_ai(monkeypatch, model_answer)
    body = env.post(URL_APP, json={"student_name": "Awa"}, headers=INST(headers_for)).json()
    assert body == {"appreciation": expected}


def test_appreciation_is_cut_at_six_hundred_characters(env, headers_for, monkeypatch):
    fake_ai(monkeypatch, {"appreciation": "a" * 900})
    assert len(env.post(URL_APP, json={"student_name": "Awa"}, headers=INST(headers_for)).json()["appreciation"]) == 600
