"""Aides des routes IA intelligentes et de modération (routes/smart_ai_routes.py, routes/admin_routes.py) :
plafond quotidien de notifications, cache de 24 h, verdicts de modération. Horloge simulée."""

from datetime import datetime
from types import SimpleNamespace

import pytest

import routes.admin_routes as ar
import routes.smart_ai_routes as sa


@pytest.fixture(autouse=True)
def clean_state():
    sa._notif_cache.clear()
    sa._notif_daily.clear()
    yield
    sa._notif_cache.clear()
    sa._notif_daily.clear()


class FrozenDay(datetime):
    today = datetime(2026, 3, 15, 10, 0)

    @classmethod
    def utcnow(cls):
        return cls.today


@pytest.fixture
def day(monkeypatch):
    FrozenDay.today = datetime(2026, 3, 15, 10, 0)
    monkeypatch.setattr(sa, "datetime", FrozenDay)
    return FrozenDay


# ── plafond quotidien ───────────────────────────────────────────────────────

def test_constants():
    assert sa._RATE_LIMIT == 3 and sa._CACHE_TTL == 86_400


def test_three_personalised_notifications_per_user_per_day(day):
    assert [sa._rate_ok(1) for _ in range(5)] == [True, True, True, False, False]
    assert sa._rate_ok(2) is True       # chaque utilisateur a son compteur


def test_the_counter_resets_the_next_day(day):
    for _ in range(3):
        sa._rate_ok(1)
    assert sa._rate_ok(1) is False
    day.today = datetime(2026, 3, 16, 0, 5)
    assert sa._rate_ok(1) is True
    assert sa._notif_daily[1] == (1, "2026-03-16")


# ── cache ───────────────────────────────────────────────────────────────────

def test_cache_returns_the_value_within_24_hours(monkeypatch):
    clock = SimpleNamespace(now=1_000.0)
    monkeypatch.setattr(sa, "time", SimpleNamespace(time=lambda: clock.now))
    key = (7, "quiz_ready", "abc")
    assert sa._cached(key) is None
    sa._set_cache(key, {"title": "Quiz prêt"})
    clock.now += 86_399
    assert sa._cached(key) == {"title": "Quiz prêt"}
    clock.now += 2
    assert sa._cached(key) is None


def test_cache_keys_are_independent(monkeypatch):
    monkeypatch.setattr(sa, "time", SimpleNamespace(time=lambda: 5.0))
    sa._set_cache((1, "a", "h"), {"n": 1})
    assert sa._cached((1, "a", "other")) is None
    assert sa._cached((2, "a", "h")) is None
    assert sa._cached((1, "a", "h")) == {"n": 1}


# ── schémas ─────────────────────────────────────────────────────────────────

def test_request_schemas_defaults():
    assert sa.GenerateNotificationRequest(user_id=1, notification_type="quiz_ready").context_data == {}
    assert sa.SummarizeNotificationsRequest(notification_texts=["a"]).notification_ids == []
    assert sa.QuizContentRequest(user_id=1).recent_question_texts == []
    r = sa.RevisionContentRequest(user_id=1)
    assert (r.subject, r.topic, r.level, r.context_hint, r.difficulty) == (None,) * 5


# ── verdicts de modération ──────────────────────────────────────────────────

@pytest.mark.parametrize("verdict,confidence,expected", [
    ("safe", 0.99, "publish"),
    ("safe", 0.0, "publish"),
    ("spam", 0.8, "hold_for_review"),         # seuil atteint
    ("spam", 0.79, "publish"),                # sous le seuil
    ("inappropriate", 0.95, "hold_for_review"),
    ("inappropriate", 0.1, "publish"),
    ("off_topic", 0.1, "publish_with_flag"),
    ("needs_review", 0.99, "publish_with_flag"),
    ("inconnu", 1.0, "publish"),
])
def test_verdict_to_action_with_default_threshold(verdict, confidence, expected):
    assert ar._verdict_to_action(verdict, confidence, 0.8) == expected


def test_verdict_threshold_is_configurable():
    assert ar._verdict_to_action("spam", 0.5, 0.4) == "hold_for_review"
    assert ar._verdict_to_action("spam", 0.5, 0.9) == "publish"


def test_moderate_message_request_default_threshold():
    assert ar.ModerateMessageRequest(content="x", author_id=1).confidence_threshold == 0.8
