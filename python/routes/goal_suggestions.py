"""
Suggestions d'objectifs hebdomadaires générées par WinAI.

Appelé par le backend .NET :
    POST /api/ai/goal-suggestions
    {
      "user_id": 38,
      "level": "Terminale C",
      "weak_subjects": ["Mathématiques", "Physique"],
      "done_this_week": {"quiz": 2, "downloads": 1, "studyHours": null}
    }

Réponse :
    {
      "suggestions": [
        {"title": "...", "reason": "...", "studyHoursTarget": 12,
         "quizTarget": 5, "downloadsTarget": 4}
      ],
      "insight": "une phrase de contexte"
    }

Le backend .NET fonctionne sans cette route : il possède un repli déterministe
et marque alors la réponse `aiPowered: false`. Cette route sert à passer les
suggestions en langage naturel, calibrées par le modèle.

Enregistrement dans app.py :

    from routes import goal_suggestions
    app.include_router(goal_suggestions.router)
"""

from typing import Any, Dict, List, Optional

from fastapi import APIRouter
from pydantic import BaseModel

router = APIRouter(prefix="/api/ai", tags=["ai"])


class GoalSuggestionRequest(BaseModel):
    user_id: Optional[int] = None
    level: Optional[str] = None
    weak_subjects: List[str] = []
    done_this_week: Dict[str, Any] = {}


class GoalSuggestion(BaseModel):
    title: str
    reason: str
    studyHoursTarget: int
    quizTarget: int
    downloadsTarget: int


class GoalSuggestionResponse(BaseModel):
    suggestions: List[GoalSuggestion]
    insight: str


EXAM_MARKERS = ("terminale", "tle", "3e", "3ème", "3eme", "bac", "bepc", "concours")


def _is_exam_year(level: Optional[str]) -> bool:
    return bool(level) and any(m in level.lower() for m in EXAM_MARKERS)


def _rule_based(req: GoalSuggestionRequest) -> GoalSuggestionResponse:
    """Base déterministe : toujours renvoyée si le modèle n'est pas joignable."""
    exam = _is_exam_year(req.level)
    weak = [s for s in req.weak_subjects if s][:3]
    done_quiz = int(req.done_this_week.get("quiz") or 0)

    suggestions = [
        GoalSuggestion(
            title="Rythme examen" if exam else "Rythme régulier",
            reason=(
                "Année d'examen : volume soutenu et révisions quotidiennes."
                if exam
                else "Un socle tenable sur toute l'année, sans surcharge."
            ),
            studyHoursTarget=12 if exam else 7,
            quizTarget=5 if exam else 3,
            downloadsTarget=4 if exam else 2,
        ),
        GoalSuggestion(
            title="Rattrapage ciblé" if weak else "Consolidation",
            reason=(
                "Concentré sur " + " et ".join(weak[:2]) + ", vos matières les plus fragiles."
                if weak
                else "Entretenir les acquis avec deux séances de quiz par semaine."
            ),
            studyHoursTarget=10 if weak else 5,
            quizTarget=6 if weak else 2,
            downloadsTarget=3,
        ),
        GoalSuggestion(
            title="Reprise en douceur" if done_quiz == 0 else "Palier suivant",
            reason=(
                "Aucun quiz cette semaine : repartir petit vaut mieux que ne pas repartir."
                if done_quiz == 0
                else f"{done_quiz} quiz déjà passés cette semaine : monter d'un cran reste atteignable."
            ),
            studyHoursTarget=3 if done_quiz == 0 else 9,
            quizTarget=1 if done_quiz == 0 else done_quiz + 2,
            downloadsTarget=1 if done_quiz == 0 else 3,
        ),
    ]

    if weak:
        insight = (
            "Vos résultats les plus faibles sont en "
            + ", ".join(weak)
            + ". Un objectif qui y consacre deux séances par semaine est le plus rentable."
        )
    else:
        insight = "Aucune matière en difficulté marquée : un objectif de maintien suffit cette semaine."

    return GoalSuggestionResponse(suggestions=suggestions, insight=insight)


@router.post("/goal-suggestions", response_model=GoalSuggestionResponse)
async def goal_suggestions(req: GoalSuggestionRequest) -> GoalSuggestionResponse:
    """
    Renvoie trois objectifs proposés.

    La base est déterministe (chiffres cohérents, jamais absurdes). Si un client
    DeepSeek est disponible dans le projet, il ne sert qu'à réécrire les
    justifications en langage naturel : les cibles chiffrées restent celles
    calculées ici, pour qu'une hallucination ne produise pas un objectif
    intenable.
    """
    base = _rule_based(req)

    try:
        from services.deepseek_service import chat_completion  # type: ignore

        prompt = (
            "Réécris chaque justification en une phrase courte, adressée à l'élève, "
            "en français, ton direct et bienveillant. Ne change aucun chiffre. "
            "Réponds par une liste JSON de trois chaînes, sans autre texte.\n\n"
            f"Niveau : {req.level or 'non précisé'}\n"
            f"Matières fragiles : {', '.join(req.weak_subjects) or 'aucune'}\n"
            + "\n".join(
                f"{i + 1}. {s.title} — {s.reason} "
                f"({s.studyHoursTarget} h, {s.quizTarget} quiz, {s.downloadsTarget} épreuves)"
                for i, s in enumerate(base.suggestions)
            )
        )

        raw = await chat_completion(prompt, max_tokens=300)

        import json
        import re

        match = re.search(r"\[.*\]", raw or "", re.S)
        if match:
            reasons = json.loads(match.group(0))
            for suggestion, reason in zip(base.suggestions, reasons):
                if isinstance(reason, str) and 10 < len(reason) < 240:
                    suggestion.reason = reason.strip()
    except Exception:
        # Modèle absent, quota épuisé, réponse non parsable : on garde la base.
        pass

    return base
