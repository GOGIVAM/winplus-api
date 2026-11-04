"""Comptabilité des tokens WinAI (services/token_accounting.py) : estimation de repli,
lecture de l'usage fournisseur, boîte de comptage par requête HTTP."""

import pytest

from services import token_accounting as ta


@pytest.fixture(autouse=True)
def production_env(monkeypatch):
    # record_usage appelle winai_environment.add_usage : sans effet hors intégration
    monkeypatch.delenv("WINPLUS_ENV", raising=False)


# ── estimate_tokens ─────────────────────────────────────────────────────────

@pytest.mark.parametrize("text,expected", [
    (None, 0), ("", 0), ("a", 1), ("abcd", 1), ("abcde", 2), ("a" * 40, 10), ("a" * 41, 11),
])
def test_estimate_tokens_rounds_up_four_chars_per_token(text, expected):
    assert ta.estimate_tokens(text) == expected


# ── estimate_prompt_tokens ──────────────────────────────────────────────────

def test_prompt_tokens_without_anything_is_zero():
    assert ta.estimate_prompt_tokens(None) == 0
    assert ta.estimate_prompt_tokens([]) == 0


def test_prompt_tokens_adds_overhead_per_message_and_system_prompt():
    messages = [{"role": "user", "content": "abcdefgh"}, {"role": "assistant", "content": "abcd"}]
    # 2 + 4 (message 1) + 1 + 4 (message 2) + 1 (système)
    assert ta.estimate_prompt_tokens(messages, "abcd") == (2 + 4) + (1 + 4) + 1


def test_prompt_tokens_counts_multimodal_blocks_and_image_flat_rate():
    messages = [{"role": "user", "content": [
        {"type": "text", "text": "abcdefgh"},
        {"type": "image_url", "image_url": {"url": "data:..."}},
        {"type": "audio"},          # type inconnu ignoré
        "pas un dict",              # bloc invalide ignoré
    ]}]
    assert ta.estimate_prompt_tokens(messages) == ta.PER_MESSAGE_OVERHEAD + 2 + ta.IMAGE_TOKEN_ESTIMATE


def test_prompt_tokens_skips_non_dict_messages_and_unsupported_content():
    assert ta.estimate_prompt_tokens(["texte", {"role": "user", "content": 42}]) == ta.PER_MESSAGE_OVERHEAD


# ── usage_total ─────────────────────────────────────────────────────────────

def test_usage_total_sums_prompt_and_completion():
    assert ta.usage_total({"prompt_tokens": 10, "completion_tokens": 5, "total_tokens": 999}) == 15


def test_usage_total_with_only_one_side():
    assert ta.usage_total({"completion_tokens": 7}) == 7
    assert ta.usage_total({"prompt_tokens": 3}) == 3


def test_usage_total_falls_back_to_total_tokens():
    assert ta.usage_total({"total_tokens": 42}) == 42
    assert ta.usage_total({"total_tokens": 42.0}) == 42


@pytest.mark.parametrize("usage", [None, {}, {"total_tokens": "12"}, {"prompt_tokens": None}])
def test_usage_total_unusable_is_zero(usage):
    assert ta.usage_total(usage) == 0


# ── boîte de comptage de la requête ─────────────────────────────────────────

def test_request_usage_accumulates_positive_values_only():
    box, token = ta.begin_request_usage()
    try:
        assert ta.current_request_usage() == 0
        ta.record_usage(10)
        ta.record_usage(0)
        ta.record_usage(-5)
        ta.record_usage(None)
        ta.record_usage(5)
        assert ta.current_request_usage() == 15
        assert box == [15]
    finally:
        ta.end_request_usage(token)
    assert ta.current_request_usage() == 0


def test_record_usage_without_box_is_silent():
    assert ta.current_request_usage() == 0
    ta.record_usage(100)
    assert ta.current_request_usage() == 0


def test_end_request_usage_with_foreign_token_does_not_raise():
    import contextvars
    other = contextvars.ContextVar("x", default=None)
    token = other.set(1)
    # un jeton issu d'une autre variable lève ValueError dans reset : avalé par end_request_usage
    ta.end_request_usage(token)


def test_header_name_is_the_one_read_by_dotnet():
    assert ta.WINAI_TOKENS_HEADER == "X-WinAI-Tokens"
