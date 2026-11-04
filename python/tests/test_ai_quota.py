"""Quota WinAI côté Python (services/ai_quota.py) : fenêtres glissantes session et semaine,
limite bloquante, réserve .NET rejouée, messages de refus, vérification fail-closed."""

from datetime import datetime, timedelta, timezone

import pytest

from services import ai_quota as aq

UTC = timezone.utc
T0 = datetime(2026, 1, 5, 10, 0, tzinfo=UTC)  # un lundi


# ── limites ─────────────────────────────────────────────────────────────────

def test_weekly_and_session_limits_follow_the_dotnet_ratios():
    assert aq.weekly_limit(200_000) == 50_000
    assert aq.session_limit(200_000) == 16_666


def test_limits_never_drop_below_one():
    assert aq.weekly_limit(0) == 1
    assert aq.session_limit(1) == 1
    assert aq.session_limit(0) == 1


# ── evaluate_window ─────────────────────────────────────────────────────────

def test_window_without_records_is_open():
    state = aq.evaluate_window([], T0, aq.SESSION_LENGTH, 100)
    assert (state.used, state.anchor_at, state.is_reached, state.resets_at) == (0, None, False, None)


def test_window_excludes_records_older_than_length_and_the_exact_boundary():
    records = [
        (T0 - aq.SESSION_LENGTH, 50),               # pile à la frontière : exclu (strictement supérieur)
        (T0 - aq.SESSION_LENGTH - timedelta(seconds=1), 50),
        (T0 - timedelta(hours=1), 30),
    ]
    state = aq.evaluate_window(records, T0, aq.SESSION_LENGTH, 100)
    assert state.used == 30
    assert state.anchor_at == T0 - timedelta(hours=1)


def test_window_anchor_is_earliest_record_and_negative_tokens_count_zero():
    records = [(T0 - timedelta(hours=1), 10), (T0 - timedelta(hours=3), 20), (T0 - timedelta(hours=2), -99)]
    state = aq.evaluate_window(records, T0, aq.SESSION_LENGTH, 100)
    assert state.used == 30
    assert state.anchor_at == T0 - timedelta(hours=3)


def test_window_reached_resets_when_first_records_expire():
    first, second = T0 - timedelta(hours=4), T0 - timedelta(hours=1)
    state = aq.evaluate_window([(second, 60), (first, 60)], T0, aq.SESSION_LENGTH, 100)
    assert state.is_reached and state.used == 120
    # après expiration du premier enregistrement il reste 60 < 100 : la limite se libère à first + 5 h
    assert state.resets_at == first + aq.SESSION_LENGTH


def test_window_reached_by_a_single_big_record():
    at = T0 - timedelta(hours=2)
    state = aq.evaluate_window([(at, 150)], T0, aq.SESSION_LENGTH, 100)
    assert state.is_reached and state.resets_at == at + aq.SESSION_LENGTH


def test_window_exactly_at_limit_is_reached():
    state = aq.evaluate_window([(T0 - timedelta(minutes=5), 100)], T0, aq.SESSION_LENGTH, 100)
    assert state.is_reached


# ── binding_limit ───────────────────────────────────────────────────────────

def _state(reached, resets=None):
    return aq.WindowState(limit=10, used=10 if reached else 0, anchor_at=None, is_reached=reached, resets_at=resets)


def test_binding_limit_none_when_nothing_reached():
    assert aq.binding_limit(_state(False), _state(False)) == (None, None)


def test_binding_limit_single_reached():
    r = T0 + timedelta(hours=2)
    assert aq.binding_limit(_state(True, r), _state(False)) == ("session", r)
    assert aq.binding_limit(_state(False), _state(True, r)) == ("week", r)


def test_binding_limit_both_reached_picks_the_latest_reset():
    soon, later = T0 + timedelta(hours=1), T0 + timedelta(days=3)
    assert aq.binding_limit(_state(True, soon), _state(True, later)) == ("week", later)
    assert aq.binding_limit(_state(True, later), _state(True, soon)) == ("session", later)


