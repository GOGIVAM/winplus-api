"""Corps des routes administrateur (routes/admin_routes.py) : anomalies, santé du contenu, insights,
prévision de revenus, modération. Bases SQLite en mémoire, fournisseur d'IA simulé, date figée."""

import json
from datetime import date, datetime, timedelta
from decimal import Decimal
from types import SimpleNamespace

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient
from sqlalchemy import create_engine
from sqlalchemy.orm import sessionmaker
from sqlalchemy.pool import StaticPool

import database as dbm
import routes.admin_routes as ad

NOW = datetime(2026, 6, 15, 12, 0, 0)


class FrozenDatetime(datetime):
    @classmethod
    def now(cls, tz=None):
        return NOW.replace(tzinfo=tz) if tz else NOW

    @classmethod
    def utcnow(cls):
        return NOW


class FrozenDate(date):
    @classmethod
    def today(cls):
        return NOW.date()


@pytest.fixture
def env(monkeypatch, sqlite_database):
    db = sqlite_database(ad, [dbm.User, dbm.Enrollment, dbm.Subject, dbm.LearningHistory, dbm.DailyScore,
                              dbm.ChatMessage, dbm.Conversation])
    admin_engine = create_engine("sqlite://", connect_args={"check_same_thread": False}, poolclass=StaticPool)
    ad._ABase.metadata.create_all(admin_engine)
    monkeypatch.setattr(ad, "_db", db)
    monkeypatch.setattr(ad, "_admin_session_factory", sessionmaker(bind=admin_engine))
    monkeypatch.setattr(ad, "datetime", FrozenDatetime)
    monkeypatch.setattr(ad, "date", FrozenDate)
    app = FastAPI()
    app.include_router(ad.admin_router, prefix="/api")
    client = TestClient(app, raise_server_exceptions=False)
    client.db = db
    client.admin = sessionmaker(bind=admin_engine)
    return client


def fake_ai(monkeypatch, value):
    """Remplace _deepseek_json ; mémorise les appels."""
    calls = []

    def fake(prompt, system, max_tokens=800):
        calls.append({"prompt": prompt, "system": system, "max_tokens": max_tokens})
        return value

    monkeypatch.setattr(ad, "_deepseek_json", fake)
    return calls


# ── _deepseek_json / _deepseek_text ─────────────────────────────────────────

class FakeClient:
    def __init__(self, content=None, error=None):
        self.content, self.error = content, error

    def chat(self, **kwargs):
        if self.error:
            raise self.error
        return {"content": self.content}


@pytest.mark.parametrize("content,expected", [
    ('{"a": 1}', {"a": 1}),
    ('```json\n{"a": 1}\n```', {"a": 1}),
    ('  [1, 2]  ', [1, 2]),
    ("pas du json", None),
    ("", None),
])
def test_deepseek_json_strips_code_fences_and_returns_none_on_garbage(monkeypatch, content, expected):
    monkeypatch.setattr(ad, "get_deepseek_client", lambda: FakeClient(content))
    assert ad._deepseek_json("p", "s") == expected


def test_deepseek_json_and_text_swallow_provider_errors(monkeypatch):
    monkeypatch.setattr(ad, "get_deepseek_client", lambda: FakeClient(error=TimeoutError("délai dépassé")))
    assert ad._deepseek_json("p", "s") is None
    assert ad._deepseek_text("p", "s") == ""


def test_deepseek_text_strips_whitespace(monkeypatch):
    monkeypatch.setattr(ad, "get_deepseek_client", lambda: FakeClient("  bonjour \n"))
    assert ad._deepseek_text("p", "s") == "bonjour"


# ── _verdict_to_action ──────────────────────────────────────────────────────

@pytest.mark.parametrize("verdict,confidence,expected", [
    ("safe", 0.1, "publish"),
    ("spam", 0.9, "hold_for_review"),
    ("inappropriate", 0.8, "hold_for_review"),     # seuil atteint
    ("spam", 0.79, "publish"),                     # sous le seuil
    ("off_topic", 0.1, "publish_with_flag"),
    ("needs_review", 0.99, "publish_with_flag"),
    ("harassment", 0.99, "publish"),               # verdict inconnu de cette fonction
])
def test_verdict_to_action(verdict, confidence, expected):
    assert ad._verdict_to_action(verdict, confidence, 0.8) == expected


# ── signaux d'anomalies ─────────────────────────────────────────────────────

