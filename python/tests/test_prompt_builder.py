"""Constructeur de prompts système WinAI (services/prompt_builder.py) : détection de langue,
choix du prompt par rôle, blocs de contexte (profil, lacunes, mémoire, enfants suivis)."""

import pytest

from services.prompt_builder import (
    UserContext,
    build_system_prompt,
    detect_language,
    _children_block,
    _mistakes_block,
    _session_context_block,
)


# ── detect_language ─────────────────────────────────────────────────────────

def test_detect_language_pidgin_marker_wins():
    assert detect_language("wuna dey come") == "pidgin"


def test_detect_language_english():
    # "the" et "is" sont des mots anglais, aucun mot français
    assert detect_language("the cat is on a mat") == "english"


def test_detect_language_french():
    assert detect_language("le chat est sur la table") == "french"


def test_detect_language_empty_and_unknown_default_to_french():
    assert detect_language("") == "french"
    assert detect_language("xyzzy qwerty") == "french"


def test_detect_language_tie_stays_french():
    # "je" (fr) et "i" (en) : égalité, la condition exige en_hits > fr_hits
    assert detect_language("je i") == "french"


# ── choix du prompt par rôle ────────────────────────────────────────────────

@pytest.mark.parametrize("role,marker", [
    ("student", "le tuteur IA de la plateforme WinPlus"),
    ("parent", "conseiller pédagogique familial"),
    ("teacher", "attaché éditorial et pédagogique"),
    ("admin", "auditeur et analyste IA"),
    ("institution", "gestionnaire institutionnel IA"),
    ("organization", "gestionnaire institutionnel IA"),
])
def test_each_role_gets_its_own_prompt(role, marker):
    assert marker in build_system_prompt(UserContext(role=role))


def test_unknown_role_falls_back_to_student_prompt():
    assert "le tuteur IA de la plateforme WinPlus" in build_system_prompt(UserContext(role="alien"))


def test_none_context_gives_student_prompt():
    assert build_system_prompt(None) == build_system_prompt(UserContext())


def test_every_role_hides_the_underlying_model():
    for role in ("student", "parent", "teacher", "admin", "institution"):
        prompt = build_system_prompt(UserContext(role=role))
        assert "Je suis WinAI, l'assistant IA de WinPlus." in prompt


# ── lignes de profil ────────────────────────────────────────────────────────

def test_student_prompt_includes_profile_lines():
    ctx = UserContext(first_name="Marie", education_level="lycée", grade="Terminale",
                      enrolled_subjects=["Maths", "Physique"], objectives=["Bac C"])
    prompt = build_system_prompt(ctx)
    assert "Tu t'adresses à Marie." in prompt
    assert "Niveau : lycée, Terminale." in prompt
    assert "Matières concernées : Maths, Physique." in prompt
    assert "Objectifs déclarés : Bac C." in prompt


def test_empty_profile_has_no_profile_lines():
    prompt = build_system_prompt(UserContext())
    assert "Tu t'adresses à" not in prompt
    assert "Niveau :" not in prompt
    assert "Matières concernées" not in prompt
    assert "Objectifs déclarés" not in prompt


def test_level_line_with_only_grade():
    assert "Niveau : L1." in build_system_prompt(UserContext(grade="L1"))


def test_performance_history_is_formatted_with_one_decimal():
    prompt = build_system_prompt(UserContext(performance_history={"Maths": 14.5, "Physique": 11.0}))
    assert "Historique de performance :" in prompt
    assert "  - Maths : 14.5/20" in prompt
    assert "  - Physique : 11.0/20" in prompt


@pytest.mark.parametrize("style,marker", [
    ("visual", "apprenant VISUEL"),
    ("auditory", "apprenant AUDITIF"),
    ("reading_writing", "apprenant LECTEUR/SCRIPTEUR"),
    ("kinesthetic", "apprenant KINESTHÉSIQUE"),
])
def test_known_learning_styles_add_vark_instructions(style, marker):
    prompt = build_system_prompt(UserContext(learning_style=style))
    assert "[Style d'apprentissage détecté]" in prompt
    assert marker in prompt


def test_unknown_learning_style_is_named_plainly():
    prompt = build_system_prompt(UserContext(learning_style="mixte"))
    assert "Style d'apprentissage : mixte." in prompt
    assert "[Style d'apprentissage détecté]" not in prompt


# ── mémoire WinAI ───────────────────────────────────────────────────────────

def test_ai_memories_use_french_labels_and_keep_unknown_types():
    ctx = UserContext(ai_memories=[
        {"type": "struggling_topics", "content": "les dérivées"},
        {"type": "inconnu", "content": "autre"},
    ])
    prompt = build_system_prompt(ctx)
    assert "[Ce que WinAI sait déjà de toi]" in prompt
    assert "[Difficultés identifiées] les dérivées" in prompt
    assert "[inconnu] autre" in prompt


def test_no_memories_no_block():
    assert "[Ce que WinAI sait déjà de toi]" not in build_system_prompt(UserContext())


# ── lacunes de quiz ─────────────────────────────────────────────────────────

def test_mistakes_block_empty():
    assert _mistakes_block(UserContext()) == ""


