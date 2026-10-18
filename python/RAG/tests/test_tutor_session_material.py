"""Lot 9.6 (suivi 17.5) : support de séance généré par WinAI.

- volume et budget de tokens proportionnés à la durée de la séance ;
- séance de groupe : adaptation au niveau du groupe ;
- sortie DeepSeek normalisée (quiz par la brique partagée avec
  /ai/generate-quiz-questions, exercices, cours) ;
- les styles d'apprentissage ne sont lus que pour les participants de la
  séance du tuteur connecté (403 sinon).
"""

import asyncio

import pytest
from fastapi import HTTPException


@pytest.fixture
def routes(monkeypatch):
    # auth.py refuse de s'importer sans secret : valeur factice pour le test.
    monkeypatch.setenv("JWT_SECRET_KEY", "test-only-not-a-secret")
    from routes import teacher_extra_routes

    return teacher_extra_routes


class _User:
    def __init__(self, user_id, role="teacher"):
        self.user_id = user_id
        self.role = role


class _Session:
    def close(self):
        pass


class _Db:
    def SessionLocal(self):
        return _Session()


def test_budget_grows_with_duration(routes):
    short = routes._session_material_budget(30)
    hour = routes._session_material_budget(60)
    long = routes._session_material_budget(180)
    assert (short["quiz"], short["exercises"]) == (5, 2)
    assert hour["quiz"] > short["quiz"] and long["quiz"] > hour["quiz"]
    assert short["max_tokens"] < hour["max_tokens"] < long["max_tokens"] <= 6000


def test_group_level_line_targets_most_frequent_level(routes):
    line = routes._group_level_line(["3e", "3e", "4e"], 3)
    assert "groupe" in line and "(3e)" in line and "4e" in line
    assert routes._group_level_line(["Terminale C"], 1) == "Niveau : Terminale C."
    assert "non renseigné" in routes._group_level_line([], 1)


def _fake_generation(routes, monkeypatch, participants, captured):
    monkeypatch.setattr(routes, "Database", _Db)
    monkeypatch.setattr(routes, "_load_session_participants", lambda s, b, u, a: participants)
    monkeypatch.setattr(routes, "_load_student_generation_signals", lambda uid: ("Style visuel.", ["Mathématiques"]))

    def fake_json(prompt, system, max_tokens=600):
        captured["prompt"] = prompt
        captured["max_tokens"] = max_tokens
        return {
            "title": "Équations du second degré",
            "objectives": ["Résoudre ax²+bx+c=0"],
            "course": {"introduction": "Intro", "sections": [{"title": "Discriminant", "content": "Δ = b² - 4ac"}],
                       "summary": "Résumé", "keyPoints": ["Δ"]},
            "quiz": [
                {"id": "q1", "text": "Signe de Δ si deux racines ?", "options": [{"id": "a", "text": "> 0"}, {"id": "b", "text": "< 0"}],
                 "correctOptionId": "a", "explanation": "Deux racines réelles distinctes."},
                {"id": "q2", "text": "mal formée"},
            ],
            "exercises": [{"id": "e1", "title": "Ex 1", "statement": "Résoudre x²-5x+6=0", "difficulty": "facile",
                           "hints": ["Calcule Δ"], "solution": "x=2 ou x=3"}],
            "groupNote": "Rappel des prérequis pour les 4e.",
        }

    monkeypatch.setattr(routes, "_deepseek_json", fake_json)


def test_group_session_material_is_structured(routes, monkeypatch):
    captured = {}
    _fake_generation(routes, monkeypatch, [{"id": 1, "level": "3e"}, {"id": 2, "level": "4e"}], captured)
    body = routes.SessionMaterialRequest(booking_id=7, subject="Mathématiques", topics=["Équations du second degré"],
                                         duration_minutes=60, participant_count=2)
    out = asyncio.run(routes.generate_session_material(body, current_user=_User(50)))

    assert out["title"] == "Équations du second degré"
    assert out["course"]["sections"][0]["title"] == "Discriminant"
    # Brique partagée : une question sans options passe ici, .NET l'écarte
    # ensuite (TutorSessionMaterialService.Sanitize : au moins 2 options).
    assert out["quiz"][0]["id"] == "q1" and out["quiz"][0]["correctOptionId"] == "a"
    assert out["quiz"][0]["options"][0] == {"id": "a", "text": "> 0"}
    assert out["exercises"][0]["solution"] == "x=2 ou x=3"
    assert out["groupNote"]  # séance de groupe
    assert "Séance de groupe (2 élèves)" in captured["prompt"]
    assert "Équations du second degré" in captured["prompt"]
    assert captured["max_tokens"] == routes._session_material_budget(60)["max_tokens"]


def test_single_session_has_no_group_note(routes, monkeypatch):
    captured = {}
    _fake_generation(routes, monkeypatch, [{"id": 1, "level": "Terminale"}], captured)
    body = routes.SessionMaterialRequest(booking_id=7, subject="Physique", topics=["Ondes"], duration_minutes=30)
    out = asyncio.run(routes.generate_session_material(body, current_user=_User(50)))
    assert out["groupNote"] is None
    assert "Style visuel." in captured["prompt"]


def test_refuses_without_topic(routes):
    body = routes.SessionMaterialRequest(booking_id=7)
    with pytest.raises(HTTPException) as exc:
        asyncio.run(routes.generate_session_material(body, current_user=_User(50)))
    assert exc.value.status_code == 422


def test_participants_only_for_the_booking_tutor(routes):
    class Row:
        def __init__(self, values):
            self._v = values

        def fetchone(self):
            return self._v

        def fetchall(self):
            return []

    class Session:
        def execute(self, *_args, **_kwargs):
            return Row((11, 99))  # élève 11, tuteur 99

    with pytest.raises(HTTPException) as exc:
        routes._load_session_participants(Session(), 7, tutor_user_id=50, is_admin=False)
    assert exc.value.status_code == 403
