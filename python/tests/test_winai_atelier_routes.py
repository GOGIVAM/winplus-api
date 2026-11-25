"""Atelier WinAI (routes/winai_atelier_routes.py) : fiches recto verso et script de résumé audio, à partir
des sources choisies. Fournisseur d'IA et recherche RAG simulés."""

import json

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

import routes.winai_atelier_routes as wa

URL_CARDS = "/api/winai/flashcards/generate-content"
URL_AUDIO = "/api/winai/resume-audio/generate-content"


class FakeClient:
    def __init__(self, *answers):
        self.answers, self.calls = list(answers), []

    def chat(self, messages, max_tokens=None, temperature=None, **kw):
        self.calls.append({"prompt": messages[0]["content"], "max_tokens": max_tokens})
        answer = self.answers.pop(0) if len(self.answers) > 1 else self.answers[0]
        if isinstance(answer, Exception):
            raise answer
        return {"content": answer}


@pytest.fixture
def env(monkeypatch):
    app = FastAPI()
    app.include_router(wa.winai_atelier_router, prefix="/api")
    client = TestClient(app, raise_server_exceptions=False)
    client.rag_calls, client.usage = [], []

    async def fake_sources(filt, messages, user_id):
        client.rag_calls.append((filt, messages[0]["content"], user_id))
        return ("\n\n[Sources choisies]\n1. [Source : Ma fiche]\nPythagore : a²+b²=c²", 120,
                [{"doc_id": "winsrc_7_x", "subject_id": None, "course_id": None, "title": "Ma fiche", "page": 2}])

    monkeypatch.setattr(wa, "build_sources_context_block", fake_sources)
    monkeypatch.setattr(wa, "record_usage", lambda n: client.usage.append(n))

    def use(*answers):
        fake = FakeClient(*answers)
        monkeypatch.setattr(wa, "get_deepseek_client", lambda: fake)
        return fake

    client.use = use
    return client


def _cards(n):
    return [{"front": f"Question {i}", "back": f"Réponse {i}", "hint": "piste" if i == 0 else ""} for i in range(n)]


def test_flashcards_need_a_token_and_some_material(env, headers_for):
    env.use(json.dumps({"title": "T", "cards": _cards(6)}))
    assert env.post(URL_CARDS, json={"user_id": 7, "subject": "Maths"}).status_code in (401, 403)
    r = env.post(URL_CARDS, json={"user_id": 7}, headers=headers_for(7))
    assert r.status_code == 422
    assert env.post(URL_CARDS, json={"user_id": 7, "subject": "Maths", "count": 50}, headers=headers_for(7)).status_code == 422


def test_flashcards_from_sources_return_cards_citations_and_bill_the_search(env, headers_for):
    fake = env.use("Voici : " + json.dumps({"title": "Pythagore", "subject": "Maths", "cards": _cards(8) + _cards(2)}))
    r = env.post(URL_CARDS, json={"user_id": 7, "count": 6, "topic": "Pythagore",
                                  "sources_filter": {"doc_ids": ["winsrc_7_x"]}, "sources_context": "Erreur : 3²=6"},
                 headers=headers_for(7))
    assert r.status_code == 200
    body = r.json()
    assert body["title"] == "Pythagore" and len(body["cards"]) == 6
    assert body["cards"][0] == {"front": "Question 0", "back": "Réponse 0", "hint": "piste"}
    assert body["cards"][1]["hint"] is None
    assert body["citations"][0]["doc_id"] == "winsrc_7_x"
    assert env.rag_calls == [({"doc_ids": ["winsrc_7_x"]}, "Pythagore", 7)]
    assert env.usage == [120]
    prompt = fake.calls[0]["prompt"]
    assert "exactement 6 fiches" in prompt and "a²+b²=c²" in prompt and "Erreur : 3²=6" in prompt


def test_flashcards_retry_once_then_fail_with_a_readable_message(env, headers_for):
    fake = env.use("pas de json", json.dumps({"cards": _cards(5)}))
    r = env.post(URL_CARDS, json={"user_id": 7, "subject": "SVT", "count": 5}, headers=headers_for(7))
    assert r.status_code == 200 and len(r.json()["cards"]) == 5 and r.json()["title"] == "Fiches : SVT"
    assert len(fake.calls) == 2 and fake.calls[1]["max_tokens"] > fake.calls[0]["max_tokens"]

    env.use("rien")
    r = env.post(URL_CARDS, json={"user_id": 7, "subject": "SVT"}, headers=headers_for(7))
    assert r.status_code == 502 and "fiches" in r.json()["detail"]


def test_resume_audio_returns_a_clean_script_with_its_length(env, headers_for):
    script = "**Bonjour** ! " + " ".join(["mot"] * 150)
    env.use(json.dumps({"title": "Résumé Pythagore", "script": script}))
    r = env.post(URL_AUDIO, json={"user_id": 7, "subject": "Maths", "minutes": 2}, headers=headers_for(7))
    assert r.status_code == 200
    body = r.json()
    assert body["script"].startswith("Bonjour !") and "*" not in body["script"]
    assert body["word_count"] == 152 and body["estimated_seconds"] == round(152 / 140 * 60)
    assert body["citations"] == [] and env.rag_calls == []


def test_resume_audio_refuses_a_too_long_duration_and_a_too_short_script(env, headers_for):
    env.use(json.dumps({"script": "trop court"}))
    assert env.post(URL_AUDIO, json={"user_id": 7, "subject": "Maths", "minutes": 30}, headers=headers_for(7)).status_code == 422
    assert env.post(URL_AUDIO, json={"user_id": 7, "subject": "Maths"}, headers=headers_for(7)).status_code == 502
