"""
WinAI  Endpoints complémentaires pour le compte Parent.

- GET  /parent/children-insights         → Comparaison inter-enfants (insights)

Le score de mobilisation parentale ("engagement_score") et le ROI éducatif
("educational-roi", qui affichait un montant FCFA en regard des résultats
d'un enfant) ont été retirés : le premier jugeait le parent sur son usage du
produit plutôt que sur son implication réelle, le second marchandisait la
progression d'un enfant en termes financiers. Voir parent_decisions_session.md
(corrections 2 et 3). Remplacés côté parent par des rappels factuels
(ParentReminders côté frontend), sans score ni montant associé à un enfant.
"""

import json
import logging
from datetime import datetime, timedelta, timezone
from typing import List

from fastapi import APIRouter, Depends, HTTPException, Query
from pydantic import BaseModel

# Module 20 : même absence de contrôle de rôle que côté professeur. Cet
# endpoint compare les résultats des enfants d'un parent : il est réservé au
# rôle parent (l'administrateur reste autorisé pour le support).
from auth import verify_token, require_role, UserTokenData
from database import Database, QuizAttempt, DailyScore, User, ParentStudentLink
from services.deepseek_client import get_deepseek_client
from services.parent_child_context import compute_child_context

logger = logging.getLogger(__name__)

parent_extra_router = APIRouter()


def _assert_can_access_child(session, current_user: UserTokenData, child_id: int) -> None:
    """Même garde que parent_alert_routes.py : sans ça, n'importe quel
    utilisateur authentifié pouvait lire le résumé d'activité/score de
    n'importe quel enfant en devinant son id dans child_ids."""
    if current_user.user_id == child_id:
        return
    linked = session.query(ParentStudentLink).filter(
        ParentStudentLink.ParentId == current_user.user_id,
        ParentStudentLink.StudentId == child_id,
        ParentStudentLink.Status == 'accepted',
    ).first()
    if linked is None:
        raise HTTPException(status_code=403, detail="Cet enfant n'est pas lié à votre compte.")


# ── Feature 6  Comparaison inter-enfants (insights) ─────────────────────────

class ChildrenInsightsResponse(BaseModel):
    insights: List[str]
    children_summary: List[dict]


