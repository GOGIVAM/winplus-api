"""Explications d'erreurs de quiz (routes/quiz_explain_routes.py) : limite par utilisateur, flux SSE, cache en base.
Le fournisseur d'IA est remplacé par un simulacre ; base SQLite en mémoire."""

import json

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

import database as dbm
import routes.quiz_explain_routes as qe

URL = "/explain-error"
BODY = {"question_text": "2+2 ?", "wrong_answer": "5", "correct_answer": "4", "subject": "Maths", "level": "le BEPC"}


class FakeStreamClient:
    def __init__(self, chunks):
        self.chunks, self.calls = chunks, []

    def chat_stream(self, messages, system_prompt=None, max_tokens=None, temperature=None, emit_usage=False):
        self.calls.append({"messages": messages, "max_tokens": max_tokens, "temperature": temperature})
        yield from self.chunks


def _delta(text):
    return "data: " + json.dumps({"delta": text}) + "\n\n"


def _parse(body):
    events = [line[6:] for line in body.split("\n\n") if line.startswith("data: ")]
    return [e if e == "[DONE]" else json.loads(e) for e in events]


@pytest.fixture(autouse=True)
def clean_rate_limit():
    qe._user_calls.clear()
    yield
    qe._user_calls.clear()


@pytest.fixture
def setup(monkeypatch, sqlite_database):
    db = sqlite_database(qe, [dbm.QuizExplanation])
    fake = FakeStreamClient([_delta("Parce que "), _delta("2+2 vaut 4."), "data: [DONE]\n\n"])
    monkeypatch.setattr(qe, "get_deepseek_client", lambda: fake)
    app = FastAPI()
    app.include_router(qe.quiz_explain_router)
    client = TestClient(app)
    client.fake, client.db = fake, db
    return client


# ── limite par utilisateur ──────────────────────────────────────────────────

class Clock:
    now = 1000.0

    def __call__(self):
        return self.now


def test_rate_limit_allows_three_calls_per_minute_per_user(monkeypatch):
    clock = Clock()
    monkeypatch.setattr(qe, "time", clock)
    assert [qe._allow(1) for _ in range(4)] == [True, True, True, False]
    assert qe._allow(2) is True                      # autre utilisateur : compteur séparé
    clock.now += 59
    assert qe._allow(1) is False
    clock.now += 2                                   # 61 s après les premiers appels : fenêtre libérée
    assert qe._allow(1) is True


def test_a_refused_call_is_not_counted(monkeypatch):
    clock = Clock()
    monkeypatch.setattr(qe, "time", clock)
    for _ in range(3):
        qe._allow(1)
    for _ in range(5):
        assert qe._allow(1) is False
    assert len(qe._user_calls[1]) == 3


# ── route ───────────────────────────────────────────────────────────────────

def test_requires_authentication(setup):
    assert setup.post(URL, json=BODY).status_code in (401, 403)
    assert setup.fake.calls == []


@pytest.mark.parametrize("missing", ["question_text", "wrong_answer", "correct_answer"])
def test_required_fields_are_validated(setup, headers_for, missing):
    body = {k: v for k, v in BODY.items() if k != missing}
    assert setup.post(URL, json=body, headers=headers_for()).status_code == 422


def test_streams_the_explanation_and_caches_it(setup, headers_for):
    r = setup.post(URL, json=BODY, headers=headers_for(7))
    assert r.status_code == 200 and r.headers["content-type"].startswith("text/event-stream")
    assert r.headers["cache-control"] == "no-cache"
    assert _parse(r.text) == [{"delta": "Parce que "}, {"delta": "2+2 vaut 4."}, "[DONE]"]

    prompt = setup.fake.calls[0]["messages"][0]["content"]
    assert "« 5 »" in prompt and "« 2+2 ? »" in prompt and "« 4 »" in prompt
    assert "le BEPC" in prompt and "en Maths" in prompt and "en français" in prompt
    assert setup.fake.calls[0]["max_tokens"] == 400

    cached = setup.db.all(dbm.QuizExplanation)
    assert len(cached) == 1 and cached[0].ExplanationText == "Parce que 2+2 vaut 4."
    assert cached[0].Subject == "Maths" and len(cached[0].QuestionHash) == 64


def test_second_identical_request_is_served_from_cache_without_the_model(setup, headers_for):
    setup.post(URL, json=BODY, headers=headers_for(7))
    again = setup.post(URL, json=BODY, headers=headers_for(8))      # autre utilisateur : le cache est partagé
    assert len(setup.fake.calls) == 1
    events = _parse(again.text)
    assert events[-1] == "[DONE]"
    assert "".join(e["delta"] for e in events[:-1]) == "Parce que 2+2 vaut 4."
    assert all(len(e["delta"]) <= 24 for e in events[:-1])
    assert len(setup.db.all(dbm.QuizExplanation)) == 1


def test_different_question_is_not_a_cache_hit(setup, headers_for):
    setup.post(URL, json=BODY, headers=headers_for(7))
    setup.post(URL, json={**BODY, "wrong_answer": "6"}, headers=headers_for(8))
    assert len(setup.fake.calls) == 2 and len(setup.db.all(dbm.QuizExplanation)) == 2


def test_defaults_when_subject_and_level_are_missing(setup, headers_for):
    body = {k: v for k, v in BODY.items() if k not in ("subject", "level")}
    setup.post(URL, json=body, headers=headers_for(7))
    prompt = setup.fake.calls[0]["messages"][0]["content"]
    assert "cet examen" in prompt and "cette matière" in prompt
    assert setup.db.all(dbm.QuizExplanation)[0].Subject is None


def test_fourth_call_in_a_minute_is_429(setup, headers_for):
    for i in range(3):
        assert setup.post(URL, json={**BODY, "wrong_answer": str(i)}, headers=headers_for(7)).status_code == 200
    r = setup.post(URL, json=BODY, headers=headers_for(7))
    assert r.status_code == 429 and "Limite d'explications" in r.json()["detail"]
    assert setup.post(URL, json=BODY, headers=headers_for(8)).status_code == 200


def test_a_stream_error_is_forwarded_and_nothing_is_cached(setup, headers_for):
    setup.fake.chunks = ['data: {"error": "Stream error"}\n\n', "data: [DONE]\n\n"]
    r = setup.post(URL, json=BODY, headers=headers_for(7))
    assert _parse(r.text) == [{"error": "Stream error"}, "[DONE]"]
    assert setup.db.all(dbm.QuizExplanation) == []
