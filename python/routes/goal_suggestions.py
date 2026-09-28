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

Enregistrement dans app.py (Module 23 : ce routeur n'avait jamais été monté,
l'appel .NET recevait donc 404 et retombait toujours sur le calcul local) :

    from routes import goal_suggestions
    app.include_router(goal_suggestions.router)
"""

import asyncio
import json
import logging
import re
from typing import Any, Dict, List, Optional

from fastapi import APIRouter, Depends
from pydantic import BaseModel

from auth import verify_token, UserTokenData
# Module 23 : l'import visait « services.deepseek_service.chat_completion »,
# module inexistant. L'ImportError était avalée par un « except Exception:
# pass » : même monté, le routeur n'aurait jamais appelé le modèle. L'import
# est désormais fait au chargement du module, pour qu'une erreur de ce type
# fasse échouer le démarrage au lieu de disparaître silencieusement.
from services.deepseek_client import get_deepseek_client

logger = logging.getLogger(__name__)

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
async def goal_suggestions(
    req: GoalSuggestionRequest,
    # Authentifiée comme les autres routes IA : une fois montée, une route
    # publique aurait ouvert un appel LLM payant à n'importe qui. Le contrôleur
    # .NET relaie le jeton de l'élève connecté.
    current_user: UserTokenData = Depends(verify_token),
) -> GoalSuggestionResponse:
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
        prompt = (
            "Réécris chaque justification en une phrase courte, adressée à l'élève, "
            "en français, ton direct et bienveillant. Ne change aucun chiffre. "
            "Réponds par une liste JSON de trois chaînes, sans autre texte.\n\n"
            f"Niveau : {req.level or 'non précisé'}\n"
            f"Matières fragiles : {', '.join(req.weak_subjects) or 'aucune'}\n"
            + "\n".join(
                f"{i + 1}. {s.title}  {s.reason} "
                f"({s.studyHoursTarget} h, {s.quizTarget} quiz, {s.downloadsTarget} épreuves)"
                for i, s in enumerate(base.suggestions)
            )
        )

        # DeepSeekClient.chat est synchrone : exécuté hors de la boucle asyncio.
        result = await asyncio.to_thread(
            get_deepseek_client().chat,
            messages=[{"role": "user", "content": prompt}],
            max_tokens=300,
        )
        if not result.get("success"):
            logger.warning(f"[goal-suggestions] génération WinAI en échec : {result.get('error')}  base déterministe conservée")
            return base

        raw = result.get("content") or ""
        match = re.search(r"\[.*\]", raw, re.S)
        if not match:
            logger.warning("[goal-suggestions] réponse WinAI sans liste JSON  base déterministe conservée")
            return base
        reasons = json.loads(match.group(0))
        for suggestion, reason in zip(base.suggestions, reasons):
            if isinstance(reason, str) and 10 < len(reason) < 240:
                suggestion.reason = reason.strip()
    except (json.JSONDecodeError, TypeError, ValueError) as e:
        # Réponse du modèle non exploitable : les chiffres déterministes
        # restent valides, seule la reformulation est perdue. Journalisé.
        logger.warning(f"[goal-suggestions] réponse WinAI illisible ({e})  base déterministe conservée")

    return base
