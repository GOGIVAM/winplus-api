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


URL_SYNTH = "/api/winai/synthese/generate-content"


def test_synthesis_writes_the_parent_week_from_the_facts_only(env, headers_for):
    fake = env.use("**Bilan** " + " ".join(["mot"] * 60))
    r = env.post(URL_SYNTH, json={"kind": "parent-week", "name": "Awa", "facts": ["3 quiz terminés", "moyenne 64 %"]},
                 headers=headers_for(5))
    assert r.status_code == 200
    assert r.json()["title"] == "Bilan de la semaine de Awa" and r.json()["text"].startswith("Bilan mot")
    prompt = fake.calls[0]["prompt"]
    assert "- 3 quiz terminés" in prompt and "Awa" in prompt and "parent" in prompt


def test_synthesis_refuses_an_unknown_kind_no_facts_and_a_too_short_answer(env, headers_for):
    env.use("trop court")
    assert env.post(URL_SYNTH, json={"kind": "autre", "name": "x", "facts": ["a"]}, headers=headers_for(5)).status_code == 422
    assert env.post(URL_SYNTH, json={"kind": "class", "name": "x", "facts": []}, headers=headers_for(5)).status_code == 422
    r = env.post(URL_SYNTH, json={"kind": "class", "name": "Tle C", "facts": ["12 élèves"]}, headers=headers_for(5))
    assert r.status_code == 502 and "synthèse" in r.json()["detail"]

def test_sources_without_a_matching_search_fall_back_to_reading_the_chosen_documents(env, headers_for, monkeypatch):
    async def nothing(filt, messages, user_id):
        return "", 30, []

    seen = {}

    def scroll(filt, user_id):
        seen["args"] = (filt, user_id)
        return "\n[Sources choisies par l'élève]\n1. [Source : Ma fiche]\nPythagore", [{"doc_id": "winsrc_7_x", "title": "Ma fiche", "page": None}]

    monkeypatch.setattr(wa, "build_sources_context_block", nothing)
    monkeypatch.setattr(wa, "_scroll_sources", scroll)
    fake = env.use(json.dumps({"title": "Résumé", "script": " ".join(["mot"] * 80)}))
    r = env.post(URL_AUDIO, json={"user_id": 7, "sources_filter": {"doc_ids": ["winsrc_7_x"]}}, headers=headers_for(7))
    assert r.status_code == 200 and r.json()["citations"][0]["doc_id"] == "winsrc_7_x"
    assert seen["args"] == ({"doc_ids": ["winsrc_7_x"]}, 7) and "Pythagore" in fake.calls[0]["prompt"]
    assert env.usage == [30]


def test_scroll_sources_keeps_the_owner_filter_and_orders_by_page(monkeypatch):
    import RAG.router as rr
    import RAG.shared.vector_store as vs
    from types import SimpleNamespace
    calls = []

    def scroll_by_filter(collection, f, limit=20):
        calls.append(f)
        return [SimpleNamespace(payload={"doc_id": "winsrc_7_x", "title": "Ma fiche", "page": 2, "text": "deux"}),
                SimpleNamespace(payload={"doc_id": "winsrc_7_x", "title": "Ma fiche", "page": 1, "text": "un"})]

    monkeypatch.setattr(rr, "_active_collection", lambda: "c")
    monkeypatch.setattr(vs, "scroll_by_filter", scroll_by_filter)
    block, cited = wa._scroll_sources({"doc_ids": ["winsrc_7_x"], "subject_ids": [3]}, 7)
    assert calls == [{"doc_id": ["winsrc_7_x"], "owner_user_id": 7, "status": "active"}, {"subject_id": [3], "status": "active"}]
    assert block.index("page 1") < block.index("page 2") and [c["page"] for c in cited] == [1, 2]

# ── Épreuve complète pour l'enseignant ───────────────────────────────────────

URL_EXAM = "/api/teacher/epreuve/generate-content"


def _exam(points):
    return {"title": "Devoir de géométrie", "subject": "Maths", "instructions": "Calculatrice autorisée.",
            "exercises": [{"title": "Exercice 1", "context": "Triangle ABC",
                           "questions": [{"number": f"1.{i}", "statement": f"Q{i}", "points": p,
                                          "answer": f"R{i}", "scoring_guide": "méthode 1 pt"}
                                         for i, p in enumerate(points)]},
                          {"title": "vide", "questions": [{"statement": ""}]}]}


TAGGED = r"""TITRE: Exercice 1 : Pythagore
CONTEXTE: Soit ABC un triangle rectangle en A
avec AB = 3 cm et AC = 4 cm.
QUESTION: 1.a | 1
ÉNONCÉ: Calculer $BC$.
RÉPONSE: $BC^2 = 3^2 + 4^2 = 25$ donc "BC = 5" cm.
BARÈME: théorème cité 0,5 ; résultat 0,5
QUESTION: 1.b | 3
**ÉNONCÉ:** Calculer $\cos \widehat{B}$.
RÉPONSE: $\cos B = 3/5$
BARÈME: méthode 2 ; résultat 1
QUESTION: 1.c | 2
ÉNONCÉ:
"""


