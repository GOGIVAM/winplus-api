"""Entretien de vérification des répétiteurs (routes/tutor_interview_routes.py) : guide et compte rendu de
secours, moyenne pondérée, lecture tolérante de la réponse du modèle, droits (admin ou jeton de service).
Le modèle est remplacé par un simulacre (_deepseek_json)."""

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

import routes.tutor_interview_routes as ti
from services import language

GUIDE = "/api/admin/tutor-interview/guide"
REPORT = "/api/admin/tutor-interview/report"


@pytest.fixture
def english():
    token = language.begin_request_language("en")
    yield
    language.end_request_language(token)


# ── listes et moyenne pondérée ──────────────────────────────────────────────

def test_clean_list_strips_dedups_truncates_and_caps():
    assert ti._clean_list([" Maths ", "Maths", "", None, "Physique"]) == ["Maths", "Physique"]
    assert ti._clean_list(["x" * 150]) == ["x" * 100]
    assert len(ti._clean_list([f"m{i}" for i in range(30)])) == ti.MAX_LIST == 12
    assert ti._clean_list(None) == []


def test_weighted_average_uses_grid_weights():
    scores = {"mastery": 5, "pedagogy": 4, "communication": 3, "safety": 5, "reliability": 2}
    # (5*3 + 4*3 + 3*2 + 5*3 + 2*2) / 13 = 52 / 13 = 4.0
    assert ti._weighted_average(scores, ti.DEFAULT_GRID) == 4.0


def test_weighted_average_ignores_invalid_and_out_of_range_values():
    scores = {"mastery": 4, "pedagogy": "x", "communication": 0, "safety": 6, "reliability": None}
    assert ti._weighted_average(scores, ti.DEFAULT_GRID) == 4.0
    assert ti._weighted_average({}, ti.DEFAULT_GRID) is None
    assert ti._weighted_average({"mastery": "4"}, ti.DEFAULT_GRID) == 4.0


# ── analyse des questions du modèle ─────────────────────────────────────────

def test_parse_questions_accepts_dicts_and_strings_and_caps_at_eight():
    raw = [{"question": " Q1 ", "purpose": "p", "good_signs": "g"}, "Q2", {"question": "  "}, 42, {"autre": 1}]
    parsed = ti._parse_questions(raw)
    assert [(q.question, q.purpose, q.good_signs) for q in parsed] == [("Q1", "p", "g"), ("Q2", "", "")]
    assert len(ti._parse_questions([f"q{i}" for i in range(20)])) == 8
    assert ti._parse_questions("pas une liste") == [] and ti._parse_questions(None) == []


def test_parse_questions_truncates_long_texts():
    q = ti._parse_questions([{"question": "q" * 600, "purpose": "p" * 400, "good_signs": "g" * 400}])[0]
    assert (len(q.question), len(q.purpose), len(q.good_signs)) == (500, 300, 300)


# ── guide de secours ────────────────────────────────────────────────────────

def test_template_guide_has_four_sections_and_fixed_grid():
    guide = ti._template_guide(ti.GuideRequest(subjects=["Maths", "Physique"], levels=["Terminale"]))
    assert [s.key for s in guide.sections] == ["fond", "pedagogie", "situations", "securite"]
    assert len(guide.sections[0].questions) == 3       # deux matières + la question sur l'erreur fréquente
    assert len(guide.sections[3].questions) == 4
    assert guide.based_on == "template" and guide.grid == ti.DEFAULT_GRID and guide.pass_rule == ti.PASS_RULE
    assert guide.intro.startswith("Entretien en visio avec un répétiteur en Maths, Physique (Terminale).")
    assert "Terminale" in guide.sections[0].questions[0].question


def test_template_guide_defaults_and_in_person_format():
    guide = ti._template_guide(ti.GuideRequest(format="in_person"))
    assert "la matière principale" in guide.intro and "les niveaux déclarés" in guide.intro
    assert guide.intro.startswith("Entretien en rencontre")


