"""Client DeepSeek (services/deepseek_client.py) : requête construite, usage réel ou estimé,
reprises et délais, mode simulé, flux SSE. Aucun appel réseau : requests.post est simulé."""

import copy
import json

import pytest
import requests

from services import deepseek_client as dc
from services import token_accounting as ta


class FakeResponse:
    def __init__(self, status=200, payload=None, lines=None, text=""):
        self.status_code = status
        self._payload = payload if payload is not None else {}
        self._lines = lines or []
        self.text = text

    def raise_for_status(self):
        if self.status_code >= 400:
            raise requests.exceptions.HTTPError(f"{self.status_code} error")

    def json(self):
        return self._payload

    def iter_lines(self):
        return iter(self._lines)


def _ok(content="Bonjour", usage=None, finish="stop", model="WinAI-chat"):
    payload = {"choices": [{"message": {"content": content}, "finish_reason": finish}], "model": model}
    if usage is not None:
        payload["usage"] = usage
    return FakeResponse(200, payload)


class Recorder:
    """Remplace requests.post : renvoie les réponses (ou lève les exceptions) dans l'ordre."""

    def __init__(self, *outcomes):
        self.outcomes = list(outcomes)
        self.calls = []

    def __call__(self, url, json=None, headers=None, timeout=None, stream=False):
        self.calls.append({"url": url, "json": copy.deepcopy(json), "headers": headers, "timeout": timeout, "stream": stream})
        outcome = self.outcomes.pop(0) if len(self.outcomes) > 1 else self.outcomes[0]
        if isinstance(outcome, Exception):
            raise outcome
        return outcome


@pytest.fixture
def client(monkeypatch):
    monkeypatch.delenv("WINPLUS_ENV", raising=False)
    monkeypatch.delenv("WINAI_SIMULATED", raising=False)
    monkeypatch.setenv("DEEPSEEK_BASE_URL", "http://llm.test")
    monkeypatch.setenv("DEEPSEEK_API_KEY", "cle-de-test")
    monkeypatch.setenv("DEEPSEEK_MODEL", "modele-test")
    monkeypatch.setenv("DEEPSEEK_TIMEOUT", "11")
    monkeypatch.setenv("DEEPSEEK_MAX_TOKENS", "2500")
    monkeypatch.setenv("DEEPSEEK_MIN_TOKENS", "2000")
    monkeypatch.setenv("DEEPSEEK_TEMPERATURE", "0.5")
    sleeps = []
    monkeypatch.setattr(dc.time, "sleep", lambda s: sleeps.append(s))
    c = dc.DeepSeekClient()
    c.sleeps = sleeps
    return c


def _use(monkeypatch, *outcomes):
    rec = Recorder(*outcomes)
    monkeypatch.setattr(dc.requests, "post", rec)
    return rec


# ── configuration ───────────────────────────────────────────────────────────

def test_client_reads_its_configuration_from_the_environment(client):
    assert (client.base_url, client.api_key, client.model) == ("http://llm.test", "cle-de-test", "modele-test")
    assert (client.timeout, client.max_tokens, client.min_tokens, client.temperature) == (11, 2500, 2000, 0.5)
    assert client.max_retries == 3


def test_get_deepseek_client_is_a_singleton(monkeypatch):
    monkeypatch.setattr(dc, "_client", None)
    first = dc.get_deepseek_client()
    assert dc.get_deepseek_client() is first


# ── chat : succès ───────────────────────────────────────────────────────────