def test_tagged_exercise_is_parsed_without_json():
    ex = wa._parse_exercise(TAGGED)
    assert ex["title"] == "Exercice 1 : Pythagore"
    assert ex["context"] == "Soit ABC un triangle rectangle en A\navec AB = 3 cm et AC = 4 cm."
    assert [q["number"] for q in ex["questions"]] == ["1.a", "1.b"], "la question sans énoncé est écartée"
    assert ex["questions"][0]["answer"] == '$BC^2 = 3^2 + 4^2 = 25$ donc "BC = 5" cm.'
    assert ex["questions"][1]["points"] == 3 and "\widehat" in ex["questions"][1]["statement"]
    assert wa._parse_exercise("rien d'utile") is None


def test_exercise_targets_grow_and_sum_to_total():
    assert wa._exercise_targets(3, 20) == [4.5, 6.5, 9.0]
    assert sum(wa._exercise_targets(4, 20)) == 20 and wa._exercise_targets(1, 20) == [20]


def test_exam_is_reserved_to_teachers(env, headers_for):
    env.use(TAGGED)
    body = {"user_id": 7, "subject": "Maths", "level": "3e", "exercises": 1}
    assert env.post(URL_EXAM, json=body, headers=headers_for(7, "student")).status_code == 403
    assert env.post(URL_EXAM, json=body, headers=headers_for(7, "teacher")).status_code == 200
    assert env.post(URL_EXAM, json={**body, "total_points": 500}, headers=headers_for(7, "teacher")).status_code == 422


PLAN = "1. Calcul de longueurs : application directe\n2. Rapports trigonométriques : calculs\n3. Hauteur d'un poteau à Douala : problème"


def test_exam_plans_distinct_exercises_retries_a_wrong_scoring_and_keeps_an_exact_total(env, headers_for):
    # Plan, puis chaque exercice : TAGGED totalise 4 points, jamais la cible -> second essai, puis barème ramené.
    fake = env.use(PLAN, TAGGED)
    r = env.post(URL_EXAM, json={"user_id": 7, "subject": "Maths", "topic": "Pythagore", "level": "3e",
                                 "exam_type": "composition", "duration_minutes": 90, "total_points": 20, "exercises": 3},
                 headers=headers_for(7, "teacher"))
    assert r.status_code == 200
    data = r.json()
    assert data["total_points"] == 20 and data["duration_minutes"] == 90 and data["exam_type"] == "composition"
    assert data["title"] == "Composition de Maths (3e) : Pythagore"
    assert len(fake.calls) == 1 + 3 * 2 and len(data["exercises"]) == 3
    assert [e["points"] for e in data["exercises"]] == [4.5, 6.5, 9.0]
    assert sum(q["points"] for e in data["exercises"] for q in e["questions"]) == 20
    q = data["exercises"][0]["questions"][0]
    assert q["scoringGuide"] == "théorème cité 0,5 ; résultat 0,5" and q["answer"].startswith("$BC^2")
    assert "Découpe le contenu à évaluer en 3 exercices" in fake.calls[0]["prompt"]
    prompts = [c["prompt"] for c in fake.calls[1:]]
    assert all("composition trimestrielle" in p and "Classe : 3e" in p and "90 minutes" in p for p in prompts)
    assert any("UNIQUEMENT sur : Hauteur d'un poteau à Douala : problème" in p and "9 points" in p
               and "lettres MNP" in p and "N'y empiète pas" in p for p in prompts)


def test_exam_keeps_the_model_scoring_when_it_matches_and_strips_points_from_titles(env, headers_for):
    exact = (TAGGED.replace("TITRE: Exercice 1 : Pythagore", "TITRE: Exercice 1 : Pythagore (5 points)")
             .replace("QUESTION: 1.a | 1", "QUESTION: 1.a | 2"))
    fake = env.use(exact)
    r = env.post(URL_EXAM, json={"user_id": 7, "subject": "Maths", "total_points": 5, "exercises": 1},
                 headers=headers_for(7, "teacher"))
    assert r.status_code == 200 and len(fake.calls) == 1, "un exercice seul : ni plan ni second essai"
    ex = r.json()["exercises"][0]
    assert ex["title"] == "Exercice 1 : Pythagore" and [q["points"] for q in ex["questions"]] == [2, 3]


def test_plan_needs_one_line_per_exercise():
    assert wa._parse_plan("Voici :\n1) Pythagore\n**2.** Trigonométrie\n3 - Problème", 3) == ["Pythagore", "Trigonométrie", "Problème"]
    assert wa._parse_plan("1. seul", 2) == []


def test_exam_rounding_gap_and_missing_points():
    exos = wa._clean_exam(_exam([1, 1, 1]), 20)
    pts = [q["points"] for q in exos[0]["questions"]]
    assert sum(pts) == 20 and all(p * 4 == int(p * 4) for p in pts)
    exos = wa._clean_exam(_exam(["", "x", None]), 10)
    assert sum(q["points"] for q in exos[0]["questions"]) == 10
    assert wa._clean_exam({"exercises": "rien"}, 20) == []


def test_exam_fails_cleanly_when_the_model_returns_nothing_usable(env, headers_for):
    env.use("pas d'exercice")
    r = env.post(URL_EXAM, json={"user_id": 7, "subject": "Maths", "exercises": 2}, headers=headers_for(7, "teacher"))
    assert r.status_code == 502