def _users(db, n, when):
    db.add(*[dbm.User(Id=i, Email=f"u{i}@x.test", CreatedAt=when) for i in range(1, n + 1)])


def test_no_signal_on_an_empty_platform(env):
    session = env.db.get_session()
    try:
        assert ad._detect_anomalies_raw(session) == []
    finally:
        session.close()


def test_account_spike_needs_ten_accounts_and_triple_the_average(env):
    _users(env.db, 30, NOW - timedelta(hours=1))
    session = env.db.get_session()
    try:
        signals = ad._detect_anomalies_raw(session)
    finally:
        session.close()
    spike = [s for s in signals if s["type"] == "account_spike"]
    assert len(spike) == 1
    assert spike[0]["severity"] == "high"                    # 30 > 5 x (30/7)
    assert spike[0]["raw"] == {"new_24h": 30, "avg_daily_7d": round(30 / 7, 1)}


def test_account_spike_is_not_raised_under_ten_accounts(env):
    _users(env.db, 9, NOW - timedelta(hours=1))
    session = env.db.get_session()
    try:
        assert ad._detect_anomalies_raw(session) == []
    finally:
        session.close()


def test_enrollment_drop_when_the_last_day_is_empty_against_a_busy_week(env):
    env.db.add(*[dbm.Enrollment(Id=i, UserId=1, SubjectId=1, EnrolledAt=NOW - timedelta(days=3))
                 for i in range(1, 22)])
    session = env.db.get_session()
    try:
        signals = ad._detect_anomalies_raw(session)
    finally:
        session.close()
    drop = [s for s in signals if s["type"] == "enrollment_drop"]
    assert drop and drop[0]["raw"] == {"enroll_24h": 0, "avg_daily": 3.0}


def test_deleted_enrollments_are_ignored(env):
    env.db.add(*[dbm.Enrollment(Id=i, UserId=1, SubjectId=1, EnrolledAt=NOW - timedelta(days=3), IsDeleted=True)
                 for i in range(1, 22)])
    session = env.db.get_session()
    try:
        assert ad._detect_anomalies_raw(session) == []
    finally:
        session.close()


def test_nocturnal_spike_above_fifty_events_between_2h_and_4h(env):
    env.db.add(*[dbm.LearningHistory(Id=i, UserId=1, SubjectId=1, Timestamp=datetime(2026, 6, 15, 3, 0))
                 for i in range(1, 52)])
    session = env.db.get_session()
    try:
        signals = ad._detect_anomalies_raw(session)
    finally:
        session.close()
    assert [s["type"] for s in signals] == ["nocturnal_spike"]
    assert signals[0]["raw"]["night_activity"] == 51


def test_score_drop_when_today_is_under_seventy_percent_of_yesterday(env):
    env.db.add(dbm.DailyScore(Id=1, UserId=1, Date=date(2026, 6, 15), AverageScore=50),
               dbm.DailyScore(Id=2, UserId=1, Date=date(2026, 6, 14), AverageScore=80))
    session = env.db.get_session()
    try:
        signals = ad._detect_anomalies_raw(session)
    finally:
        session.close()
    assert signals == [{"type": "score_drop", "severity": "high",
                        "raw": {"today_avg": 50.0, "yesterday_avg": 80.0}}]


def test_score_is_not_a_drop_at_exactly_seventy_percent(env):
    env.db.add(dbm.DailyScore(Id=1, UserId=1, Date=date(2026, 6, 15), AverageScore=56),
               dbm.DailyScore(Id=2, UserId=1, Date=date(2026, 6, 14), AverageScore=80))
    session = env.db.get_session()
    try:
        assert ad._detect_anomalies_raw(session) == []
    finally:
        session.close()


def test_topic_concentration_counts_long_words_only(env):
    env.db.add(dbm.Conversation(Id=1, UserId=1),
               *[dbm.ChatMessage(Id=i, ConversationId=1, Role="user", Content="derivation court mot",
                                 CreatedAt=NOW - timedelta(days=1)) for i in range(1, 32)])
    session = env.db.get_session()
    try:
        signals = ad._detect_anomalies_raw(session)
    finally:
        session.close()
    topic = [s for s in signals if s["type"] == "topic_concentration"]
    assert topic and topic[0]["severity"] == "low"
    assert topic[0]["raw"]["occurrences"] == 31 and topic[0]["raw"]["keyword"] in ("derivation",)