def test_mistakes_block_limits_to_eight_and_truncates_question():
    mistakes = [{"subject": f"M{i}", "question": "q" * 300, "given_answer": "a", "correct_answer": "b"}
                for i in range(12)]
    block = _mistakes_block(UserContext(quiz_mistakes=mistakes))
    assert block.count("| Répondu : a | Correct : b") == 8
    assert "M7" in block and "M8" not in block
    assert "q" * 120 in block and "q" * 121 not in block


def test_mistakes_block_defaults_for_missing_fields():
    block = _mistakes_block(UserContext(quiz_mistakes=[{"question": "2+2 ?"}]))
    assert "[Général]" in block
    assert "Répondu : sans réponse" in block
    assert "Correct : ?" in block


# ── contexte de session ─────────────────────────────────────────────────────

def test_session_context_empty_when_nothing_known():
    assert _session_context_block(UserContext()) == ""


def test_session_context_uses_last_navigation_title_then_path():
    ctx = UserContext(navigation_history=[{"path": "/a", "title": "Premier"},
                                          {"path": "/subjects/1", "title": "Trigonométrie"}])
    assert "Page consultée : Trigonométrie" in _session_context_block(ctx)
    ctx2 = UserContext(navigation_history=[{"path": "/subjects/9"}])
    assert "Page consultée : /subjects/9" in _session_context_block(ctx2)


def test_session_context_last_activity_with_score():
    ctx = UserContext(recent_activity=[{"type": "quiz", "subjectTitle": "Maths", "score": 58}])
    block = _session_context_block(ctx)
    assert "Dernière activité : quiz Maths" in block
    assert "58/100" in block


# ── enfants suivis (prompt parent) ──────────────────────────────────────────

def test_children_block_empty():
    assert _children_block(UserContext(role="parent")) == ""


def test_children_block_score_and_rising_trend():
    ctx = UserContext(role="parent", children_data=[
        {"name": "Marie", "level": "Terminale", "avg_score": 14.5, "avg_score_trend": 1.5,
         "subjects": ["Maths", "Physique", "SVT", "Histoire", "Anglais"]}])
    block = _children_block(ctx)
    assert "[Enfants suivis]" in block
    assert "Marie" in block and "niveau Terminale" in block
    assert "score moyen 14.5/20 (en hausse de 1.5 pt)" in block
    # seules 4 matières sont listées
    assert "Histoire" in block and "Anglais" not in block


def test_children_block_falling_trend_and_zero_trend():
    falling = _children_block(UserContext(children_data=[{"name": "A", "avg_score": 10.0, "avg_score_trend": -2.0}]))
    assert "en baisse de 2.0 pt" in falling
    flat = _children_block(UserContext(children_data=[{"name": "A", "avg_score": 10.0, "avg_score_trend": 0}]))
    assert "score moyen 10.0/20" in flat and "hausse" not in flat and "baisse" not in flat


def test_children_block_homework_gaps_bulletin_and_exam_watch():
    child = {
        "name": "Paul",
        "quiz_gaps": ["fractions", "angles", "racines", "vecteurs"],
        "late_homework": [{"title": "DM1"}, {"title": "DM2"}],
        "pending_homework": [{"title": "Exposé"}],
        "bulletin_extract": {"average_grade": 12.5, "school_year": "2025-2026"},
        "exam_watch": {"exam_type": "BEPC", "exam_date": "2026-06-10"},
    }
    block = _children_block(UserContext(children_data=[child]))
    assert "lacunes récentes : fractions, angles, racines" in block
    assert "vecteurs" not in block
    assert "devoirs EN RETARD : DM1, DM2" in block
    assert "devoirs à venir : Exposé" in block
    assert "moyenne de bulletin 2025-2026 : 12.5/20" in block
    assert "en veille d'examen (BEPC, le 2026-06-10)" in block


def test_children_block_ignores_incomplete_bulletin_and_exam_watch():
    child = {"name": "Paul", "bulletin_extract": {"average_grade": None, "school_year": "2025"},
             "exam_watch": {"exam_type": "BEPC"}}
    block = _children_block(UserContext(children_data=[child]))
    assert "bulletin" not in block
    assert "veille d'examen" not in block


def test_parent_prompt_embeds_children_block():
    prompt = build_system_prompt(UserContext(role="parent", children_data=[{"name": "Marie"}]))
    assert "[Enfants suivis]" in prompt and "Marie" in prompt


# ── langue forcée ───────────────────────────────────────────────────────────

def test_french_or_missing_language_adds_no_instruction():
    assert "[Langue]" not in build_system_prompt(UserContext(language="french"))
    assert "[Langue]" not in build_system_prompt(UserContext())


def test_english_and_pidgin_instructions():
    english = build_system_prompt(UserContext(language="english"))
    assert "[Langue] L'utilisateur s'exprime en anglais" in english
    pidgin = build_system_prompt(UserContext(language="pidgin"))
    assert "pidgin" in pidgin and "français standard" in pidgin


def test_unknown_language_adds_no_instruction():
    assert "[Langue]" not in build_system_prompt(UserContext(language="klingon"))
