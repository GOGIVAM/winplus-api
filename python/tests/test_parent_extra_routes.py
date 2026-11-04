"""Comparaison inter-enfants (routes/parent_extra_routes.py, GET /parent/children-insights) : droits, lien
parent-enfant, calculs par enfant, observations du modèle et repli déterministe. Base SQLite en mémoire,
contexte partagé et modèle d'IA simulés."""

import json
from datetime import date, datetime, timedelta, timezone

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

import database as dbm
import routes.parent_extra_routes as pe

UTC = timezone.utc


def _ago(days):
    return datetime.now(UTC) - timedelta(days=days)


class FakeDeepSeek:
    def __init__(self, content=None, error=None):
        self.content, self.error, self.calls = content, error, []

    def chat(self, messages, system_prompt=None, max_tokens=None, temperature=None, json_mode=False):
        self.calls.append({"messages": messages, "system": system_prompt, "max_tokens": max_tokens})
        if self.error:
            raise self.error
        return {"content": self.content}


CONTEXTS = {
    10: {"name": "Marie", "avg_score": 12.0, "late_homework": [{"id": 1}, {"id": 2}], "quiz_gaps": ["Maths", "SVT"]},
    11: {"name": "Paul", "avg_score": None, "late_homework": [], "quiz_gaps": []},
}


@pytest.fixture
def setup(monkeypatch, sqlite_database):
    db = sqlite_database(pe, [dbm.ParentStudentLink, dbm.QuizAttempt, dbm.DailyScore])
    db.add(dbm.ParentStudentLink(Id=1, ParentId=5, StudentId=10, Status="accepted"),
           dbm.ParentStudentLink(Id=2, ParentId=5, StudentId=11, Status="accepted"),
           dbm.ParentStudentLink(Id=3, ParentId=5, StudentId=12, Status="pending"),
           # activité de Marie : 3 quiz dont un trop ancien ; scores sur 2 jours distincts (dont un en double)
           dbm.QuizAttempt(Id=1, UserId=10, CompletedAt=_ago(2)), dbm.QuizAttempt(Id=2, UserId=10, CompletedAt=_ago(9)),
           dbm.QuizAttempt(Id=3, UserId=10, CompletedAt=_ago(60)),
           dbm.DailyScore(Id=1, UserId=10, Date=date(2026, 3, 1), AverageScore=60, CreatedAt=_ago(3)),
           dbm.DailyScore(Id=2, UserId=10, Date=date(2026, 3, 1), AverageScore=70, CreatedAt=_ago(3)),
           dbm.DailyScore(Id=3, UserId=10, Date=date(2026, 3, 2), AverageScore=80, CreatedAt=_ago(2)),
           dbm.DailyScore(Id=4, UserId=10, Date=date(2025, 1, 1), AverageScore=50, CreatedAt=_ago(90)))
    monkeypatch.setattr(pe, "compute_child_context", lambda child_id, session=None: CONTEXTS.get(child_id))
    fake = FakeDeepSeek(json.dumps(["Marie est régulière.", "Paul progresse.", "Un point commun."]))
    monkeypatch.setattr(pe, "get_deepseek_client", lambda: fake)
    app = FastAPI()
    app.include_router(pe.parent_extra_router, prefix="/api")
    client = TestClient(app, raise_server_exceptions=False)
    client.fake, client.db = fake, db
    return client


URL = "/api/parent/children-insights"


# ── droits ──────────────────────────────────────────────────────────────────

def test_requires_authentication(setup):
    assert setup.get(URL, params={"child_ids": "10,11"}).status_code in (401, 403)


@pytest.mark.parametrize("role", ["student", "teacher", "institution"])
def test_only_parents_and_admins_pass_the_role_guard(setup, headers_for, role):
    assert setup.get(URL, params={"child_ids": "10,11"}, headers=headers_for(5, role)).status_code == 403


def test_child_ids_is_required(setup, headers_for):
    assert setup.get(URL, headers=headers_for(5, "parent")).status_code == 422


# ── identifiants ────────────────────────────────────────────────────────────

@pytest.mark.parametrize("child_ids", ["10", "", "abc", "10,abc", " 10 , x "])
def test_fewer_than_two_valid_ids_gives_an_empty_answer_without_calling_the_model(setup, headers_for, child_ids):
    r = setup.get(URL, params={"child_ids": child_ids}, headers=headers_for(5, "parent"))
    assert r.status_code == 200 and r.json() == {"insights": [], "children_summary": []}
    assert setup.fake.calls == []


def test_a_child_who_is_not_linked_is_403_and_the_model_is_not_called(setup, headers_for):
    r = setup.get(URL, params={"child_ids": "10,99"}, headers=headers_for(5, "parent"))
    assert r.status_code == 403 and "n'est pas lié" in r.json()["detail"]
    assert setup.get(URL, params={"child_ids": "10,12"}, headers=headers_for(5, "parent")).status_code == 403
    assert setup.fake.calls == []