def test_chat_builds_the_request_and_reads_real_usage(client, monkeypatch):
    rec = _use(monkeypatch, _ok("Salut", usage={"prompt_tokens": 12, "completion_tokens": 8}))
    result = client.chat([{"role": "user", "content": "Hello"}], system_prompt="Sois bref")
    assert result["success"] is True and result["content"] == "Salut"
    assert (result["tokens_used"], result["prompt_tokens"], result["completion_tokens"]) == (20, 12, 8)
    assert result["usage_estimated"] is False and result["finish_reason"] == "stop"

    call = rec.calls[0]
    assert call["url"] == "http://llm.test/v1/chat/completions"
    assert call["timeout"] == 11 and call["stream"] is False
    assert call["headers"]["Authorization"] == "Bearer cle-de-test"
    body = call["json"]
    assert body["messages"][0] == {"role": "system", "content": "Sois bref"}
    assert body["messages"][1] == {"role": "user", "content": "Hello"}
    assert body["model"] == "modele-test" and body["temperature"] == 0.5 and body["stream"] is False
    assert "response_format" not in body


def test_chat_applies_the_token_floor_and_explicit_overrides(client, monkeypatch):
    rec = _use(monkeypatch, _ok())
    client.chat([{"role": "user", "content": "x"}], max_tokens=300, temperature=0)
    body = rec.calls[0]["json"]
    # plancher de 2000 : 300 demandés ne suffisent pas
    assert body["max_tokens"] == 2000
    # une température de 0 est respectée (pas remplacée par le défaut)
    assert body["temperature"] == 0
    client.chat([{"role": "user", "content": "x"}], max_tokens=3000)
    assert rec.calls[1]["json"]["max_tokens"] == 3000


def test_chat_json_mode_requests_a_json_object(client, monkeypatch):
    rec = _use(monkeypatch, _ok('{"a":1}'))
    client.chat([{"role": "user", "content": "x"}], json_mode=True)
    assert rec.calls[0]["json"]["response_format"] == {"type": "json_object"}


def test_chat_without_api_key_sends_no_authorization(client, monkeypatch):
    client.api_key = ""
    rec = _use(monkeypatch, _ok())
    client.chat([{"role": "user", "content": "x"}])
    assert "Authorization" not in rec.calls[0]["headers"]


def test_chat_estimates_usage_when_provider_omits_it(client, monkeypatch):
    _use(monkeypatch, _ok("abcdefgh"))  # pas d'usage
    messages = [{"role": "user", "content": "abcd"}]
    result = client.chat(messages, system_prompt="abcd")
    expected_prompt = ta.estimate_prompt_tokens([{"role": "system", "content": "abcd"}] + messages)
    assert result["usage_estimated"] is True
    assert result["prompt_tokens"] == expected_prompt
    assert result["completion_tokens"] == 2
    assert result["tokens_used"] == expected_prompt + 2


def test_chat_charges_the_request_box(client, monkeypatch):
    _use(monkeypatch, _ok(usage={"prompt_tokens": 5, "completion_tokens": 5}))
    box, token = ta.begin_request_usage()
    try:
        client.chat([{"role": "user", "content": "x"}])
        assert box == [10]
    finally:
        ta.end_request_usage(token)


def test_chat_reports_truncation_finish_reason(client, monkeypatch):
    _use(monkeypatch, _ok('{"a":', usage={"total_tokens": 9}, finish="length"))
    result = client.chat([{"role": "user", "content": "x"}])
    assert result["success"] is True and result["finish_reason"] == "length" and result["tokens_used"] == 9


# ── chat : erreurs et reprises ──────────────────────────────────────────────

def test_chat_does_not_retry_on_client_error(client, monkeypatch):
    rec = _use(monkeypatch, FakeResponse(402, text="payment"))
    result = client.chat([{"role": "user", "content": "x"}])
    assert result["success"] is False and result["error"] == "HTTP error: 402"
    assert result["tokens_used"] == 0 and "ne peux pas répondre" in result["content"]
    assert len(rec.calls) == 1 and client.sleeps == []


def test_chat_retries_server_errors_with_exponential_backoff(client, monkeypatch):
    rec = _use(monkeypatch, FakeResponse(503, text="down"))
    result = client.chat([{"role": "user", "content": "x"}])
    assert result["success"] is False and result["error"] == "HTTP error: 503"
    assert len(rec.calls) == 3
    assert client.sleeps == [1, 2]  # pas d'attente après la dernière tentative