def test_signal_collection_errors_are_swallowed():
    class Broken:
        def query(self, *a, **k):
            raise RuntimeError("base indisponible")

    assert ad._detect_anomalies_raw(Broken()) == []


# ── POST /admin/anomaly-detection ───────────────────────────────────────────

URL_DETECT = "/api/admin/anomaly-detection"


def test_detection_requires_authentication_and_the_admin_role(env, headers_for):
    assert env.post(URL_DETECT, json={}).status_code in (401, 403)
    assert env.post(URL_DETECT, json={}, headers=headers_for(1, "teacher")).status_code == 403


def test_detection_without_signal_returns_an_empty_list_and_never_calls_the_model(env, headers_for, monkeypatch):
    calls = fake_ai(monkeypatch, [])
    r = env.post(URL_DETECT, json={}, headers=headers_for(1, "admin"))
    assert r.status_code == 200
    assert r.json() == {"anomalies": [], "scanned_at": NOW.isoformat()}
    assert calls == []


def test_detection_rejects_a_non_boolean_force_refresh(env, headers_for):
    r = env.post(URL_DETECT, json={"force_refresh": "peut-etre"}, headers=headers_for(1, "admin"))
    assert r.status_code == 422


SIGNALS = [{"type": "account_spike", "severity": "high", "raw": {"new_24h": 30, "avg_daily_7d": 4.3}},
           {"type": "nocturnal_spike", "severity": "medium", "raw": {"night_activity": 60}}]


def test_detection_stores_the_interpreted_anomalies(env, headers_for, monkeypatch):
    monkeypatch.setattr(ad, "_detect_anomalies_raw", lambda session: SIGNALS)
    interpreted = [{"type": "account_spike", "severity": "high", "description": "Pic.", "action_suggestion": "Vérifier."},
                   {"type": "nocturnal_spike", "severity": "medium", "description": "Nuit.", "action_suggestion": "Voir."}]
    calls = fake_ai(monkeypatch, interpreted)
    r = env.post(URL_DETECT, json={}, headers=headers_for(1, "admin"))
    assert r.status_code == 200
    assert r.json()["anomalies"] == interpreted and r.json()["new_count"] == 2
    assert "account_spike" in calls[0]["prompt"] and calls[0]["max_tokens"] == 1000
    session = env.admin()
    try:
        rows = session.query(ad.PlatformAnomalyModel).order_by(ad.PlatformAnomalyModel.Id).all()
        assert [r.Type for r in rows] == ["account_spike", "nocturnal_spike"]
        assert json.loads(rows[0].Metadata) == {"action_suggestion": "Vérifier."}
    finally:
        session.close()


def test_detection_falls_back_to_raw_signals_when_the_model_answers_badly(env, headers_for, monkeypatch):
    monkeypatch.setattr(ad, "_detect_anomalies_raw", lambda session: SIGNALS)
    fake_ai(monkeypatch, None)
    body = env.post(URL_DETECT, json={}, headers=headers_for(1, "admin")).json()
    assert [a["type"] for a in body["anomalies"]] == ["account_spike", "nocturnal_spike"]
    assert all(a["action_suggestion"] == "Investiguer manuellement." for a in body["anomalies"])
    assert "new_24h" in body["anomalies"][0]["description"]
    assert body["new_count"] == 2


def test_detection_does_not_store_a_type_already_open_in_the_last_24_hours(env, headers_for, monkeypatch):
    monkeypatch.setattr(ad, "_detect_anomalies_raw", lambda session: SIGNALS[:1])
    fake_ai(monkeypatch, None)
    first = env.post(URL_DETECT, json={}, headers=headers_for(1, "admin")).json()
    second = env.post(URL_DETECT, json={}, headers=headers_for(1, "admin")).json()
    assert first["new_count"] == 1 and second["new_count"] == 0


# ── GET /admin/anomalies/active et résolution ───────────────────────────────

def _anomaly(env, **kw):
    session = env.admin()
    try:
        row = ad.PlatformAnomalyModel(**kw)
        session.add(row)
        session.commit()
        return row.Id
    finally:
        session.close()