def test_another_parent_cannot_compare_these_children(setup, headers_for):
    assert setup.get(URL, params={"child_ids": "10,11"}, headers=headers_for(6, "parent")).status_code == 403


# ── résumé par enfant ───────────────────────────────────────────────────────

def test_children_summary_values(setup, headers_for):
    r = setup.get(URL, params={"child_ids": "10,11"}, headers=headers_for(5, "parent"))
    assert r.status_code == 200
    marie, paul = r.json()["children_summary"]
    assert marie == {"child_id": 10, "child_name": "Marie", "avg_score": 60.0,     # 12 sur 20 remis sur 100
                     "quiz_count": 2, "active_days": 2, "late_homework_count": 2, "quiz_gaps": ["Maths", "SVT"]}
    assert paul == {"child_id": 11, "child_name": "Paul", "avg_score": 0.0, "quiz_count": 0, "active_days": 0,
                    "late_homework_count": 0, "quiz_gaps": []}


def test_child_without_shared_context_gets_neutral_values(setup, headers_for, monkeypatch):
    monkeypatch.setattr(pe, "compute_child_context", lambda child_id, session=None: None)
    r = setup.get(URL, params={"child_ids": "10,11"}, headers=headers_for(5, "parent"))
    first = r.json()["children_summary"][0]
    assert (first["child_name"], first["avg_score"], first["late_homework_count"], first["quiz_gaps"]) == ("Enfant 10", 0.0, 0, [])


# ── observations du modèle ──────────────────────────────────────────────────

def test_model_observations_are_returned_and_capped_at_four(setup, headers_for):
    setup.fake.content = json.dumps([f"Observation {i}" for i in range(6)])
    r = setup.get(URL, params={"child_ids": "10,11"}, headers=headers_for(5, "parent"))
    assert r.json()["insights"] == [f"Observation {i}" for i in range(4)]
    call = setup.fake.calls[0]
    prompt = call["messages"][0]["content"]
    assert "Marie (score moy. 60.0%, 2 quiz, 2 jours actifs ce mois, 2 devoir(s) en retard, lacunes : Maths, SVT)" in prompt
    assert "Paul" in prompt and "lacunes : aucune" in prompt
    assert "Ne classe JAMAIS les enfants" in prompt and call["max_tokens"] == 200


def test_fenced_json_from_the_model_is_accepted(setup, headers_for):
    setup.fake.content = '```json\n["Une seule observation."]\n```'
    r = setup.get(URL, params={"child_ids": "10,11"}, headers=headers_for(5, "parent"))
    assert r.json()["insights"] == ["Une seule observation."]


@pytest.mark.parametrize("fake_args", [
    {"content": "pas du json"}, {"content": '{"a": 1}'}, {"error": RuntimeError("fournisseur indisponible")},
])
def test_unusable_model_answer_gives_the_deterministic_insights(setup, headers_for, fake_args):
    setup.fake.content, setup.fake.error = fake_args.get("content"), fake_args.get("error")
    r = setup.get(URL, params={"child_ids": "10,11"}, headers=headers_for(5, "parent"))
    assert r.status_code == 200
    insights = r.json()["insights"]
    # Marie a 2 quiz et 2 jours actifs, Paul 0 et 0 : Marie est la plus assidue ET la plus régulière
    assert insights[0].startswith("Marie est particulièrement assidu(e) avec 2 exercices ce mois")
    assert not any("très régulier(e)" in i for i in insights)
    assert any(i.startswith("Marie pourrait bénéficier") for i in insights)
    assert any(i.startswith("Paul pourrait bénéficier") for i in insights)
    assert len(insights) == 3


def test_fallback_names_a_second_child_when_the_most_regular_is_not_the_most_active(setup, headers_for, monkeypatch):
    # Paul : plus de jours actifs que Marie, mais moins de quiz
    setup.db.add(dbm.DailyScore(Id=10, UserId=11, Date=date(2026, 3, 1), AverageScore=50, CreatedAt=_ago(1)),
                 dbm.DailyScore(Id=11, UserId=11, Date=date(2026, 3, 2), AverageScore=50, CreatedAt=_ago(2)),
                 dbm.DailyScore(Id=12, UserId=11, Date=date(2026, 3, 3), AverageScore=50, CreatedAt=_ago(3)))
    setup.fake.content = "pas du json"
    insights = setup.get(URL, params={"child_ids": "10,11"}, headers=headers_for(5, "parent")).json()["insights"]
    assert any(i.startswith("Paul est très régulier(e) : 3 jours actifs") for i in insights)
