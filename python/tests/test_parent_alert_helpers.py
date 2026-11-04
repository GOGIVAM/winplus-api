"""Alertes parent (routes/parent_alert_routes.py) : contrôle d'accès à l'enfant, vocabulaire interdit, messages
de repli, message WinAI, persistance dédupliquée sur 24 h. Base SQLite en mémoire, modèle d'IA simulé."""

from datetime import datetime, timedelta, timezone

import pytest
from fastapi import HTTPException

import database as dbm
import routes.parent_alert_routes as pa
from auth import UserTokenData

UTC = timezone.utc


def _user(user_id, role="parent"):
    return UserTokenData(user_id=user_id, email="p@example.test", role=role)


@pytest.fixture
def db(sqlite_database):
    return sqlite_database(pa, [dbm.ParentStudentLink, dbm.ParentAlertDB])


# ── accès à l'enfant ────────────────────────────────────────────────────────

def test_a_child_may_read_its_own_alerts(db):
    session = db.SessionLocal()
    pa._assert_can_access_child(session, _user(20, "student"), 20)
    session.close()


def test_a_linked_parent_may_read_the_childs_alerts(db):
    db.add(dbm.ParentStudentLink(Id=1, ParentId=5, StudentId=20, Status="accepted"))
    session = db.SessionLocal()
    pa._assert_can_access_child(session, _user(5), 20)
    session.close()


@pytest.mark.parametrize("link", [
    dict(ParentId=5, StudentId=20, Status="pending"),
    dict(ParentId=6, StudentId=20, Status="accepted"),
    dict(ParentId=5, StudentId=21, Status="accepted"),
    None,
])
def test_everyone_else_gets_403(db, link):
    if link:
        db.add(dbm.ParentStudentLink(Id=1, **link))
    session = db.SessionLocal()
    with pytest.raises(HTTPException) as info:
        pa._assert_can_access_child(session, _user(5), 20)
    session.close()
    assert info.value.status_code == 403 and "n'est pas lié" in info.value.detail


# ── vocabulaire interdit ────────────────────────────────────────────────────

@pytest.mark.parametrize("text", ["Il souffre de DÉPRESSION", "un trouble de l'attention", "signe de burnout",
                                  "un diagnostic précis", "anxiété diagnostiquée"])
def test_clinical_vocabulary_is_detected_ignoring_case(text):
    assert pa._contains_forbidden_vocabulary(text) is True


def test_neutral_wording_is_allowed():
    assert pa._contains_forbidden_vocabulary("Un changement de rythme à surveiller") is False
    assert pa._contains_forbidden_vocabulary("") is False


def test_fallback_messages_never_use_forbidden_vocabulary():
    for alert_type in list(pa._ALERT_TYPE_TO_DB) + ["inconnu"]:
        message = pa._fallback_message(alert_type, "Marie")
        assert "Marie" in message
        assert not pa._contains_forbidden_vocabulary(message)


def test_fallback_message_per_type_and_default():
    assert "excellente semaine" in pa._fallback_message("excellent_week", "Paul")
    assert "n'a pas étudié" in pa._fallback_message("inactivity", "Paul")
    assert pa._fallback_message("inconnu", "Paul") == "WinAI a détecté quelque chose à surveiller pour Paul."


# ── message WinAI ───────────────────────────────────────────────────────────

class FakeDeepSeek:
    def __init__(self, content="", error=None):
        self.content, self.error, self.calls = content, error, []

    def chat(self, messages, system_prompt=None, max_tokens=None, temperature=None, json_mode=False):
        self.calls.append({"messages": messages, "system": system_prompt, "max_tokens": max_tokens,
                           "temperature": temperature})
        if self.error:
            raise self.error
        return {"content": self.content}


def test_winai_message_returns_the_model_text_and_states_the_rules():
    ds = FakeDeepSeek("  Marie a un peu ralenti, un mot d'encouragement l'aiderait.  ")
    text = pa._winai_message("performance_drop", "Marie", 12.4, ds)
    assert text == "Marie a un peu ralenti, un mot d'encouragement l'aiderait."
    call = ds.calls[0]
    assert "baisser de 12%" in call["messages"][0]["content"]
    assert call["max_tokens"] == 120 and call["temperature"] == 0.7
    assert "JAMAIS de vocabulaire clinique" in call["system"] and "en français" in call["system"]