def test_active_anomalies_are_sorted_by_severity_and_skip_resolved_ones(env, headers_for):
    _anomaly(env, Type="a", Severity="low", Description="d1", DetectedAt=NOW)
    _anomaly(env, Type="b", Severity="critical", Description="d2", DetectedAt=NOW - timedelta(hours=2),
             Metadata=json.dumps({"action_suggestion": "Agir"}))
    _anomaly(env, Type="c", Severity="medium", Description="d3", DetectedAt=NOW - timedelta(hours=1))
    _anomaly(env, Type="d", Severity="high", Description="d4", DetectedAt=NOW, ResolvedAt=NOW)
    _anomaly(env, Type="e", Severity="bizarre", Description="d5", DetectedAt=NOW)
    r = env.get("/api/admin/anomalies/active", headers=headers_for(1, "admin"))
    body = r.json()
    assert [a["type"] for a in body["anomalies"]] == ["b", "c", "a", "e"]
    assert body["total"] == 4
    assert body["anomalies"][0]["action_suggestion"] == "Agir"
    assert body["anomalies"][1]["action_suggestion"] == ""


def test_active_anomalies_are_capped_at_fifty(env, headers_for):
    for i in range(55):
        _anomaly(env, Type=f"t{i}", Severity="low", Description="d", DetectedAt=NOW - timedelta(minutes=i))
    assert env.get("/api/admin/anomalies/active", headers=headers_for(1, "admin")).json()["total"] == 50


def test_resolving_an_anomaly_stamps_it_and_removes_it_from_the_active_list(env, headers_for):
    anomaly_id = _anomaly(env, Type="a", Severity="low", Description="d", DetectedAt=NOW)
    r = env.post(f"/api/admin/anomaly/{anomaly_id}/resolve", params={"resolution": "RAS"},
                 headers=headers_for(1, "admin"))
    assert r.status_code == 200 and r.json() == {"success": True}
    session = env.admin()
    try:
        row = session.query(ad.PlatformAnomalyModel).one()
        assert row.Resolution == "RAS" and row.ResolvedAt == NOW
    finally:
        session.close()
    assert env.get("/api/admin/anomalies/active", headers=headers_for(1, "admin")).json()["total"] == 0


def test_resolving_requires_a_note_and_an_existing_anomaly(env, headers_for):
    admin = headers_for(1, "admin")
    assert env.post("/api/admin/anomaly/1/resolve", headers=admin).status_code == 422
    assert env.post("/api/admin/anomaly/999/resolve", params={"resolution": "x"}, headers=admin).status_code == 404
    assert env.post("/api/admin/anomaly/abc/resolve", params={"resolution": "x"}, headers=admin).status_code == 422


# ── POST /admin/content-health-check ────────────────────────────────────────

URL_HEALTH = "/api/admin/content-health-check"


def test_health_check_validates_the_body_and_the_role(env, headers_for):
    assert env.post(URL_HEALTH, json={}, headers=headers_for(1, "admin")).status_code == 422
    assert env.post(URL_HEALTH, json={"content_id": 1}, headers=headers_for(1, "student")).status_code == 403


def test_health_check_without_model_answer_and_without_pdf(env, headers_for, monkeypatch):
    fake_ai(monkeypatch, None)
    body = env.post(URL_HEALTH, json={"content_id": 5}, headers=headers_for(1, "admin")).json()
    assert body["content_id"] == 5 and body["quality_score"] == 40
    assert body["recommendation"] == "request_revision"
    assert body["issues"][0]["type"] == "incomplete_content"


def test_health_check_without_model_answer_but_with_a_long_pdf(env, headers_for, monkeypatch):
    fake_ai(monkeypatch, "pas un dict")
    body = env.post(URL_HEALTH, json={"content_id": 5, "pdf_text": "x" * 200},
                    headers=headers_for(1, "admin")).json()
    assert body["quality_score"] == 65 and body["issues"] == [] and body["recommendation"] == "approve"


def test_health_check_prompt_uses_metadata_and_cuts_the_excerpt(env, headers_for, monkeypatch):
    calls = fake_ai(monkeypatch, {"quality_score": 80, "issues": []})
    env.post(URL_HEALTH, json={"content_id": 5, "pdf_text": "a" * 1500 + "ZZZ",
                               "metadata": {"title": "BAC 2024", "type": "epreuve", "subject": "Maths"}},
             headers=headers_for(1, "admin"))
    prompt = calls[0]["prompt"]
    assert "Titre : BAC 2024" in prompt and "Matière : Maths" in prompt
    assert "a" * 1500 in prompt and "ZZZ" not in prompt
    assert calls[0]["max_tokens"] == 700


def test_health_check_short_pdf_is_reported_as_unavailable(env, headers_for, monkeypatch):
    calls = fake_ai(monkeypatch, {"quality_score": 80, "issues": []})
    env.post(URL_HEALTH, json={"content_id": 5, "pdf_text": "court"}, headers=headers_for(1, "admin"))
    assert "Non disponible" in calls[0]["prompt"] and "Contenu #5" in calls[0]["prompt"]


