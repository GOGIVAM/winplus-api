"""Q&A automatique du canal de formation (routes/canal_qa_routes.py) : prompt construit, seuil de confiance,
lecture tolérante de la réponse du modèle. Le modèle est remplacé par un simulacre."""

import json

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

import routes.canal_qa_routes as cq

URL = "/winai/canal-qa"


class FakeDeepSeek:
    def __init__(self, content=None, error=None):
        self.content, self.error, self.calls = content, error, []

    def chat(self, messages, system_prompt=None, max_tokens=None, temperature=None, json_mode=False):
        self.calls.append({"messages": messages, "system": system_prompt, "max_tokens": max_tokens,
                           "temperature": temperature})
        if self.error:
            raise self.error
        return {"success": True, "content": self.content}


@pytest.fixture
def make_client(monkeypatch):
    def make(content=None, error=None):
        fake = FakeDeepSeek(content, error)
        monkeypatch.setattr(cq, "get_deepseek_client", lambda: fake)
        app = FastAPI()
        app.include_router(cq.canal_qa_router)
        client = TestClient(app, raise_server_exceptions=False)
        client.fake = fake
        return client

    return make


def _ask(client, headers, **body):
    payload = {"question": "Qu'est-ce qu'une dérivée ?", "course_title": "Analyse", "lessons_context": ["Leçon 1"]}
    payload.update(body)
    return client.post(URL, json=payload, headers=headers)


# ── prompt ──────────────────────────────────────────────────────────────────

def test_build_prompt_contains_course_title_and_lessons():
    question, system = cq._build_prompt(cq.CanalQaRequest(question="Q ?", course_title="Analyse",
                                                          lessons_context=["Un", "Deux"]))
    assert question == "Q ?"
    assert "« Analyse »" in system and "Un\n\nDeux" in system
    assert "UNIQUEMENT à partir du contenu des leçons" in system


def test_build_prompt_default_title_and_context_cap():
    _, system = cq._build_prompt(cq.CanalQaRequest(question="Q", lessons_context=["x" * 7000]))
    assert "« cette formation »" in system
    assert "x" * 6000 in system and "x" * 6001 not in system


# ── route ───────────────────────────────────────────────────────────────────

def test_requires_authentication(make_client):
    client = make_client('{"answer": "a", "confidence": 1}')
    assert client.post(URL, json={"question": "Q"}).status_code in (401, 403)
    assert client.fake.calls == []


def test_question_is_required(make_client, headers_for):
    assert make_client("{}").post(URL, json={}, headers=headers_for()).status_code == 422


def test_confident_answer_is_returned(make_client, headers_for):
    client = make_client(json.dumps({"answer": "La pente de la tangente.", "confidence": 0.92}))
    r = _ask(client, headers_for())
    assert r.status_code == 200
    assert r.json() == {"action": "answered", "answer": "La pente de la tangente.", "confidence": 0.92}
    call = client.fake.calls[0]
    assert call["messages"] == [{"role": "user", "content": "Qu'est-ce qu'une dérivée ?"}]
    assert call["max_tokens"] == 400 and call["temperature"] == 0.3
    assert "Leçon 1" in call["system"]


@pytest.mark.parametrize("confidence,action", [(0.70, "answered"), (0.69, "tag_professor"), (0.0, "tag_professor")])
def test_confidence_threshold_is_inclusive_at_seventy_percent(make_client, headers_for, confidence, action):
    client = make_client(json.dumps({"answer": "Réponse", "confidence": confidence}))
    r = _ask(client, headers_for())
    assert r.json()["action"] == action and r.json()["confidence"] == confidence


def test_low_confidence_never_returns_the_guess(make_client, headers_for):
    r = _ask(make_client(json.dumps({"answer": "Peut-être", "confidence": 0.3})), headers_for())
    assert r.json() == {"action": "tag_professor", "answer": None, "confidence": 0.3}


@pytest.mark.parametrize("content", [
    '{"answer": null, "confidence": 0.99}', '{"confidence": 0.99}', '{"answer": "", "confidence": 0.99}',
])
def test_missing_or_empty_answer_tags_the_professor(make_client, headers_for, content):
    assert _ask(make_client(content), headers_for()).json()["action"] == "tag_professor"


def test_missing_or_null_confidence_counts_as_zero(make_client, headers_for):
    r = _ask(make_client('{"answer": "Réponse"}'), headers_for())
    assert r.json() == {"action": "tag_professor", "answer": None, "confidence": 0.0}
    assert _ask(make_client('{"answer": "R", "confidence": null}'), headers_for()).json()["confidence"] == 0.0


def test_code_fences_around_json_are_removed(make_client, headers_for):
    fenced = '```json\n{"answer": "Oui.", "confidence": 0.8}\n```'
    assert _ask(make_client(fenced), headers_for()).json()["action"] == "answered"


def test_numeric_string_confidence_is_accepted(make_client, headers_for):
    assert _ask(make_client('{"answer": "Oui.", "confidence": "0.9"}'), headers_for()).json()["confidence"] == 0.9


@pytest.mark.parametrize("content", ["pas du json", ""])
def test_unparsable_model_output_tags_the_professor(make_client, headers_for, content):
    r = _ask(make_client(content), headers_for())
    assert r.status_code == 200
    assert r.json() == {"action": "tag_professor", "answer": None, "confidence": 0.0}


@pytest.mark.xfail(strict=False, reason="un JSON qui n'est pas un objet, ou une confiance non numérique, lève "
                   "AttributeError/ValueError hors du try de canal_qa : 500 au lieu de tag_professor")
@pytest.mark.parametrize("content", ["[1, 2]", '{"answer": "x", "confidence": "haute"}'])
def test_unexpected_json_shapes_should_also_tag_the_professor(make_client, headers_for, content):
    r = _ask(make_client(content), headers_for())
    assert r.status_code == 200 and r.json()["action"] == "tag_professor"


def test_model_failure_tags_the_professor_instead_of_failing(make_client, headers_for):
    r = _ask(make_client(error=RuntimeError("fournisseur indisponible")), headers_for())
    assert r.status_code == 200 and r.json()["action"] == "tag_professor"