def test_winai_message_with_clinical_wording_uses_the_fallback():
    ds = FakeDeepSeek("Marie fait peut-être une dépression.")
    assert pa._winai_message("inactivity", "Marie", 5, ds) == pa._fallback_message("inactivity", "Marie")


@pytest.mark.parametrize("ds", [FakeDeepSeek(""), FakeDeepSeek("   "), FakeDeepSeek(error=RuntimeError("panne"))])
def test_winai_message_falls_back_on_empty_answer_or_failure(ds):
    assert pa._winai_message("surmenage", "Marie", 9, ds) == pa._fallback_message("surmenage", "Marie")


def test_winai_message_prompt_carries_the_statistic():
    ds = FakeDeepSeek("ok")
    pa._winai_message("inactivity", "Paul", 6, ds)
    assert "depuis 6 jours" in ds.calls[0]["messages"][0]["content"]
    pa._winai_message("excellent_week", "Paul", 91.0, ds)
    assert "score moyen 91%" in ds.calls[1]["messages"][0]["content"]
    pa._winai_message("exam_anxiety", "Paul", 4, ds)
    assert "4 séances" in ds.calls[2]["messages"][0]["content"]


# ── persistance ─────────────────────────────────────────────────────────────

def _alert(kind="performance_drop", severity="error", message="Baisse observée"):
    return {"type": kind, "severity": severity, "message": message}


def _rows(db):
    return sorted(db.all(dbm.ParentAlertDB), key=lambda r: r.Id)


def test_alerts_are_written_with_database_vocabulary(db):
    session = db.SessionLocal()
    pa._persist_alerts(session, 5, 20, [_alert(), _alert("inactivity", "warn", "Inactif"),
                                        _alert("excellent_week", "success", "Bravo"), _alert("exam_anxiety", "info", "Tard"),
                                        _alert("surmenage", "autre", "Pause")])
    session.close()
    rows = _rows(db)
    assert [(r.Type, r.Severity, r.Content) for r in rows] == [
        ("BaissePerformance", "High", "Baisse observée"), ("Inactivite", "Medium", "Inactif"),
        ("Felicitations", "Low", "Bravo"), ("AnxieteExamen", "Low", "Tard"), ("Surmenage", "Low", "Pause")]
    assert all(r.ParentId == 5 and r.ChildId == 20 for r in rows)


def test_unknown_alert_types_are_not_persisted(db):
    session = db.SessionLocal()
    pa._persist_alerts(session, 5, 20, [_alert("type_inconnu")])
    session.close()
    assert _rows(db) == []


def test_same_alert_within_24_hours_is_not_duplicated(db):
    for _ in range(2):
        session = db.SessionLocal()
        pa._persist_alerts(session, 5, 20, [_alert()])
        session.close()
    assert len(_rows(db)) == 1


def test_same_alert_for_another_child_or_parent_is_kept(db):
    session = db.SessionLocal()
    pa._persist_alerts(session, 5, 20, [_alert()])
    pa._persist_alerts(session, 5, 21, [_alert()])
    pa._persist_alerts(session, 6, 20, [_alert()])
    session.close()
    assert len(_rows(db)) == 3


def test_an_older_alert_does_not_block_a_new_one(db):
    old = datetime.now(UTC) - timedelta(days=2)
    db.add(dbm.ParentAlertDB(Id=1, ParentId=5, ChildId=20, Type="BaissePerformance", Content="ancien",
                             DetectedAt=old, CreatedAt=old))
    session = db.SessionLocal()
    pa._persist_alerts(session, 5, 20, [_alert()])
    session.close()
    assert [r.Content for r in _rows(db)] == ["ancien", "Baisse observée"]


def test_persistence_failure_is_absorbed_and_rolled_back():
    class Broken:
        rolled_back = False

        def query(self, *a, **k):
            raise RuntimeError("base cassée")

        def rollback(self):
            self.rolled_back = True

    session = Broken()
    pa._persist_alerts(session, 5, 20, [_alert()])
    assert session.rolled_back is True