@pytest.mark.parametrize("model_answer,expected", [
    ({"quality_score": 85, "issues": [], "recommendation": "reject"}, "approve"),            # score >= 70 sans souci grave
    ({"quality_score": 70, "issues": [{"severity": "low"}], "recommendation": "reject"}, "approve"),
    ({"quality_score": 85, "issues": [{"severity": "high"}], "recommendation": "approve"}, "approve"),  # conserve la réponse
    ({"quality_score": 20, "issues": [], "recommendation": "approve"}, "reject"),            # score < 30
    ({"quality_score": 29, "issues": []}, "reject"),
    ({"quality_score": 90, "issues": [{"type": "copyright_concern", "severity": "low"}]}, "reject"),
    ({"quality_score": 50, "issues": []}, "request_revision"),                              # défaut
    ({"quality_score": 50, "issues": [], "recommendation": "approve"}, "approve"),
])
def test_health_check_recommendation_is_aligned_on_the_score(env, headers_for, monkeypatch, model_answer, expected):
    fake_ai(monkeypatch, dict(model_answer))
    body = env.post(URL_HEALTH, json={"content_id": 1}, headers=headers_for(1, "admin")).json()
    assert body["recommendation"] == expected


# ── POST /admin/growth-insights ─────────────────────────────────────────────

URL_GROWTH = "/api/admin/growth-insights"


def test_growth_insights_use_the_stats_given_without_touching_the_base(env, headers_for, monkeypatch):
    calls = fake_ai(monkeypatch, [{"insight": str(i)} for i in range(7)])
    stats = {"total_users": 1234, "period_days": 30}
    body = env.post(URL_GROWTH, json={"period_days": 30, "stats": stats}, headers=headers_for(1, "admin")).json()
    assert body["unavailable"] is False and len(body["insights"]) == 5      # coupé à 5
    assert body["data_context"] == stats and body["period_days"] == 30
    assert "1234 utilisateurs" in calls[0]["prompt"] and "30 derniers jours" in calls[0]["prompt"]


def test_growth_insights_compute_real_stats_when_none_are_given(env, headers_for, monkeypatch):
    env.db.add(dbm.Subject(Id=1, Title="Maths", Category="Sciences"), dbm.Subject(Id=2, Title="Anglais", Category="Langues"),
               dbm.User(Id=1, Email="a@x.test", CreatedAt=NOW - timedelta(days=2)),
               dbm.User(Id=2, Email="b@x.test", CreatedAt=NOW - timedelta(days=20)),
               dbm.Enrollment(Id=1, UserId=1, SubjectId=1, EnrolledAt=NOW - timedelta(days=1)),
               dbm.Enrollment(Id=2, UserId=2, SubjectId=1, EnrolledAt=NOW - timedelta(days=2)),
               dbm.Enrollment(Id=3, UserId=2, SubjectId=2, EnrolledAt=NOW - timedelta(days=3)),
               dbm.Enrollment(Id=4, UserId=2, SubjectId=2, EnrolledAt=NOW - timedelta(days=30)),
               dbm.DailyScore(Id=1, UserId=1, Date=date(2026, 6, 14), AverageScore=70),
               dbm.DailyScore(Id=2, UserId=1, Date=date(2026, 6, 4), AverageScore=50))
    fake_ai(monkeypatch, [{"insight": "ok"}])
    ctx = env.post(URL_GROWTH, json={"period_days": 7}, headers=headers_for(1, "admin")).json()["data_context"]
    assert ctx["new_users"] == 1 and ctx["total_users"] == 2 and ctx["new_enrollments"] == 3
    assert ctx["avg_score"] == 70.0 and ctx["prev_avg_score"] == 50.0 and ctx["score_trend"] == 20.0
    assert ctx["top_subjects"][0] == {"title": "Maths", "category": "Sciences", "enrollments": 2}


@pytest.mark.parametrize("model_answer", [None, [], "texte", {"insight": "x"}])
def test_growth_insights_report_unavailable_instead_of_inventing_text(env, headers_for, monkeypatch, model_answer):
    fake_ai(monkeypatch, model_answer)
    body = env.post(URL_GROWTH, json={"stats": {"total_users": 3}}, headers=headers_for(1, "admin")).json()
    assert body["insights"] == [] and body["unavailable"] is True and body["reason"]