def test_chat_recovers_after_a_timeout(client, monkeypatch):
    rec = _use(monkeypatch, requests.exceptions.Timeout(), _ok("ok", usage={"total_tokens": 3}))
    result = client.chat([{"role": "user", "content": "x"}])
    assert result["success"] is True and result["content"] == "ok"
    assert len(rec.calls) == 2 and client.sleeps == [1]


def test_chat_gives_up_after_repeated_timeouts(client, monkeypatch):
    rec = _use(monkeypatch, requests.exceptions.Timeout())
    result = client.chat([{"role": "user", "content": "x"}])
    assert result["success"] is False and result["error"] == "Request timeout"
    assert len(rec.calls) == 3


def test_chat_reports_connection_errors(client, monkeypatch):
    _use(monkeypatch, requests.exceptions.ConnectionError("refusé"))
    result = client.chat([{"role": "user", "content": "x"}])
    assert result["error"].startswith("Connection error: ") and "refusé" in result["error"]


def test_chat_unexpected_error_is_returned_not_raised(client, monkeypatch):
    _use(monkeypatch, ValueError("réponse illisible"))
    result = client.chat([{"role": "user", "content": "x"}])
    assert result["success"] is False and result["error"] == "réponse illisible"


# ── environnement d'intégration ─────────────────────────────────────────────

def test_chat_simulated_never_calls_the_provider(client, monkeypatch):
    monkeypatch.setenv("WINPLUS_ENV", "integration")
    monkeypatch.setenv("WINAI_SIMULATED", "true")
    rec = _use(monkeypatch, _ok())
    result = client.chat([{"role": "user", "content": "x"}])
    assert rec.calls == []
    assert result["simulated"] is True and result["tokens_used"] == 0
    assert result["content"].startswith("[WinAI simulé]")


def test_chat_json_mode_simulated_returns_valid_json(client, monkeypatch):
    monkeypatch.setenv("WINPLUS_ENV", "integration")
    monkeypatch.setenv("WINAI_SIMULATED", "true")
    _use(monkeypatch, _ok())
    data = json.loads(client.chat([{"role": "user", "content": "x"}], json_mode=True)["content"])
    assert data["simulated"] is True


# ── chat_stream ─────────────────────────────────────────────────────────────

def _sse(obj):
    return b"data: " + json.dumps(obj).encode()


def _events(chunks):
    out = []
    for chunk in chunks:
        assert chunk.startswith("data: ") and chunk.endswith("\n\n")
        body = chunk[6:-2]
        out.append(body if body == "[DONE]" else json.loads(body))
    return out


def test_stream_yields_deltas_usage_and_done(client, monkeypatch):
    lines = [
        _sse({"choices": [{"delta": {"content": "Bon"}}]}),
        b"",
        b": commentaire ignore",
        _sse({"choices": [{"delta": {"content": "jour"}, "finish_reason": "stop"}]}),
        _sse({"choices": [], "usage": {"prompt_tokens": 10, "completion_tokens": 5}}),
        b"data: [DONE]",
    ]
    rec = _use(monkeypatch, FakeResponse(200, lines=lines))
    events = _events(client.chat_stream([{"role": "user", "content": "x"}], emit_usage=True))
    assert events == [
        {"delta": "Bon"},
        {"delta": "jour"},
        {"usage_final": True, "tokens_used": 15, "prompt_tokens": 10, "completion_tokens": 5, "usage_estimated": False},
        "[DONE]",
    ]
    body = rec.calls[0]["json"]
    assert body["stream"] is True and body["stream_options"] == {"include_usage": True}
    assert rec.calls[0]["stream"] is True


def test_stream_without_emit_usage_has_no_usage_event(client, monkeypatch):
    lines = [_sse({"choices": [{"delta": {"content": "A"}}]}), b"data: [DONE]"]
    _use(monkeypatch, FakeResponse(200, lines=lines))
    assert _events(client.chat_stream([{"role": "user", "content": "x"}])) == [{"delta": "A"}, "[DONE]"]