@parent_extra_router.get("/parent/children-insights", response_model=ChildrenInsightsResponse)
async def get_children_insights(
    child_ids: str = Query(..., description="Comma-separated child user IDs"),
    current_user: UserTokenData = Depends(require_role("parent", "admin")),
):
    """
    Comparaison bienveillante inter-enfants  jamais un classement, toujours des insights.
    Retourne des observations actionnables pour chaque enfant.
    """
    ids = [int(x.strip()) for x in child_ids.split(",") if x.strip().isdigit()]
    if len(ids) < 2:
        return {"insights": [], "children_summary": []}

    db = Database()
    session = db.SessionLocal()
    now = datetime.now(timezone.utc)
    cutoff_30 = now - timedelta(days=30)

    try:
        children_data = []

        for child_id in ids:
            _assert_can_access_child(session, current_user, child_id)

            # Module 13 (lot 6) : le score moyen vient désormais de la même
            # base de contexte partagée que le chat et le rapport
            # hebdomadaire (services/parent_child_context), au lieu d'un
            # recalcul local sur une fenêtre identique mais indépendante  un
            # même score moyen doit afficher la même valeur dans les trois.
            ctx = compute_child_context(child_id, session=session)
            child_name = ctx["name"] if ctx else f"Enfant {child_id}"
            avg_score_20 = ctx["avg_score"] if ctx and ctx.get("avg_score") is not None else 0.0
            # Cet endpoint affichait jusqu'ici le score sur 100 (moyenne brute
            # de DailyScore.AverageScore) ; le contexte partagé le renvoie sur
            # 20 (convention du reste de l'app)  on revient à /100 ici pour
            # ne pas changer la forme de réponse déjà consommée par le
            # frontend (children_summary.avg_score), conformément à la règle
            # du Module 15 "n'ajoute pas, ne renomme pas sans vérifier les
            # consommateurs".
            avg_score = avg_score_20 * 100 / 20 if avg_score_20 else 0.0

            attempts = (
                session.query(QuizAttempt)
                .filter(
                    QuizAttempt.UserId == child_id,
                    QuizAttempt.CompletedAt >= cutoff_30,
                )
                .count()
            )
            scores_30d = (
                session.query(DailyScore.Date)
                .filter(DailyScore.UserId == child_id, DailyScore.CreatedAt >= cutoff_30)
                .all()
            )
            active_days = len({d for (d,) in scores_30d})

            children_data.append({
                "child_id": child_id,
                "child_name": child_name,
                "avg_score": round(avg_score, 1),
                "quiz_count": attempts,
                "active_days": active_days,
                # Module 16 (lot 6) : enrichi seulement après que la base de
                # contexte partagée du Module 13 soit en place (ctx ci-dessus),
                # pour ne pas reconstruire une seconde collecte de signaux.
                # Avant, cet endpoint se limitait au score moyen et au nombre
                # de quiz.
                "late_homework_count": len(ctx["late_homework"]) if ctx else 0,
                "quiz_gaps": ctx["quiz_gaps"] if ctx else [],
            })

        # Generate bienveillant insights with DeepSeek
        summary = "; ".join(
            f"{c['child_name']} (score moy. {c['avg_score']}%, {c['quiz_count']} quiz, "
            f"{c['active_days']} jours actifs ce mois, {c['late_homework_count']} devoir(s) en retard, "
            f"lacunes : {', '.join(c['quiz_gaps']) or 'aucune'})"
            for c in children_data
        )
        try:
            deepseek = get_deepseek_client()
            result = deepseek.chat(
                messages=[{
                    "role": "user",
                    "content": (
                        f"Voici les données du mois pour les enfants d'un parent : {summary}. "
                        "Génère 2-3 observations bienveillantes et actionnables pour ce parent. "
                        "Ne classe JAMAIS les enfants. Valorise les points forts de chacun. "
                        "Suggère comment le parent peut aider chaque enfant à progresser. "
                        "Format : liste JSON [\"Observation 1\", \"Observation 2\", ...]"
                    ),
                }],
                system_prompt=(
                    "Tu es WinAI, conseiller pédagogique familial bienveillant. "
                    "Réponds en français avec un JSON valide uniquement."
                ),
                max_tokens=200,
                temperature=0.7,
            )
            content = result.get("content", "[]").strip()
            if content.startswith("```"):
                content = "\n".join(content.split("\n")[1:])
            if content.endswith("```"):
                content = content.rsplit("```", 1)[0].strip()
            insights = json.loads(content)
            if not isinstance(insights, list):
                raise ValueError("Not a list")
        except Exception:
            # Fallback: generate simple insights
            insights = []
            most_active = max(children_data, key=lambda c: c["quiz_count"])
            most_consistent = max(children_data, key=lambda c: c["active_days"])
            insights.append(
                f"{most_active['child_name']} est particulièrement assidu(e) avec {most_active['quiz_count']} exercices ce mois  félicitez-le/la !"
            )
            if most_consistent["child_id"] != most_active["child_id"]:
                insights.append(
                    f"{most_consistent['child_name']} est très régulier(e) : {most_consistent['active_days']} jours actifs. La régularité est la clé du succès."
                )
            for c in children_data:
                if c["quiz_count"] < 3:
                    insights.append(
                        f"{c['child_name']} pourrait bénéficier d'un petit coup de pouce  proposez-lui une session de 15 minutes ensemble."
                    )

        return {
            "insights": insights[:4],
            "children_summary": children_data,
        }

    except HTTPException:
        raise  # ex. le 403 de _assert_can_access_child  ne doit pas devenir un 500 générique ci-dessous.
    except Exception as e:
        logger.error(f"Error computing children insights: {e}")
        raise HTTPException(status_code=500, detail="Insights computation failed")
    finally:
        session.close()