def test_growth_insights_reject_a_non_integer_period(env, headers_for):
    assert env.post(URL_GROWTH, json={"period_days": "abc"}, headers=headers_for(1, "admin")).status_code == 422


# ── POST /admin/revenue-forecast ────────────────────────────────────────────

URL_REV = "/api/admin/revenue-forecast"


class FakeResult:
    def __init__(self, rows):
        self.rows = rows

    def fetchall(self):
        return self.rows


class FakePaymentsSession:
    def __init__(self, rows=None, error=None):
        self.rows, self.error, self.executed, self.closed = rows or [], error, [], False

    def execute(self, statement, params=None):
        self.executed.append((str(statement), params))
        if self.error:
            raise self.error
        return FakeResult(self.rows)

    def close(self):
        self.closed = True


def _forecast_env(env, monkeypatch, rows=None, error=None):
    session = FakePaymentsSession(rows, error)
    monkeypatch.setattr(ad, "_db", SimpleNamespace(SessionLocal=lambda: session))
    return session


def _pay(month, revenue, payments=1):
    return SimpleNamespace(month=f"{month}-01 00:00:00+00:00", revenue=revenue, payments=payments)


def test_forecast_needs_the_admin_role(env, headers_for):
    assert env.post(URL_REV, json={}, headers=headers_for(1, "parent")).status_code == 403


def test_forecast_with_no_payment_is_marked_insufficient(env, headers_for, monkeypatch):
    session = _forecast_env(env, monkeypatch)
    body = env.post(URL_REV, json={}, headers=headers_for(1, "admin")).json()
    assert body["insufficient_data"] is True and body["current_mrr"] == 0 and body["monthly_history"] == []
    assert body["forecast_3m"] is None and body["growth_rate_pct"] is None and body["trend"] == "stable"
    assert body["currency"] == "XAF" and body["basis"] == "payments_completed"
    assert session.executed[0][1]["start"].isoformat().startswith("2025-06-01") and session.closed


def test_forecast_with_a_single_paid_month_is_insufficient(env, headers_for, monkeypatch):
    _forecast_env(env, monkeypatch, [_pay("2026-05", Decimal("90000"))])
    body = env.post(URL_REV, json={"currency": "EUR"}, headers=headers_for(1, "admin")).json()
    assert body["insufficient_data"] is True and body["current_mrr"] == 90000 and body["currency"] == "EUR"


def test_forecast_compounds_the_monthly_growth_over_the_last_three_months(env, headers_for, monkeypatch):
    _forecast_env(env, monkeypatch, [_pay("2026-03", 100000, 4), _pay("2026-04", 110000), _pay("2026-05", 121000),
                                     _pay("2026-06", 40000)])
    body = env.post(URL_REV, json={}, headers=headers_for(1, "admin")).json()
    assert body["insufficient_data"] is False
    assert [h["month"] for h in body["monthly_history"]] == ["2026-03", "2026-04", "2026-05"]
    assert body["monthly_history"][0] == {"month": "2026-03", "revenue": 100000, "payments": 4}
    assert body["current_mrr"] == 121000 and body["month_to_date"] == 40000     # le mois en cours n'est pas projeté
    assert body["growth_rate_pct"] == pytest.approx(10.0, abs=0.01) and body["trend"] == "up"
    assert body["forecast_3m"] == 161051 and body["forecast_6m"] == 214359 and body["forecast_12m"] == 379750


def test_forecast_history_keeps_real_zero_months_after_the_first_payment(env, headers_for, monkeypatch):
    _forecast_env(env, monkeypatch, [_pay("2026-02", 50000), _pay("2026-04", 50000)])
    history = env.post(URL_REV, json={}, headers=headers_for(1, "admin")).json()["monthly_history"]
    assert [(h["month"], h["revenue"]) for h in history] == [
        ("2026-02", 50000), ("2026-03", 0), ("2026-04", 50000), ("2026-05", 0)]


def test_forecast_trend_down_and_stable(env, headers_for, monkeypatch):
    _forecast_env(env, monkeypatch, [_pay("2026-04", 100000), _pay("2026-05", 80000)])
    body = env.post(URL_REV, json={}, headers=headers_for(1, "admin")).json()
    assert body["growth_rate_pct"] == -20.0 and body["trend"] == "down" and body["forecast_3m"] == round(80000 * 0.8 ** 3)
    _forecast_env(env, monkeypatch, [_pay("2026-04", 100000), _pay("2026-05", 101000)])
    assert env.post(URL_REV, json={}, headers=headers_for(1, "admin")).json()["trend"] == "stable"


