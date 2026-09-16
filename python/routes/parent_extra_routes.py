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

from auth import verify_token, UserTokenData
from database import Database, QuizAttempt, DailyScore, User
from services.deepseek_client import get_deepseek_client

logger = logging.getLogger(__name__)

parent_extra_router = APIRouter()


# ── Feature 6  Comparaison inter-enfants (insights) ─────────────────────────

class ChildrenInsightsResponse(BaseModel):
    insights: List[str]
    children_summary: List[dict]


@parent_extra_router.get("/parent/children-insights", response_model=ChildrenInsightsResponse)
async def get_children_insights(
    child_ids: str = Query(..., description="Comma-separated child user IDs"),
    current_user: UserTokenData = Depends(verify_token),
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
            child_user = session.query(User).filter(User.Id == child_id).first()
            child_name = child_user.FirstName or f"Enfant {child_id}" if child_user else f"Enfant {child_id}"

            scores = (
                session.query(DailyScore)
                .filter(
                    DailyScore.UserId == child_id,
                    DailyScore.CreatedAt >= cutoff_30,
                )
                .all()
            )
            attempts = (
                session.query(QuizAttempt)
                .filter(
                    QuizAttempt.UserId == child_id,
                    QuizAttempt.CompletedAt >= cutoff_30,
                )
                .count()
            )

            avg_score = (
                sum(float(s.AverageScore) for s in scores) / len(scores)
                if scores else 0.0
            )

            children_data.append({
                "child_id": child_id,
                "child_name": child_name,
                "avg_score": round(avg_score, 1),
                "quiz_count": attempts,
                "active_days": len(set(s.Date for s in scores)),
            })

        # Generate bienveillant insights with DeepSeek
        summary = "; ".join(
            f"{c['child_name']} (score moy. {c['avg_score']}%, {c['quiz_count']} quiz, "
            f"{c['active_days']} jours actifs ce mois)"
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

    except Exception as e:
        logger.error(f"Error computing children insights: {e}")
        raise HTTPException(status_code=500, detail="Insights computation failed")
    finally:
        session.close()