def test_template_guide_in_english(english):
    guide = ti._template_guide(ti.GuideRequest(subjects=["Maths"]))
    assert guide.pass_rule == ti.PASS_RULE_EN and guide.grid == ti.DEFAULT_GRID_EN
    assert guide.sections[0].title == "Subject questions"


def test_grid_weights_and_keys_are_identical_in_both_languages():
    assert [(c.key, c.weight) for c in ti.DEFAULT_GRID] == [(c.key, c.weight) for c in ti.DEFAULT_GRID_EN]


# ── compte rendu de secours ─────────────────────────────────────────────────

def _report(**kw):
    kw.setdefault("notes", "Bon entretien.")
    return ti._template_report(ti.ReportRequest(**kw))


GOOD = {"mastery": 5, "pedagogy": 4, "communication": 4, "safety": 5, "reliability": 4}


def test_report_without_scores_is_unsure():
    r = _report()
    assert r.recommendation == "unsure" and r.based_on == "template"
    assert "Bon entretien." in r.report and "NON joints" in r.report


def test_report_pass_with_good_scores_lists_the_grid():
    r = _report(scores=GOOD, identity_attached=True)
    assert r.recommendation == "pass"
    assert "- Sécurité avec des mineurs : 5/5" in r.report and "Moyenne pondérée : 4.46/5" in r.report
    assert "en attente de validation" in r.report


def test_report_fails_when_safety_is_below_four_even_with_high_average():
    r = _report(scores={**GOOD, "safety": 3})
    assert r.recommendation == "fail"


def test_report_fails_when_average_is_below_three_and_a_half():
    r = _report(scores={"mastery": 3, "pedagogy": 3, "communication": 3, "safety": 4, "reliability": 3})
    assert r.recommendation == "fail"


def test_report_identity_states():
    assert "validés par un admin" in _report(identity_approved=True).report
    assert "NON joints" in _report().report


def test_report_in_english(english):
    r = ti._template_report(ti.ReportRequest(notes="Good.", scores=GOOD, identity_approved=True))
    assert "Weighted average" in r.report and "approved by an admin" in r.report and r.recommendation == "pass"


# ── routes ──────────────────────────────────────────────────────────────────

@pytest.fixture
def client(monkeypatch):
    state = {"raw": None, "calls": []}

    def fake_json(prompt, system, max_tokens=800):
        state["calls"].append((prompt, system, max_tokens))
        return state["raw"]

    monkeypatch.setattr(ti, "_deepseek_json", fake_json)
    app = FastAPI()
    app.include_router(ti.tutor_interview_router, prefix="/api")
    c = TestClient(app)
    c.state = state
    return c


def test_routes_require_admin_or_the_service_scope(client, headers_for, service_headers):
    for url, body in ((GUIDE, {}), (REPORT, {"notes": "x"})):
        assert client.post(url, json=body).status_code in (401, 403)
        assert client.post(url, json=body, headers=headers_for(1, "teacher")).status_code == 403
        assert client.post(url, json=body, headers=service_headers("ai.autre")).status_code == 403
        assert client.post(url, json=body, headers=headers_for(1, "admin")).status_code == 200
        assert client.post(url, json=body, headers=service_headers("ai.tutor-interview")).status_code == 200
    assert client.state["calls"], "le modèle simulé est appelé pour les accès autorisés"


def test_report_requires_notes(client, headers_for):
    assert client.post(REPORT, json={}, headers=headers_for(1, "admin")).status_code == 422


def test_guide_falls_back_to_the_template_when_the_model_returns_nothing(client, headers_for):
    r = client.post(GUIDE, json={"subjects": ["Maths"]}, headers=headers_for(1, "admin"))
    assert r.status_code == 200 and r.json()["based_on"] == "template"
    assert client.state["calls"][0][2] == 1800


