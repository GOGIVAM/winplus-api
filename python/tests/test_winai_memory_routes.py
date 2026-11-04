"""Mémoire WinAI (routes/winai_memory_routes.py) : liste, suggestion de reprise, suppression, isolation par
utilisateur. Base SQLite en mémoire."""

from datetime import datetime, timezone

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

import database as dbm
import routes.winai_memory_routes as wm

UTC = timezone.utc


def _memory(id, user_id, kind, content, day):
    stamp = datetime(2026, 1, day, tzinfo=UTC)
    return dbm.UserAIMemory(Id=id, UserId=user_id, MemoryType=kind, Content=content, CreatedAt=stamp, UpdatedAt=stamp)


@pytest.fixture
def setup(sqlite_database):
    db = sqlite_database(wm, [dbm.UserAIMemory])
    app = FastAPI()
    app.include_router(wm.winai_memory_router)
    return TestClient(app), db


def test_both_routes_require_authentication(setup):
    client, _ = setup
    assert client.get("/winai/memoire").status_code in (401, 403)
    assert client.delete("/winai/memoire/1").status_code in (401, 403)


def test_empty_memory(setup, headers_for):
    client, _ = setup
    r = client.get("/winai/memoire", headers=headers_for(7))
    assert r.status_code == 200 and r.json() == {"memories": [], "continuationSuggestion": None}


def test_lists_only_own_memories_newest_first_with_labels(setup, headers_for):
    client, db = setup
    db.add(_memory(1, 7, "struggling_topics", "les dérivées", 1),
           _memory(2, 7, "learning_preference", "schémas", 3),
           _memory(3, 7, "type_inconnu", "autre", 2),
           _memory(4, 8, "exam_context", "BAC de quelqu'un d'autre", 5))
    body = client.get("/winai/memoire", headers=headers_for(7)).json()
    assert [m["id"] for m in body["memories"]] == [2, 3, 1]
    assert body["memories"][0] == {"id": 2, "type": "learning_preference", "typeLabel": "Préférence d'apprentissage",
                                   "content": "schémas", "updatedAt": "2026-01-03T00:00:00"}
    assert body["memories"][1]["typeLabel"] == "type_inconnu"
    assert all("quelqu'un d'autre" not in m["content"] for m in body["memories"])


def test_unfinished_topic_becomes_a_suggestion_and_is_hidden_from_the_list(setup, headers_for):
    client, db = setup
    db.add(_memory(1, 7, "unfinished_topic", "Les limites", 1), _memory(2, 7, "exam_context", "BEPC", 2))
    body = client.get("/winai/memoire", headers=headers_for(7)).json()
    assert [m["type"] for m in body["memories"]] == ["exam_context"]
    assert "On avait commencé « Les limites »" in body["continuationSuggestion"]
    assert body["continuationSuggestion"].endswith("tu veux continuer ?")


def test_most_recent_unfinished_topic_wins(setup, headers_for):
    client, db = setup
    db.add(_memory(1, 7, "unfinished_topic", "Ancien sujet", 1), _memory(2, 7, "unfinished_topic", "Nouveau sujet", 9))
    assert "Nouveau sujet" in client.get("/winai/memoire", headers=headers_for(7)).json()["continuationSuggestion"]


def test_delete_removes_one_precise_memory(setup, headers_for):
    client, db = setup
    db.add(_memory(1, 7, "exam_context", "a", 1), _memory(2, 7, "exam_context", "b", 2))
    r = client.delete("/winai/memoire/1", headers=headers_for(7))
    assert r.status_code == 200 and r.json() == {"success": True}
    assert [m.Id for m in db.all(dbm.UserAIMemory)] == [2]


def test_cannot_delete_someone_elses_memory(setup, headers_for):
    client, db = setup
    db.add(_memory(1, 8, "exam_context", "secret", 1))
    r = client.delete("/winai/memoire/1", headers=headers_for(7))
    assert r.status_code == 404 and r.json()["detail"] == "Mémoire introuvable."
    assert len(db.all(dbm.UserAIMemory)) == 1


def test_delete_unknown_memory_is_404_and_bad_id_is_422(setup, headers_for):
    client, _ = setup
    assert client.delete("/winai/memoire/999", headers=headers_for(7)).status_code == 404
    assert client.delete("/winai/memoire/abc", headers=headers_for(7)).status_code == 422
