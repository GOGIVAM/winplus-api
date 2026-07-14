"""
WinAI — Alertes parent basées sur la détection d'anomalies réelle.

GET /api/parent-alerts/{child_id}
  — Analyse les scores des 14 derniers jours (QuizAttempts)
  — Détecte : baisse de performance, inactivité, excellente semaine
  — Génère un message WinAI court via DeepSeek (max_tokens=120)
  — Retourne { alerts: [{ type, severity, message, detected_at, child_stats }] }
"""

import logging
from datetime import datetime, timedelta, timezone
from typing import List, Optional

from fastapi import APIRouter, Depends, HTTPException, status
from pydantic import BaseModel

from auth import verify_token, UserTokenData
from database import Database, QuizAttempt, User
from services.deepseek_client import get_deepseek_client

logger = logging.getLogger(__name__)

parent_alert_router = APIRouter()


class AlertItem(BaseModel):
    type: str          # "performance_drop" | "inactivity" | "excellent_week"
    severity: str      # "error" | "warn" | "info" | "success"
    message: str
    detected_at: str
    child_stats: dict


class ParentAlertsResponse(BaseModel):
    alerts: List[AlertItem]


def _winai_message(alert_type: str, child_name: str, stat_value: float | int | None, deepseek) -> str:
    """Génère un message WinAI court (1-2 phrases) via DeepSeek."""
    prompts = {
        "performance_drop": (
            f"L'élève {child_name} a vu ses scores baisser de {stat_value:.0f}% cette semaine par rapport à la semaine précédente. "
            "Génère un message bienveillant de 1-2 phrases pour informer le parent et l'encourager à soutenir son enfant."
        ),
        "inactivity": (
            f"{child_name} n'a pas eu d'activité depuis {stat_value} jours. "
            "Génère un message bienveillant de 1-2 phrases pour alerter le parent, avec empathie et encouragement à reprendre l'apprentissage."
        ),
        "excellent_week": (
            f"{child_name} a obtenu une excellente performance cette semaine (score moyen {stat_value:.0f}%). "
            "Génère un message chaleureux de 1-2 phrases pour féliciter et encourager le parent à transmettre cette fierté à l'enfant."
        ),
    }
    prompt = prompts.get(alert_type, f"Génère un message bienveillant court pour le parent de {child_name}.")
    try:
        result = deepseek.chat(
            messages=[{"role": "user", "content": prompt}],
            system_prompt=(
                "Tu es WinAI, assistant pédagogique bienveillant de WinPlus. "
                "Tu t'adresses aux parents avec empathie, chaleur et encouragement. "
                "Réponds toujours en français, en 1-2 phrases concises."
            ),
            max_tokens=120,
            temperature=0.7,
        )
        return result.get("content", "").strip() or _fallback_message(alert_type, child_name)
    except Exception as e:
        logger.warning(f"DeepSeek WinAI message failed: {e}")
        return _fallback_message(alert_type, child_name)


def _fallback_message(alert_type: str, child_name: str) -> str:
    fallbacks = {
        "performance_drop": f"Les scores de {child_name} ont baissé cette semaine — un petit encouragement peut faire toute la différence !",
        "inactivity":       f"{child_name} n'a pas étudié depuis quelques jours. Un message de votre part pourrait l'aider à reprendre.",
        "excellent_week":   f"Bravo à {child_name} pour cette excellente semaine ! Partagez-lui votre fierté.",
    }
    return fallbacks.get(alert_type, f"WinAI a détecté quelque chose à surveiller pour {child_name}.")


@parent_alert_router.get("/{child_id}", response_model=ParentAlertsResponse)
async def get_parent_alerts(
    child_id: int,
    current_user: UserTokenData = Depends(verify_token),
):
    """
    Détecte des anomalies dans les 14 derniers jours d'activité de l'enfant.
    Génère un message WinAI bienveillant pour chaque alerte.
    """
    db = Database()
    session = db.SessionLocal()
    alerts: list[dict] = []

    try:
        # Récupérer le prénom de l'enfant
        child_user = session.query(User).filter(User.Id == child_id).first()
        child_name = child_user.FirstName or f"L'élève" if child_user else "L'élève"

        now = datetime.now(timezone.utc)
        cutoff_14 = now - timedelta(days=14)
        cutoff_7  = now - timedelta(days=7)

        # Récupérer les tentatives des 14 derniers jours
        attempts = (
            session.query(QuizAttempt)
            .filter(
                QuizAttempt.UserId == child_id,
                QuizAttempt.CompletedAt >= cutoff_14,
            )
            .order_by(QuizAttempt.CompletedAt)
            .all()
        )

        scores_last_7  = [float(a.Score) for a in attempts if a.CompletedAt and a.CompletedAt >= cutoff_7  and a.Score is not None]
        scores_prev_7  = [float(a.Score) for a in attempts if a.CompletedAt and a.CompletedAt <  cutoff_7  and a.Score is not None]

        # ── Détection 1 : baisse de performance ────────────────────────────────
        if scores_last_7 and scores_prev_7:
            avg_last = sum(scores_last_7) / len(scores_last_7)
            avg_prev = sum(scores_prev_7) / len(scores_prev_7)
            if avg_prev > 0:
                delta_pct = (avg_prev - avg_last) / avg_prev * 100
                if delta_pct >= 15:
                    deepseek = get_deepseek_client()
                    msg = _winai_message("performance_drop", child_name, delta_pct, deepseek)
                    alerts.append({
                        "type":         "performance_drop",
                        "severity":     "warn",
                        "message":      msg,
                        "detected_at":  now.isoformat(),
                        "child_stats":  {
                            "avg_score_last_7d": round(avg_last, 1),
                            "avg_score_prev_7d": round(avg_prev, 1),
                            "delta_pct":         round(delta_pct, 1),
                        },
                    })

        # ── Détection 2 : inactivité prolongée ─────────────────────────────────
        all_dates = [a.CompletedAt for a in attempts if a.CompletedAt]
        if all_dates:
            last_activity = max(all_dates)
            inactivity_days = (now - last_activity).days
        else:
            inactivity_days = 14

        if inactivity_days > 4:
            deepseek = get_deepseek_client()
            msg = _winai_message("inactivity", child_name, inactivity_days, deepseek)
            alerts.append({
                "type":        "inactivity",
                "severity":    "error" if inactivity_days > 7 else "warn",
                "message":     msg,
                "detected_at": now.isoformat(),
                "child_stats": {"inactivity_days": inactivity_days},
            })

        # ── Détection 3 : excellente performance ───────────────────────────────
        if scores_last_7 and not any(a["type"] == "performance_drop" for a in alerts):
            avg_last = sum(scores_last_7) / len(scores_last_7)
            if avg_last >= 85:
                deepseek = get_deepseek_client()
                msg = _winai_message("excellent_week", child_name, avg_last, deepseek)
                alerts.append({
                    "type":        "excellent_week",
                    "severity":    "success",
                    "message":     msg,
                    "detected_at": now.isoformat(),
                    "child_stats": {
                        "avg_score_last_7d": round(avg_last, 1),
                        "quiz_count":        len(scores_last_7),
                    },
                })

    except Exception as e:
        logger.error(f"Error computing parent alerts for child {child_id}: {e}")
        raise HTTPException(status_code=500, detail="Alert computation failed")
    finally:
        session.close()

    return {"alerts": alerts}
