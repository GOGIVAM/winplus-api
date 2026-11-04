"""Suggestions d'objectifs hebdomadaires (routes/goal_suggestions.py) : base déterministe, reformulation par le
modèle sans jamais toucher aux chiffres, repli en cas de réponse inutilisable. Le modèle est simulé."""

import json

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

import routes.goal_suggestions as gs
from services import language

URL = "/api/ai/goal-suggestions"


def _req(**kw):
    return gs.GoalSuggestionRequest(**kw)


# ── année d'examen ──────────────────────────────────────────────────────────

@pytest.mark.parametrize("level,expected", [
    ("Terminale C", True), ("TLE D", True), ("3ème", True), ("Prépa BAC", True), ("BEPC", True), ("Concours", True),
    ("Seconde A", False), ("Sixième", False), ("", False), (None, False),
])
def test_is_exam_year(level, expected):
    assert gs._is_exam_year(level) is expected


# ── base déterministe en français ───────────────────────────────────────────

def test_rule_based_for_an_exam_student_with_weak_subjects():
    result = gs._rule_based(_req(level="Terminale C", weak_subjects=["Maths", "Physique", "SVT", "Histoire"],
                                 done_this_week={"quiz": 2}))
    first, second, third = result.suggestions
    assert (first.title, first.studyHoursTarget, first.quizTarget, first.downloadsTarget) == ("Rythme examen", 12, 5, 4)
    assert second.title == "Rattrapage ciblé"
    assert second.reason == "Concentré sur Maths et Physique, vos matières les plus fragiles."
    assert (second.studyHoursTarget, second.quizTarget, second.downloadsTarget) == (10, 6, 3)
    assert third.title == "Palier suivant" and "2 quiz déjà passés" in third.reason
    assert (third.studyHoursTarget, third.quizTarget, third.downloadsTarget) == (9, 4, 3)
    # seules trois matières fragiles sont citées
    assert result.insight.startswith("Vos résultats les plus faibles sont en Maths, Physique, SVT.")


def test_rule_based_for_a_regular_student_without_weakness_or_quiz():
    result = gs._rule_based(_req(level="Seconde A"))
    first, second, third = result.suggestions
    assert (first.title, first.studyHoursTarget, first.quizTarget, first.downloadsTarget) == ("Rythme régulier", 7, 3, 2)
    assert (second.title, second.studyHoursTarget, second.quizTarget) == ("Consolidation", 5, 2)
    assert (third.title, third.studyHoursTarget, third.quizTarget, third.downloadsTarget) == ("Reprise en douceur", 3, 1, 1)
    assert result.insight.startswith("Aucune matière en difficulté marquée")


def test_rule_based_ignores_empty_subject_names_and_reads_string_counts():
    result = gs._rule_based(_req(weak_subjects=["", "Maths"], done_this_week={"quiz": "3"}))
    assert result.suggestions[1].reason == "Concentré sur Maths, vos matières les plus fragiles."
    assert result.suggestions[2].quizTarget == 5          # 3 quiz déjà faits, plus deux
    assert gs._rule_based(_req(done_this_week={"quiz": None})).suggestions[2].title == "Reprise en douceur"


# ── base déterministe en anglais ────────────────────────────────────────────

@pytest.fixture
def english():
    token = language.begin_request_language("en")
    yield
    language.end_request_language(token)


def test_english_account_gets_english_texts_with_the_same_numbers(english):
    req = dict(level="Terminale C", weak_subjects=["Maths"], done_this_week={"quiz": 1})
    en = gs._rule_based(_req(**req))
    assert [s.title for s in en.suggestions] == ["Exam pace", "Targeted catch-up", "Next step"]
    assert en.suggestions[1].reason == "Focused on Maths, your weakest subjects."
    assert "1 quizzes already taken" in en.suggestions[2].reason
    assert en.insight.startswith("Your weakest results are in Maths.")
    token = language.begin_request_language("fr")
    try:
        fr = gs._rule_based(_req(**req))
    finally:
        language.end_request_language(token)
    numbers = lambda r: [(s.studyHoursTarget, s.quizTarget, s.downloadsTarget) for s in r.suggestions]
    assert numbers(en) == numbers(fr)


def test_english_without_weakness(english):
    assert gs._rule_based(_req()).insight.startswith("No subject is clearly struggling")


# ── route ───────────────────────────────────────────────────────────────────

