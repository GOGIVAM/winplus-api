"""Routes service à service du conseiller parent (routes/parent_advisor_routes.py) : seul un jeton technique
.NET de la bonne portée est accepté, un enfant en échec n'interrompt pas les autres. Base et calculs simulés."""

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

import routes.parent_advisor_routes as pa

THRESHOLDS = "/api/parent-advisor/threshold-alerts"
WEEKLY = "/api/parent-advisor/weekly-trend"


class FakeSession:
    def __init__(self):
        self.closed = False

    def close(self):
        self.closed = True


@pytest.fixture
def setup(monkeypatch):
    session = FakeSession()
    monkeypatch.setattr(pa, "Database", lambda: type("D", (), {"SessionLocal": lambda self: session})())
    app = FastAPI()
    app.include_router(pa.parent_advisor_router, prefix="/api/parent-advisor")
    client = TestClient(app)
    client.session = session
    return client


def _alert(kind="BaisseScoreSeuil", severity="High", key="scoredrop:2026-03"):
    return {"type": kind, "severity": severity, "content": "texte", "detected_at": "2026-03-15T12:00:00+00:00",
            "dedup_key": key}


# ── droits ──────────────────────────────────────────────────────────────────

@pytest.mark.parametrize("url,body", [(THRESHOLDS, {"links": []}), (WEEKLY, {"child_ids": []})])
def test_no_token_is_rejected(setup, url, body):
    assert setup.post(url, json=body).status_code in (401, 403)


@pytest.mark.parametrize("url,body", [(THRESHOLDS, {"links": []}), (WEEKLY, {"child_ids": []})])
@pytest.mark.parametrize("role", ["admin", "parent", "teacher", "student"])
def test_user_tokens_are_always_forbidden(setup, headers_for, url, body, role):
    # roles=() : la route n'est ouverte qu'à la tâche de fond .NET
    assert setup.post(url, json=body, headers=headers_for(1, role)).status_code == 403


@pytest.mark.parametrize("url,body,own_scope,other_scope", [
    (THRESHOLDS, {"links": []}, "ai.parent-advisor-thresholds", "ai.parent-weekly-trend"),
    (WEEKLY, {"child_ids": []}, "ai.parent-weekly-trend", "ai.parent-advisor-thresholds"),
])
def test_service_token_must_carry_the_scope_of_the_route(setup, service_headers, url, body, own_scope, other_scope):
    assert setup.post(url, json=body, headers=service_headers(own_scope)).status_code == 200
    assert setup.post(url, json=body, headers=service_headers(other_scope)).status_code == 403


def test_invalid_bodies_are_422(setup, service_headers):
    assert setup.post(THRESHOLDS, json={}, headers=service_headers("ai.parent-advisor-thresholds")).status_code == 422
    assert setup.post(THRESHOLDS, json={"links": [{"parent_id": 1}]},
                      headers=service_headers("ai.parent-advisor-thresholds")).status_code == 422
    assert setup.post(WEEKLY, json={"child_ids": ["x"]}, headers=service_headers("ai.parent-weekly-trend")).status_code == 422


# ── seuils ──────────────────────────────────────────────────────────────────

def test_threshold_alerts_are_returned_per_link(setup, monkeypatch, service_headers):
    seen = []

    def fake_detect(child_id, session=None):
        seen.append((child_id, session))
        return [_alert()] if child_id == 20 else []

    monkeypatch.setattr(pa, "detect_threshold_alerts", fake_detect)
    body = {"links": [{"parent_id": 1, "child_id": 10}, {"parent_id": 2, "child_id": 20}]}
    r = setup.post(THRESHOLDS, json=body, headers=service_headers("ai.parent-advisor-thresholds"))
    assert r.status_code == 200
    assert r.json() == {"alerts": [{"parent_id": 2, "child_id": 20, "type": "BaisseScoreSeuil", "severity": "High",
                                    "content": "texte", "detected_at": "2026-03-15T12:00:00+00:00",
                                    "dedup_key": "scoredrop:2026-03"}]}
    assert [c for c, _ in seen] == [10, 20]
    assert all(s is setup.session for _, s in seen)      # une seule session partagée
    assert setup.session.closed is True


def test_a_failing_child_does_not_stop_the_scan(setup, monkeypatch, service_headers):
    def fake_detect(child_id, session=None):
        if child_id == 10:
            raise RuntimeError("base cassée")
        return [_alert(key=f"k{child_id}")]

    monkeypatch.setattr(pa, "detect_threshold_alerts", fake_detect)
    body = {"links": [{"parent_id": 1, "child_id": 10}, {"parent_id": 1, "child_id": 11}]}
    r = setup.post(THRESHOLDS, json=body, headers=service_headers("ai.parent-advisor-thresholds"))
    assert [a["dedup_key"] for a in r.json()["alerts"]] == ["k11"]
    assert setup.session.closed is True


def test_a_parent_with_several_alerts_for_one_child(setup, monkeypatch, service_headers):
    monkeypatch.setattr(pa, "detect_threshold_alerts",
                        lambda child_id, session=None: [_alert(key="a"), _alert("DevoirRetardProlonge", "Medium", "b")])
    r = setup.post(THRESHOLDS, json={"links": [{"parent_id": 1, "child_id": 10}]},
                   headers=service_headers("ai.parent-advisor-thresholds"))
    assert [(a["type"], a["severity"]) for a in r.json()["alerts"]] == [
        ("BaisseScoreSeuil", "High"), ("DevoirRetardProlonge", "Medium")]


# ── tendance hebdomadaire ───────────────────────────────────────────────────

def test_weekly_trend_returns_one_result_per_child(setup, monkeypatch, service_headers):
    monkeypatch.setattr(pa, "compute_weekly_trend", lambda child_id, session=None: {
        "child_id": child_id, "week_avg": 70.0, "prev_avg": 50.0, "quiz_count": 2, "delta": 20.0})
    r = setup.post(WEEKLY, json={"child_ids": [3, 4]}, headers=service_headers("ai.parent-weekly-trend"))
    assert r.status_code == 200
    assert [x["child_id"] for x in r.json()["results"]] == [3, 4]
    assert r.json()["results"][0] == {"child_id": 3, "week_avg": 70.0, "prev_avg": 50.0, "quiz_count": 2, "delta": 20.0}
    assert setup.session.closed is True


def test_weekly_trend_omits_a_child_whose_computation_fails(setup, monkeypatch, service_headers):
    def fake(child_id, session=None):
        if child_id == 3:
            raise RuntimeError("échec")
        return {"child_id": child_id, "week_avg": 0.0, "prev_avg": 0.0, "quiz_count": 0, "delta": 0.0}

    monkeypatch.setattr(pa, "compute_weekly_trend", fake)
    r = setup.post(WEEKLY, json={"child_ids": [3, 4]}, headers=service_headers("ai.parent-weekly-trend"))
    # un enfant absent (et non un zéro silencieux) fait retomber .NET sur son repli pour lui seul
    assert [x["child_id"] for x in r.json()["results"]] == [4]


def test_empty_requests_return_empty_lists(setup, service_headers):
    assert setup.post(THRESHOLDS, json={"links": []}, headers=service_headers("ai.parent-advisor-thresholds")).json() == {"alerts": []}
    assert setup.post(WEEKLY, json={"child_ids": []}, headers=service_headers("ai.parent-weekly-trend")).json() == {"results": []}
