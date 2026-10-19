"""Lot 10.6 : textes de secours de WinAI dans la langue du compte.

Quand le modèle n'est pas joignable, les suggestions d'objectifs et le guide
d'entretien de vérification sont construits à partir de modèles fixes. Ils
suivent la langue du profil (en-tête X-User-Locale) ; le français reste le
texte historique, et les chiffres ne dépendent pas de la langue.
"""

import os

# auth.py refuse de se charger sans secret : valeur de test, jamais utilisée pour signer.
os.environ.setdefault("JWT_SECRET_KEY", "test-only-secret-for-language-fallbacks-0123456789")

from services.language import begin_request_language, end_request_language  # noqa: E402
from routes.goal_suggestions import GoalSuggestionRequest, _rule_based
from routes.tutor_interview_routes import (
    GuideRequest,
    ReportRequest,
    _template_guide,
    _template_report,
)


def _in(lang, fn, *args):
    token = begin_request_language(lang)
    try:
        return fn(*args)
    finally:
        end_request_language(token)


def _goal_request():
    return GoalSuggestionRequest(level="Terminale C", weak_subjects=["Physique", "Chimie"], done_this_week={"quiz": 0})


def test_goal_fallback_french_unchanged():
    res = _rule_based(_goal_request())
    assert res.suggestions[0].title == "Rythme examen"
    assert res.suggestions[1].reason == "Concentré sur Physique et Chimie, vos matières les plus fragiles."
    assert res.insight.startswith("Vos résultats les plus faibles sont en Physique, Chimie.")


def test_goal_fallback_english_same_figures():
    fr = _rule_based(_goal_request())
    en = _in("en", _rule_based, _goal_request())
    assert en.suggestions[0].title == "Exam pace"
    assert en.suggestions[1].reason == "Focused on Physique and Chimie, your weakest subjects."
    assert en.suggestions[2].title == "Gentle restart"
    assert en.insight.startswith("Your weakest results are in Physique, Chimie.")
    for a, b in zip(fr.suggestions, en.suggestions):
        assert (a.studyHoursTarget, a.quizTarget, a.downloadsTarget) == (b.studyHoursTarget, b.quizTarget, b.downloadsTarget)


def test_interview_guide_fallback_follows_language():
    body = GuideRequest(subjects=["Mathématiques"], levels=["Terminale"], format="video")
    fr = _template_guide(body)
    en = _in("en", _template_guide, body)
    assert fr.sections[0].title == "Questions de fond"
    assert fr.intro.startswith("Entretien en visio")
    assert en.sections[0].title == "Subject questions"
    assert en.intro.startswith("Interview by video")
    assert [c.key for c in en.grid] == [c.key for c in fr.grid]
    assert [c.weight for c in en.grid] == [c.weight for c in fr.grid]
    assert "Safety with minors" in en.pass_rule
    assert all(q.question for s in en.sections for q in s.questions)


def test_interview_report_fallback_follows_language():
    body = ReportRequest(subjects=["Mathématiques"], levels=["Terminale"], notes="RAS",
                         scores={"mastery": 4, "pedagogy": 4, "communication": 4, "safety": 5, "reliability": 4})
    fr = _template_report(body)
    en = _in("en", _template_report, body)
    assert fr.report.startswith("Entretien en visio")
    assert en.report.startswith("Interview by video")
    assert "Weighted average" in en.report
    assert en.recommendation == fr.recommendation == "pass"