def test_guide_uses_model_questions_and_fills_missing_sections_from_the_template(client, headers_for):
    client.state["raw"] = {"intro": " Intro du modèle. ", "fond": [{"question": "Question du modèle ?"}],
                           "pedagogie": "pas une liste"}
    r = client.post(GUIDE, json={"subjects": ["Maths"]}, headers=headers_for(1, "admin")).json()
    assert r["based_on"] == "winai" and r["intro"] == "Intro du modèle."
    sections = {s["key"]: s for s in r["sections"]}
    assert [q["question"] for q in sections["fond"]["questions"]] == ["Question du modèle ?"]
    template = ti._template_guide(ti.GuideRequest(subjects=["Maths"]))
    assert len(sections["pedagogie"]["questions"]) == len(template.sections[1].questions)
    assert r["grid"] == [c.model_dump() for c in ti.DEFAULT_GRID] and r["pass_rule"] == ti.PASS_RULE


def test_guide_blank_intro_falls_back_to_the_template_intro(client, headers_for):
    client.state["raw"] = {"intro": "  "}
    r = client.post(GUIDE, json={"subjects": ["Maths"]}, headers=headers_for(1, "admin")).json()
    assert r["intro"] == ti._template_guide(ti.GuideRequest(subjects=["Maths"])).intro


def test_guide_prompt_carries_the_profile(client, headers_for):
    client.post(GUIDE, json={"subjects": ["Maths"], "levels": ["3e"], "specialties": ["Algèbre"],
                             "teaching_style": "ludique", "format": "in_person"}, headers=headers_for(1, "admin"))
    prompt = client.state["calls"][0][0]
    assert "Matières : Maths." in prompt and "Niveaux : 3e." in prompt and "Spécialités : Algèbre." in prompt
    assert "Style déclaré : ludique." in prompt and "rencontre physique" in prompt


def test_report_falls_back_when_the_model_gives_no_report(client, headers_for):
    for raw in (None, {"report": "  "}, ["liste"]):
        client.state["raw"] = raw
        r = client.post(REPORT, json={"notes": "Notes", "scores": GOOD}, headers=headers_for(1, "admin")).json()
        assert r["based_on"] == "template" and r["recommendation"] == "pass"


def test_report_uses_the_model_output_and_sanitises_it(client, headers_for):
    client.state["raw"] = {"report": "Compte rendu.", "recommendation": " PASS ",
                           "strengths": ["Clair", " ", 3], "concerns": "pas une liste"}
    r = client.post(REPORT, json={"notes": "N", "identity_attached": True}, headers=headers_for(1, "admin")).json()
    assert r == {"report": "Compte rendu.", "recommendation": "pass", "strengths": ["Clair", "3"], "concerns": [],
                 "based_on": "winai"}


def test_report_unknown_recommendation_becomes_unsure(client, headers_for):
    client.state["raw"] = {"report": "R", "recommendation": "peut-être", "strengths": [], "concerns": []}
    r = client.post(REPORT, json={"notes": "N", "identity_attached": True}, headers=headers_for(1, "admin")).json()
    assert r["recommendation"] == "unsure"


def test_report_warns_when_identity_is_missing_and_not_otherwise(client, headers_for):
    client.state["raw"] = {"report": "R", "recommendation": "pass"}
    missing = client.post(REPORT, json={"notes": "N"}, headers=headers_for(1, "admin")).json()
    assert "pièce d'identité et le selfie ne sont pas joints" in missing["report"]
    attached = client.post(REPORT, json={"notes": "N", "identity_attached": True}, headers=headers_for(1, "admin")).json()
    assert attached["report"] == "R"


def test_report_prompt_includes_the_pass_rule_and_scores(client, headers_for):
    client.post(REPORT, json={"notes": "Notes de l'agent", "scores": {"safety": 5}}, headers=headers_for(1, "admin"))
    prompt = client.state["calls"][0][0]
    assert "safety : 5/5" in prompt and ti.PASS_RULE in prompt and "Notes de l'agent" in prompt