class FakeDeepSeek:
    def __init__(self, content="", success=True):
        self.content, self.success, self.calls = content, success, []

    def chat(self, messages, system_prompt=None, max_tokens=None, temperature=None, json_mode=False):
        self.calls.append({"messages": messages, "max_tokens": max_tokens})
        if not self.success:
            return {"success": False, "error": "HTTP error: 503", "content": "Désolé"}
        return {"success": True, "content": self.content}


@pytest.fixture
def make_client(monkeypatch):
    def make(content="", success=True):
        fake = FakeDeepSeek(content, success)
        monkeypatch.setattr(gs, "get_deepseek_client", lambda: fake)
        app = FastAPI()
        app.include_router(gs.router)
        client = TestClient(app)
        client.fake = fake
        return client

    return make


BODY = {"user_id": 38, "level": "Terminale C", "weak_subjects": ["Maths"], "done_this_week": {"quiz": 2}}


def _numbers(payload):
    return [(s["studyHoursTarget"], s["quizTarget"], s["downloadsTarget"]) for s in payload["suggestions"]]


def test_requires_authentication(make_client):
    client = make_client()
    assert client.post(URL, json=BODY).status_code in (401, 403)
    assert client.fake.calls == []


def test_empty_body_is_accepted(make_client, headers_for):
    r = make_client("[]").post(URL, json={}, headers=headers_for())
    assert r.status_code == 200 and len(r.json()["suggestions"]) == 3


def test_model_rewrites_reasons_but_never_the_numbers(make_client, headers_for):
    reasons = ["Une phrase réécrite numéro un.", "Une phrase réécrite numéro deux.", "Une phrase réécrite numéro trois."]
    client = make_client("Voici :\n" + json.dumps(reasons, ensure_ascii=False))
    r = client.post(URL, json=BODY, headers=headers_for())
    assert r.status_code == 200
    payload = r.json()
    assert [s["reason"] for s in payload["suggestions"]] == reasons
    assert _numbers(payload) == [(12, 5, 4), (10, 6, 3), (9, 4, 3)]
    prompt = client.fake.calls[0]["messages"][0]["content"]
    assert "Niveau : Terminale C" in prompt and "Matières fragiles : Maths" in prompt
    assert "(12 h, 5 quiz, 4 épreuves)" in prompt and "en français" in prompt
    assert client.fake.calls[0]["max_tokens"] == 300


def test_unsuitable_reasons_keep_the_deterministic_text(make_client, headers_for):
    base = gs._rule_based(_req(**BODY))
    reasons = ["court", "x" * 240, 42, "Une justification parfaitement valable."]
    r = make_client(json.dumps(reasons)).post(URL, json=BODY, headers=headers_for())
    got = [s["reason"] for s in r.json()["suggestions"]]
    assert got[0] == base.suggestions[0].reason      # trop courte (10 caractères ou moins)
    assert got[1] == base.suggestions[1].reason      # trop longue (240 ou plus)
    assert got[2] == base.suggestions[2].reason      # pas une chaîne


def test_extra_model_reasons_are_ignored_and_partial_lists_are_applied(make_client, headers_for):
    r = make_client(json.dumps(["Seule la première est réécrite."])).post(URL, json=BODY, headers=headers_for())
    base = gs._rule_based(_req(**BODY))
    reasons = [s["reason"] for s in r.json()["suggestions"]]
    assert reasons[0] == "Seule la première est réécrite."
    assert reasons[1:] == [s.reason for s in base.suggestions[1:]]


@pytest.mark.parametrize("client_args", [
    {"content": "", "success": False},                     # fournisseur en échec
    {"content": "pas de liste ici"},                       # aucune liste JSON
    {"content": "[ceci n'est pas du json]"},               # liste illisible
])
def test_unusable_model_answer_returns_the_deterministic_base(make_client, headers_for, client_args):
    r = make_client(**client_args).post(URL, json=BODY, headers=headers_for())
    base = gs._rule_based(_req(**BODY))
    assert r.status_code == 200
    assert r.json() == json.loads(base.model_dump_json())


def test_each_suggestion_has_the_documented_shape(make_client, headers_for):
    r = make_client("[]").post(URL, json=BODY, headers=headers_for())
    assert set(r.json()) == {"suggestions", "insight"}
    for s in r.json()["suggestions"]:
        assert set(s) == {"title", "reason", "studyHoursTarget", "quizTarget", "downloadsTarget"}