def test_forecast_rate_is_zero_when_the_window_starts_at_zero(env, headers_for, monkeypatch):
    # fenêtre de 3 mois dont le premier est vide : pas de division par zéro
    _forecast_env(env, monkeypatch, [_pay("2026-02", 10), _pay("2026-04", 100000), _pay("2026-05", 100000)])
    body = env.post(URL_REV, json={}, headers=headers_for(1, "admin")).json()
    assert body["growth_rate_pct"] == 0.0 and body["forecast_12m"] == 100000


def test_forecast_database_failure_is_a_clean_500(env, headers_for, monkeypatch):
    session = _forecast_env(env, monkeypatch, error=RuntimeError("connexion perdue"))
    r = env.post(URL_REV, json={}, headers=headers_for(1, "admin"))
    assert r.status_code == 500 and r.json()["detail"] == "Prévision indisponible" and session.closed


# ── POST /admin/moderate-content ────────────────────────────────────────────

URL_MOD = "/api/admin/moderate-content"
POST = {"post_id": 10, "thread_id": 2, "content_text": "Bonjour à tous", "author_id": 4}


def test_moderation_requires_a_token_but_not_the_admin_role(env, headers_for, monkeypatch):
    fake_ai(monkeypatch, {"verdict": "safe", "confidence": 0.9, "reason": "ok"})
    assert env.post(URL_MOD, json=POST).status_code in (401, 403)
    assert env.post(URL_MOD, json=POST, headers=headers_for(4, "student")).status_code == 200


@pytest.mark.parametrize("missing", ["post_id", "thread_id", "content_text"])
def test_moderation_requires_its_fields(env, headers_for, missing):
    body = {k: v for k, v in POST.items() if k != missing}
    assert env.post(URL_MOD, json=body, headers=headers_for(4, "student")).status_code == 422


def test_safe_post_is_stored_as_auto_approved_and_published(env, headers_for, monkeypatch):
    fake_ai(monkeypatch, {"verdict": "safe", "confidence": 0.97, "reason": "Pertinent."})
    body = env.post(URL_MOD, json=POST, headers=headers_for(4, "student")).json()
    assert body == {"post_id": 10, "verdict": "safe", "confidence": 0.97, "reason": "Pertinent.", "action": "publish"}
    session = env.admin()
    try:
        assert session.query(ad.ForumModerationModel).one().Status == "auto_approved"
    finally:
        session.close()


def test_spam_post_is_held_and_queued_as_pending(env, headers_for, monkeypatch):
    fake_ai(monkeypatch, {"verdict": "spam", "confidence": 0.95, "reason": "Pub."})
    body = env.post(URL_MOD, json=POST, headers=headers_for(4, "student")).json()
    assert body["action"] == "hold_for_review"
    pending = env.get("/api/admin/moderation/pending", headers=headers_for(1, "admin")).json()
    assert pending["total"] == 1 and pending["posts"][0]["post_id"] == 10 and pending["posts"][0]["status"] == "pending"


def test_moderation_threshold_changes_the_action(env, headers_for, monkeypatch):
    fake_ai(monkeypatch, {"verdict": "inappropriate", "confidence": 0.6, "reason": "x"})
    body = env.post(URL_MOD, json={**POST, "confidence_threshold": 0.5}, headers=headers_for(4, "student")).json()
    assert body["action"] == "hold_for_review"


def test_unusable_model_answer_means_needs_review(env, headers_for, monkeypatch):
    fake_ai(monkeypatch, None)
    body = env.post(URL_MOD, json=POST, headers=headers_for(4, "student")).json()
    assert body["verdict"] == "needs_review" and body["confidence"] == 0.5 and body["action"] == "publish_with_flag"


def test_content_is_cut_in_the_prompt_and_in_the_storage(env, headers_for, monkeypatch):
    calls = fake_ai(monkeypatch, {"verdict": "safe", "confidence": 1})
    env.post(URL_MOD, json={**POST, "content_text": "a" * 1000 + "b" * 1500}, headers=headers_for(4, "student"))
    assert "a" * 1000 in calls[0]["prompt"] and "b" not in calls[0]["prompt"].split("---")[1]
    session = env.admin()
    try:
        assert len(session.query(ad.ForumModerationModel).one().ContentText) == 2000
    finally:
        session.close()