def test_stream_estimates_usage_when_provider_sends_none(client, monkeypatch):
    lines = [_sse({"choices": [{"delta": {"content": "abcdefgh"}}]}), b"data: [DONE]"]
    _use(monkeypatch, FakeResponse(200, lines=lines))
    messages = [{"role": "user", "content": "abcd"}]
    events = _events(client.chat_stream(messages, system_prompt="abcd", emit_usage=True))
    usage = events[-2]
    expected_prompt = ta.estimate_prompt_tokens([{"role": "system", "content": "abcd"}] + messages)
    assert usage["usage_estimated"] is True
    assert usage["prompt_tokens"] == expected_prompt and usage["completion_tokens"] == 2
    assert usage["tokens_used"] == expected_prompt + 2


def test_stream_retries_without_stream_options_on_400(client, monkeypatch):
    lines = [_sse({"choices": [{"delta": {"content": "ok"}}]}), b"data: [DONE]"]
    rec = _use(monkeypatch, FakeResponse(400), FakeResponse(200, lines=lines))
    events = _events(client.chat_stream([{"role": "user", "content": "x"}]))
    assert events[0] == {"delta": "ok"}
    assert len(rec.calls) == 2
    assert rec.calls[0]["json"]["stream_options"] == {"include_usage": True}
    assert "stream_options" not in rec.calls[1]["json"]


def test_stream_ignores_malformed_json_lines(client, monkeypatch):
    lines = [b"data: {pas du json", _sse({"choices": [{"delta": {"content": "x"}}]}), b"data: [DONE]"]
    _use(monkeypatch, FakeResponse(200, lines=lines))
    assert _events(client.chat_stream([{"role": "user", "content": "q"}])) == [{"delta": "x"}, "[DONE]"]


def test_stream_empty_answer_sends_fallback_message(client, monkeypatch):
    _use(monkeypatch, FakeResponse(200, lines=[_sse({"choices": [{"delta": {}, "finish_reason": "length"}]}),
                                               b"data: [DONE]"]))
    events = _events(client.chat_stream([{"role": "user", "content": "x"}]))
    assert "Je n'ai pas réussi à terminer ma réponse" in events[0]["delta"]
    assert events[-1] == "[DONE]"


def test_stream_connection_failure_emits_error_then_fallback_without_usage(client, monkeypatch):
    _use(monkeypatch, requests.exceptions.ConnectionError("coupé"))
    events = _events(client.chat_stream([{"role": "user", "content": "x"}], emit_usage=True))
    assert events[0] == {"error": "Stream error"}
    assert "delta" in events[1]
    # rien généré et usage estimé : aucun événement usage_final
    assert not any(isinstance(e, dict) and e.get("usage_final") for e in events)
    assert events[-1] == "[DONE]"


def test_stream_simulated_never_calls_the_provider(client, monkeypatch):
    monkeypatch.setenv("WINPLUS_ENV", "integration")
    monkeypatch.setenv("WINAI_SIMULATED", "true")
    rec = _use(monkeypatch, FakeResponse(200))
    events = _events(client.chat_stream([{"role": "user", "content": "x"}]))
    assert rec.calls == []
    assert events[0]["delta"].startswith("[WinAI simulé]") and events[-1] == "[DONE]"


# ── health_check ────────────────────────────────────────────────────────────

def test_health_check_states(client, monkeypatch):
    monkeypatch.setattr(dc.requests, "get", lambda url, timeout=None: FakeResponse(200))
    assert client.health_check()["status"] == "healthy"
    monkeypatch.setattr(dc.requests, "get", lambda url, timeout=None: FakeResponse(503))
    unhealthy = client.health_check()
    assert unhealthy["status"] == "unhealthy" and "503" in unhealthy["error"]

    def boom(url, timeout=None):
        raise requests.exceptions.ConnectionError("injoignable")

    monkeypatch.setattr(dc.requests, "get", boom)
    unreachable = client.health_check()
    assert unreachable["status"] == "unreachable" and "injoignable" in unreachable["error"]