# ── classify_existing ───────────────────────────────────────────────────────

def test_classify_existing_cases():
    assert aq.classify_existing(False, False, None, T0) == "new"
    assert aq.classify_existing(True, True, T0, T0) == "consumed"
    assert aq.classify_existing(True, False, None, T0) == "consumed"
    assert aq.classify_existing(True, False, T0 - timedelta(minutes=14), T0) == "reattach"
    # pile à la limite de la fenêtre de rejeu : consommé
    assert aq.classify_existing(True, False, T0 - aq.REPLAY_WINDOW, T0) == "consumed"
    assert aq.classify_existing(True, False, T0 - timedelta(minutes=16), T0) == "consumed"


# ── messages ────────────────────────────────────────────────────────────────

def test_format_reset_time_uses_cameroon_offset():
    assert aq.format_reset_time(datetime(2026, 1, 5, 10, 30, tzinfo=UTC)) == "le lundi 5 janvier à 11h30"


def test_format_reset_time_rolls_over_midnight_and_converts_other_zones():
    assert aq.format_reset_time(datetime(2026, 1, 5, 23, 30, tzinfo=UTC)) == "le mardi 6 janvier à 00h30"
    plus_two = timezone(timedelta(hours=2))
    # 12h00 à UTC+2 = 10h00 UTC = 11h00 au Cameroun
    assert aq.format_reset_time(datetime(2026, 12, 25, 12, 0, tzinfo=plus_two)) == "le vendredi 25 décembre à 11h00"


def test_limit_message_without_reset_time():
    assert "5 heures glissantes" in aq.limit_message("session", None)
    assert "7 jours glissants" in aq.limit_message("week", None)


def test_limit_message_with_reset_time_never_mentions_token_counts():
    resets = datetime(2026, 1, 5, 10, 30, tzinfo=UTC)
    session = aq.limit_message("session", resets)
    week = aq.limit_message("week", resets)
    assert "limite de session" in session and "le lundi 5 janvier à 11h30" in session
    assert "limite hebdomadaire" in week and "le lundi 5 janvier à 11h30" in week
    assert "token" not in session.lower() and "token" not in week.lower()


# ── check_quota avec une session simulée ────────────────────────────────────

class _Result:
    def __init__(self, first=None, rows=()):
        self._first, self._rows = first, list(rows)

    def first(self):
        return self._first

    def all(self):
        return self._rows


class FakeSession:
    """Distingue les trois requêtes de check_quota par leur SQL."""

    def __init__(self, message_row=None, reference_row=None, usage_rows=()):
        self.message_row, self.reference_row, self.usage_rows = message_row, reference_row, usage_rows
        self.calls = []

    def execute(self, statement, params=None):
        sql = str(statement)
        self.calls.append((sql, params or {}))
        if '"IsFinalized"' in sql:
            return _Result(first=self.message_row)
        if '"ReferenceTokens"' in sql:
            return _Result(first=self.reference_row)
        if '"TokensCharged"' in sql:
            return _Result(rows=self.usage_rows)
        raise AssertionError("requête inattendue : " + sql)


@pytest.fixture
def frozen_now(monkeypatch):
    class FrozenDatetime(datetime):
        @classmethod
        def now(cls, tz=None):
            return T0 if tz is None else T0.astimezone(tz)

    monkeypatch.setattr(aq, "datetime", FrozenDatetime)
    return T0


def test_check_quota_reattaches_a_live_reservation(frozen_now):
    session = FakeSession(message_row=(False, T0 - timedelta(minutes=1)))
    result = aq.check_quota(session, 7, "msg-1")
    assert result.allowed is True and result.reason == "reserved"
    # la requête de fenêtre n'est même pas lancée
    assert len(session.calls) == 1


