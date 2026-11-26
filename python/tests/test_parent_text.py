"""Rédaction des messages aux parents (services/parent_text.py)."""

from services.parent_text import tidy_parent_text, PARENT_STYLE


def test_formule_d_appel_et_tiret_long_retires():
    t = tidy_parent_text("Bonjour, nous avons remarqué que Gaël n’a pas étudié — un petit mot pourrait l’aider. Cordialement.")
    assert t == "Nous avons remarqué que Gaël n'a pas étudié, un petit mot pourrait l'aider."


def test_cher_parent_et_limite_de_phrases():
    t = tidy_parent_text("Cher parent, Awa progresse bien. Ses quiz sont meilleurs. Continuez ainsi. Bravo !")
    assert t == "Awa progresse bien. Ses quiz sont meilleurs."


def test_double_espace_reduit():
    assert tidy_parent_text("Les scores ont baissé cette semaine  un encouragement aide.") == \
        "Les scores ont baissé cette semaine un encouragement aide."


def test_vide_et_consigne():
    assert tidy_parent_text(None) == ""
    assert "vouvoie" in PARENT_STYLE and "Bonjour" in PARENT_STYLE
