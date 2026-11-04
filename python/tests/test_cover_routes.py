"""Route POST /api/ai/cover/generate (routes/cover_routes.py) : droits, validation, erreurs du modèle d'image.
La génération d'image est remplacée par un simulacre : aucun appel à Gemini."""

import base64

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

import routes.cover_routes as cr
from services.cover_image_client import CoverImage, CoverImageUnavailable


@pytest.fixture
def client(monkeypatch):
    calls = []

    def fake_generate(title, description, kind):
        calls.append((title, description, kind))
        return CoverImage(png_bytes=b"\x89PNG-fake", mime_type="image/png", model="modele-test")

    monkeypatch.setattr(cr, "generate_cover_png", fake_generate)
    app = FastAPI()
    app.include_router(cr.router)
    c = TestClient(app)
    c.calls = calls
    return c


URL = "/api/ai/cover/generate"


def test_requires_authentication(client):
    assert client.post(URL, json={"title": "T"}).status_code in (401, 403)
    assert client.post(URL, json={"title": "T"}, headers={"Authorization": "Bearer faux.jeton.ici"}).status_code == 401
    assert client.calls == []


def test_returns_base64_image_and_metadata(client, headers_for):
    r = client.post(URL, json={"title": "Maths", "description": "Algèbre", "kind": "exam"}, headers=headers_for())
    assert r.status_code == 200
    body = r.json()
    assert base64.b64decode(body["imageBase64"]) == b"\x89PNG-fake"
    assert body["mimeType"] == "image/png" and body["model"] == "modele-test"
    assert client.calls == [("Maths", "Algèbre", "exam")]


def test_kind_defaults_to_subject_and_description_is_optional(client, headers_for):
    assert client.post(URL, json={"title": "Maths"}, headers=headers_for()).status_code == 200
    assert client.calls == [("Maths", None, "subject")]


@pytest.mark.parametrize("kind", ["subject", "exam", "course"])
def test_allowed_kinds(client, headers_for, kind):
    assert client.post(URL, json={"title": "T", "kind": kind}, headers=headers_for()).status_code == 200


def test_unknown_kind_is_400_and_never_reaches_the_model(client, headers_for):
    r = client.post(URL, json={"title": "T", "kind": "video"}, headers=headers_for())
    assert r.status_code == 400 and r.json()["detail"] == "Type de contenu inconnu."
    assert client.calls == []


@pytest.mark.parametrize("payload", [
    {}, {"title": ""}, {"title": "x" * 301}, {"title": "T", "description": "d" * 4001},
])
def test_invalid_payload_is_422(client, headers_for, payload):
    assert client.post(URL, json=payload, headers=headers_for()).status_code == 422
    assert client.calls == []


def test_title_at_limits_is_accepted(client, headers_for):
    r = client.post(URL, json={"title": "x" * 300, "description": "d" * 4000}, headers=headers_for())
    assert r.status_code == 200


def test_unavailable_model_is_503_with_the_reason(monkeypatch, client, headers_for):
    def unavailable(title, description, kind):
        raise CoverImageUnavailable("GEMINI_API_KEY non configurée.")

    monkeypatch.setattr(cr, "generate_cover_png", unavailable)
    r = client.post(URL, json={"title": "T"}, headers=headers_for())
    assert r.status_code == 503 and r.json()["detail"] == "GEMINI_API_KEY non configurée."


def test_any_role_may_call_it(client, headers_for):
    for role in ("student", "teacher", "admin"):
        assert client.post(URL, json={"title": "T"}, headers=headers_for(role=role)).status_code == 200
