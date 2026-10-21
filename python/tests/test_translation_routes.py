"""Traduction des contenus de la base : route POST /api/winai/translate-batch."""

import json
import os
import time

os.environ.setdefault("JWT_SECRET_KEY", "test-secret-key-for-translation-tests-0123456789")

import jwt  # noqa: E402
import pytest  # noqa: E402
from fastapi import FastAPI  # noqa: E402
from fastapi.testclient import TestClient  # noqa: E402

import routes.translation_routes as tr  # noqa: E402
from auth import JWT_ISSUER, JWT_SECRET, SERVICE_AUDIENCE  # noqa: E402


def _token(scope="ai.content-translation", audience=SERVICE_AUDIENCE, token_use="service"):
    now = int(time.time())
    payload = {"sub": "service:winplus-dotnet", "token_use": token_use, "scope": scope,
               "iss": JWT_ISSUER, "aud": audience, "iat": now, "nbf": now, "exp": now + 300}
    return "Bearer " + jwt.encode(payload, JWT_SECRET, algorithm="HS256")


class FakeDeepSeek:
    def __init__(self, translate=None, success=True):
        self.calls = []
        self.translate = translate or (lambda text: "EN:" + text)
        self.success = success

    def chat(self, messages, system_prompt=None, max_tokens=None, temperature=None, json_mode=False):
        self.calls.append({"messages": messages, "system": system_prompt, "json_mode": json_mode})
        if not self.success:
            return {"success": False, "error": "HTTP error: 402", "tokens_used": 0}
        items = json.loads(messages[0]["content"])["items"]
        out = {"items": [{"id": i["id"], "text": self.translate(i["text"])} for i in items]}
        return {"success": True, "content": json.dumps(out), "tokens_used": 120}


@pytest.fixture
def client(monkeypatch):
    tr.clear_cache()
    fake = FakeDeepSeek()
    monkeypatch.setattr(tr, "get_deepseek_client", lambda: fake)
    app = FastAPI()
    app.include_router(tr.router)
    c = TestClient(app)
    c.fake = fake
    return c


def _body(texts, glossary=None):
    return {"source_lang": "fr", "target_lang": "en",
            "glossary": glossary or [], "items": [{"id": str(i), "text": t} for i, t in enumerate(texts)]}


def test_translates_batch_with_service_token(client):
    r = client.post("/api/winai/translate-batch", json=_body(["Épreuve de maths", "Corrigé 2023"]),
                    headers={"Authorization": _token()})
    assert r.status_code == 200
    data = r.json()
    assert [i["text"] for i in data["items"]] == ["EN:Épreuve de maths", "EN:Corrigé 2023"]
    assert data["tokens_used"] == 120
    assert data["missing"] == []
    assert client.fake.calls[0]["json_mode"] is True


def test_glossary_is_imposed_in_system_prompt(client):
    glossary = [{"fr": "épreuve", "en": "paper"}, {"fr": "corrigé", "en": "worked solution"}]
    client.post("/api/winai/translate-batch", json=_body(["Une épreuve"], glossary), headers={"Authorization": _token()})
    system = client.fake.calls[0]["system"]
    assert "- épreuve -> paper" in system
    assert "- corrigé -> worked solution" in system
    assert "acronyms" in system and "numbers, years" in system


def test_duplicates_and_cache_avoid_second_call(client):
    body = _body(["Physique", "Physique", "Chimie"])
    r1 = client.post("/api/winai/translate-batch", json=body, headers={"Authorization": _token()})
    assert len(json.loads(client.fake.calls[0]["messages"][0]["content"])["items"]) == 2  # doublon envoyé une fois
    assert [i["text"] for i in r1.json()["items"]] == ["EN:Physique", "EN:Physique", "EN:Chimie"]
    r2 = client.post("/api/winai/translate-batch", json=body, headers={"Authorization": _token()})
    assert len(client.fake.calls) == 1  # tout vient du cache
    assert r2.json()["cached"] == 3
    assert r2.json()["tokens_used"] == 0


def test_user_token_and_wrong_scope_are_refused(client):
    user = "Bearer " + jwt.encode({"sub": "1", "user_id": 1, "email": "a@b.c", "role": "admin", "iss": JWT_ISSUER, "aud": "WinPlusUsers",
                                   "exp": int(time.time()) + 300}, JWT_SECRET, algorithm="HS256")
    assert client.post("/api/winai/translate-batch", json=_body(["x"]), headers={"Authorization": user}).status_code == 403
    assert client.post("/api/winai/translate-batch", json=_body(["x"]),
                       headers={"Authorization": _token(scope="ai.notification")}).status_code == 403
    assert client.fake.calls == []


def test_changed_numbers_are_rejected_as_missing(client, monkeypatch):
    fake = FakeDeepSeek(translate=lambda t: t.replace("2023", "2024"))
    monkeypatch.setattr(tr, "get_deepseek_client", lambda: fake)
    r = client.post("/api/winai/translate-batch", json=_body(["BEPC 2023"]), headers={"Authorization": _token()})
    assert r.status_code == 200
    assert r.json()["items"] == []
    assert r.json()["missing"] == ["0"]


def test_provider_failure_returns_503(client, monkeypatch):
    monkeypatch.setattr(tr, "get_deepseek_client", lambda: FakeDeepSeek(success=False))
    r = client.post("/api/winai/translate-batch", json=_body(["Bonjour"]), headers={"Authorization": _token()})
    assert r.status_code == 503


def test_only_fr_to_en_and_batch_limits(client):
    body = _body(["x"])
    body["target_lang"] = "de"
    assert client.post("/api/winai/translate-batch", json=body, headers={"Authorization": _token()}).status_code == 400
    big = _body(["a"] * (tr.MAX_ITEMS + 1))
    assert client.post("/api/winai/translate-batch", json=big, headers={"Authorization": _token()}).status_code == 413


def test_parse_model_items_tolerates_fences_and_noise():
    assert tr.parse_model_items('```json\n{"items": [{"id": "0", "text": "Hello"}]}\n```') == {"0": "Hello"}
    assert tr.parse_model_items('Here: {"items": [{"id": 1, "text": "A"}]} done') == {"1": "A"}
    assert tr.parse_model_items("not json") == {}


def test_keeps_numbers():
    assert tr.keeps_numbers("BEPC 2023, 3,5 points", "BEPC 2023, 3.5 marks")
    assert not tr.keeps_numbers("Session 2019", "Session 2018")
