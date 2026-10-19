"""Lot 10.6 : langue des réponses WinAI hors chat (services/language.py)."""

import asyncio

from services.language import (
    begin_request_language,
    end_request_language,
    in_language,
    is_english,
    language_directive,
    normalize_language,
    resolve_language,
)


def test_normalize_language():
    assert normalize_language("en") == "en"
    assert normalize_language(" EN-gb ") == "en"
    assert normalize_language("en_US") == "en"
    assert normalize_language("fr-FR") == "fr"
    assert normalize_language("de") == "fr"
    assert normalize_language(None) == "fr"
    assert normalize_language(12) == "fr"


def test_french_prompts_unchanged_without_header():
    assert resolve_language() == "fr"
    assert in_language() == "en français"
    assert language_directive() == ""


def test_header_sets_language_for_the_request_only():
    token = begin_request_language("en")
    try:
        assert resolve_language() == "en"
        assert is_english()
        assert in_language() == "en anglais"
        assert "in English" in language_directive()
    finally:
        end_request_language(token)
    assert resolve_language() == "fr"


def test_explicit_value_wins_over_header():
    token = begin_request_language("en")
    try:
        assert resolve_language("fr") == "fr"
        assert resolve_language("xx") == "en"
    finally:
        end_request_language(token)


def test_unknown_header_falls_back_to_french():
    token = begin_request_language("es")
    try:
        assert resolve_language() == "fr"
    finally:
        end_request_language(token)


def test_concurrent_requests_do_not_leak():
    async def handle(header, out):
        token = begin_request_language(header)
        try:
            await asyncio.sleep(0.01)
            out.append((header, resolve_language()))
        finally:
            end_request_language(token)

    async def main():
        out = []
        await asyncio.gather(handle("en", out), handle(None, out), handle("fr", out))
        return out

    results = dict(asyncio.run(main()))
    assert results == {"en": "en", None: "fr", "fr": "fr"}