def test_check_quota_message_id_is_trimmed_and_truncated(frozen_now):
    session = FakeSession(message_row=(False, T0))
    aq.check_quota(session, 7, "  " + "x" * 100 + "  ")
    params = session.calls[0][1]
    assert params["uid"] == 7
    # strip() puis coupe à 64 caractères
    assert params["mid"] == "x" * 64


def test_check_quota_finalized_reservation_is_not_reusable(frozen_now):
    session = FakeSession(message_row=(True, T0 - timedelta(minutes=1)))
    result = aq.check_quota(session, 7, "msg-1")
    assert result.allowed is False and result.reason == "reservation_required"
    assert "API WinPlus" in result.message


def test_check_quota_without_message_id_requires_reservation(frozen_now):
    session = FakeSession()
    result = aq.check_quota(session, 7)
    assert (result.allowed, result.reason, result.limit) == (False, "reservation_required", None)
    # ni requête de réserve, seulement référence puis usage
    assert not any('"IsFinalized"' in sql for sql, _ in session.calls)


def test_check_quota_session_limit_reached(frozen_now):
    # référence 400 : semaine 100, session 33 ; 40 jetons il y a 1 h (date naïve : lue en UTC)
    at = (T0 - timedelta(hours=1)).replace(tzinfo=None)
    session = FakeSession(reference_row=(400,), usage_rows=[(at, 40)])
    result = aq.check_quota(session, 7)
    assert result.allowed is False and result.reason == "limit_reached" and result.limit == "session"
    assert result.resets_at == at.replace(tzinfo=UTC) + aq.SESSION_LENGTH
    assert "limite de session" in result.message


def test_check_quota_week_limit_wins_when_both_reached(frozen_now):
    at = T0 - timedelta(hours=1)
    session = FakeSession(reference_row=(400,), usage_rows=[(at, 120)])
    result = aq.check_quota(session, 7)
    assert result.limit == "week" and result.resets_at == at + aq.WEEK_LENGTH
    assert "limite hebdomadaire" in result.message


def test_check_quota_week_only_reached_when_usage_is_older_than_session(frozen_now):
    at = T0 - timedelta(days=2)
    session = FakeSession(reference_row=(400,), usage_rows=[(at, 100)])
    result = aq.check_quota(session, 7)
    assert result.limit == "week" and result.reason == "limit_reached"


def test_check_quota_uses_free_fallback_reference_when_unknown(frozen_now):
    # sans réserve connue : référence 200 000, session 16 666
    at = T0 - timedelta(minutes=10)
    session = FakeSession(reference_row=None, usage_rows=[(at, 16_666)])
    assert aq.check_quota(session, 7).limit == "session"
    under = FakeSession(reference_row=(0,), usage_rows=[(at, 16_665)])
    assert aq.check_quota(under, 7).reason == "reservation_required"


def test_reference_tokens_ignores_missing_or_zero_values():
    assert aq._reference_tokens(FakeSession(reference_row=None), 1) == aq.FREE_REFERENCE_FALLBACK
    assert aq._reference_tokens(FakeSession(reference_row=(0,)), 1) == aq.FREE_REFERENCE_FALLBACK
    assert aq._reference_tokens(FakeSession(reference_row=(None,)), 1) == aq.FREE_REFERENCE_FALLBACK
    assert aq._reference_tokens(FakeSession(reference_row=(1234,)), 1) == 1234


def test_check_quota_propagates_database_errors_for_fail_closed_callers(frozen_now):
    class Broken:
        def execute(self, *a, **k):
            raise RuntimeError("base inaccessible")

    with pytest.raises(RuntimeError):
        aq.check_quota(Broken(), 7)


def test_aware_helper():
    assert aq._aware(None) is None
    naive = datetime(2026, 1, 1, 0, 0)
    assert aq._aware(naive).tzinfo == UTC
    aware = datetime(2026, 1, 1, tzinfo=timezone(timedelta(hours=2)))
    assert aq._aware(aware) is aware