def test_an_already_moderated_post_is_returned_without_calling_the_model(env, headers_for, monkeypatch):
    calls = fake_ai(monkeypatch, {"verdict": "spam", "confidence": 0.9, "reason": "Pub."})
    env.post(URL_MOD, json=POST, headers=headers_for(4, "student"))
    again = env.post(URL_MOD, json={**POST, "confidence_threshold": 0.99}, headers=headers_for(4, "student")).json()
    assert len(calls) == 1
    assert again["verdict"] == "spam" and again["action"] == "publish"          # sous le nouveau seuil


# ── POST /admin/moderate-message ────────────────────────────────────────────

URL_MSG = "/api/admin/moderate-message"


def test_message_moderation_stores_nothing_and_cuts_the_text(env, headers_for, monkeypatch):
    calls = fake_ai(monkeypatch, {"verdict": "harassment", "confidence": 0.9, "reason": "Insulte."})
    body = env.post(URL_MSG, json={"content": "x" * 900, "author_id": 3}, headers=headers_for(3, "student")).json()
    assert body == {"verdict": "harassment", "confidence": 0.9, "reason": "Insulte.", "action": "publish"}
    assert "x" * 800 in calls[0]["prompt"] and "x" * 801 not in calls[0]["prompt"]
    assert calls[0]["max_tokens"] == 150
    session = env.admin()
    try:
        assert session.query(ad.ForumModerationModel).count() == 0
    finally:
        session.close()


def test_message_moderation_defaults_to_review_when_the_model_fails(env, headers_for, monkeypatch):
    fake_ai(monkeypatch, None)
    body = env.post(URL_MSG, json={"content": "salut", "author_id": 3}, headers=headers_for(3, "student")).json()
    assert body["verdict"] == "needs_review" and body["action"] == "publish_with_flag"


def test_message_moderation_validation(env, headers_for):
    assert env.post(URL_MSG, json={"content": "salut"}, headers=headers_for(3, "student")).status_code == 422
    assert env.post(URL_MSG, json={"content": "a", "author_id": 1}).status_code in (401, 403)


# ── liste d'attente et résolution ───────────────────────────────────────────

def _queue(env, post_id, status="pending", created=NOW):
    session = env.admin()
    try:
        row = ad.ForumModerationModel(PostId=post_id, ThreadId=1, ContentText="t", Verdict="spam", Confidence=0.9,
                                      Status=status, CreatedAt=created)
        session.add(row)
        session.commit()
        return row.Id
    finally:
        session.close()


def test_pending_list_is_admin_only_newest_first_and_skips_resolved(env, headers_for):
    _queue(env, 1, created=NOW - timedelta(hours=3))
    _queue(env, 2, created=NOW - timedelta(hours=1))
    _queue(env, 3, status="approved")
    assert env.get("/api/admin/moderation/pending", headers=headers_for(1, "student")).status_code == 403
    body = env.get("/api/admin/moderation/pending", headers=headers_for(1, "admin")).json()
    assert [p["post_id"] for p in body["posts"]] == [2, 1] and body["total"] == 2


def test_resolving_a_moderation_approves_or_rejects(env, headers_for):
    admin = headers_for(1, "admin")
    first, second = _queue(env, 1), _queue(env, 2)
    r = env.post(f"/api/admin/moderation/{first}/resolve", params={"action": "approve", "note": "RAS"}, headers=admin)
    assert r.json() == {"success": True, "post_id": 1, "action": "approve"}
    env.post(f"/api/admin/moderation/{second}/resolve", params={"action": "n'importe quoi"}, headers=admin)
    session = env.admin()
    try:
        rows = {r.PostId: r for r in session.query(ad.ForumModerationModel).all()}
        assert rows[1].Status == "approved" and rows[1].AdminNote == "RAS" and rows[1].ResolvedAt == NOW
        assert rows[2].Status == "rejected"        # toute action autre que "approve" rejette
    finally:
        session.close()


def test_resolving_checks_existence_and_parameters(env, headers_for):
    admin = headers_for(1, "admin")
    assert env.post("/api/admin/moderation/99/resolve", params={"action": "approve"}, headers=admin).status_code == 404
    assert env.post("/api/admin/moderation/1/resolve", headers=admin).status_code == 422
    assert env.post("/api/admin/moderation/1/resolve", params={"action": "approve"},
                    headers=headers_for(1, "teacher")).status_code == 403
