"""
WinAI  Alertes parent basées sur la détection d'anomalies réelle.

GET /api/parent-alerts/{child_id}
   Analyse les scores des 14 derniers jours (QuizAttempts)
   Détecte : baisse de performance, inactivité, excellente semaine
   Génère un message WinAI court via DeepSeek (max_tokens=120)
   Retourne { alerts: [{ type, severity, message, detected_at, child_stats }] }
   Persiste aussi chaque alerte dans ParentAlerts (table .NET) pour lui donner
   un historique consultable  voir _persist_alerts. Le baromètre (vue
   consolidée des signaux comportementaux) lit cet historique côté .NET
   (ParentAlertController.GetBarometre) plutôt que de dupliquer un lecteur
   ici : ce module ne fait qu'écrire.

Vocabulaire : jamais de terme clinique ("dépression", "trouble", "burnout",
"diagnostic"...) dans les messages générés  voir FORBIDDEN_TERMS et
_contains_forbidden_vocabulary, qui filtrent la sortie DeepSeek avant tout
retour au parent (le prompt le demande déjà, ce filtre est un filet de
sécurité, pas la seule protection).
"""

import logging
from datetime import datetime, timedelta, timezone
from typing import List, Optional

from fastapi import APIRouter, Depends, HTTPException, status
from pydantic import BaseModel

from auth import verify_token, UserTokenData
from database import Database, QuizAttempt, User, ExamCoachPlanAI, ParentAlertDB, ParentStudentLink
from services.deepseek_client import get_deepseek_client

logger = logging.getLogger(__name__)

parent_alert_router = APIRouter()


def _assert_can_access_child(session, current_user: UserTokenData, child_id: int) -> None:
    """
    Sans ce garde-fou, n'importe quel utilisateur authentifié pouvait lire (et
    faire persister à son propre nom) les alertes comportementales de
    n'importe quel enfant en devinant son id  cette route est la seule à
    réellement générer/écrire ces données, le contrôleur .NET qui vérifie le
    lien ne fait que les relire ensuite. Autorisé : l'enfant lui-même, ou un
    parent avec un ParentStudentLink 'accepted' vers cet enfant.
    """
    if current_user.user_id == child_id:
        return
    linked = session.query(ParentStudentLink).filter(
        ParentStudentLink.ParentId == current_user.user_id,
        ParentStudentLink.StudentId == child_id,
        ParentStudentLink.Status == 'accepted',
    ).first()
    if linked is None:
        raise HTTPException(status_code=status.HTTP_403_FORBIDDEN,
                             detail="Cet enfant n'est pas lié à votre compte.")

# Vocabulaire interdit dans tout texte destiné au parent (UI, IA, y compris
# commentaires/noms de variables du code qui décrivent ces alertes) — jamais
# de cadrage clinique/diagnostique pour un signal comportemental détecté par
# des heuristiques d'usage, pas par un professionnel de santé.
FORBIDDEN_TERMS = ["dépression", "anxiété diagnostiquée", "trouble", "burnout", "diagnostic"]

# Type Python (interne, utilisé par la détection) -> Type ParentAlert (table
# persistée, voir ParentAlert.cs côté .NET).
_ALERT_TYPE_TO_DB = {
    "performance_drop": "BaissePerformance",
    "inactivity": "Inactivite",
    "excellent_week": "Felicitations",
    "exam_anxiety": "AnxieteExamen",
    "surmenage": "Surmenage",
}
_SEVERITY_TO_DB = {"error": "High", "warn": "Medium", "info": "Low", "success": "Low"}


def _contains_forbidden_vocabulary(text: str) -> bool:
    lowered = text.lower()
    return any(term in lowered for term in FORBIDDEN_TERMS)


def _persist_alerts(session, parent_id: int, child_id: int, alerts: list[dict]) -> None:
    """
    Écrit chaque alerte calculée dans ParentAlerts, avec déduplication sur 24h
    (même parent/enfant/type) : cet endpoint est recalculé à chaque appel
    (dashboard rouvert plusieurs fois par jour), sans cette garde chaque
    consultation créerait une nouvelle ligne pour le même signal.
    """
    now = datetime.now(timezone.utc)
    dedup_cutoff = now - timedelta(hours=24)
    try:
        for a in alerts:
            db_type = _ALERT_TYPE_TO_DB.get(a["type"])
            if not db_type:
                continue

            already_persisted = (
                session.query(ParentAlertDB)
                .filter(
                    ParentAlertDB.ParentId == parent_id,
                    ParentAlertDB.ChildId == child_id,
                    ParentAlertDB.Type == db_type,
                    ParentAlertDB.CreatedAt >= dedup_cutoff,
                )
                .first()
            )
            if already_persisted:
                continue

            session.add(ParentAlertDB(
                ParentId=parent_id,
                ChildId=child_id,
                Type=db_type,
                Severity=_SEVERITY_TO_DB.get(a["severity"], "Low"),
                Content=a["message"],
                DetectedAt=now,
            ))
        session.commit()
    except Exception as e:
        logger.warning(f"Failed to persist parent alerts for child {child_id}: {e}")
        session.rollback()


class AlertItem(BaseModel):
    type: str          # "performance_drop" | "inactivity" | "excellent_week" | "exam_anxiety" | "surmenage"
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
        "exam_anxiety": (
            f"{child_name} a passé {int(stat_value) if stat_value else 'plusieurs'} séances de révision après 22h cette semaine, signe d'une anxiété possible à l'approche d'un examen. "
            "Génère un message empathique de 1-2 phrases pour alerter le parent avec douceur et lui proposer de rassurer son enfant."
        ),
        "surmenage": (
            f"{child_name} a nettement augmenté son rythme de travail ({int(stat_value) if stat_value else 'beaucoup'} séances cette semaine) mais ses scores ont baissé malgré cet effort. "
            "Génère un message bienveillant de 1-2 phrases pour alerter le parent sur un possible surmenage et l'encourager à inviter son enfant à se reposer."
        ),
    }
    prompt = prompts.get(alert_type, f"Génère un message bienveillant court pour le parent de {child_name}.")
    try:
        result = deepseek.chat(
            messages=[{"role": "user", "content": prompt}],
            system_prompt=(
                "Tu es WinAI, assistant pédagogique bienveillant de WinPlus. "
                "Tu t'adresses aux parents avec empathie, chaleur et encouragement. "
                "Réponds toujours en français, en 1-2 phrases concises. "
                "IMPORTANT : n'utilise JAMAIS de vocabulaire clinique ou diagnostique "
                "(interdits : dépression, anxiété diagnostiquée, trouble, burnout, diagnostic). "
                "Ce sont des signaux comportementaux observés, pas un diagnostic médical. "
                "Préfère des formulations comme : changement de rythme, signal à ne pas "
                "ignorer, moment pour prendre des nouvelles, baisse d'activité observée."
            ),
            max_tokens=120,
            temperature=0.7,
        )
        text = result.get("content", "").strip()
        if text and not _contains_forbidden_vocabulary(text):
            return text
        if text:
            logger.warning(f"WinAI message for {alert_type} contained forbidden vocabulary, using fallback.")
        return _fallback_message(alert_type, child_name)
    except Exception as e:
        logger.warning(f"DeepSeek WinAI message failed: {e}")
        return _fallback_message(alert_type, child_name)


def _fallback_message(alert_type: str, child_name: str) -> str:
    fallbacks = {
        "performance_drop": f"Les scores de {child_name} ont baissé cette semaine  un petit encouragement peut faire toute la différence !",
        "inactivity":       f"{child_name} n'a pas étudié depuis quelques jours. Un message de votre part pourrait l'aider à reprendre.",
        "excellent_week":   f"Bravo à {child_name} pour cette excellente semaine ! Partagez-lui votre fierté.",
        "exam_anxiety":     f"{child_name} révise très tard le soir. Encouragez-le à se reposer : le sommeil est essentiel avant un examen.",
        "surmenage":        f"{child_name} travaille beaucoup mais ses résultats baissent. Invitez-le à faire une pause  la qualité prime sur la quantité.",
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
        _assert_can_access_child(session, current_user, child_id)

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

        # ── Détection 4 : anxiété d'examen (quiz après 22h) ────────────────────
        night_attempts = [
            a for a in attempts
            if a.CompletedAt and a.CompletedAt.hour >= 22
        ]
        if len(night_attempts) >= 3:
            near_exam = False
            try:
                plan = (
                    session.query(ExamCoachPlanAI)
                    .filter(
                        ExamCoachPlanAI.UserId == child_id,
                        ExamCoachPlanAI.IsActive == True,
                        ExamCoachPlanAI.ExamDate >= now,
                        ExamCoachPlanAI.ExamDate <= now + timedelta(days=60),
                    )
                    .first()
                )
                near_exam = plan is not None
            except Exception:
                pass
            if near_exam:
                deepseek = get_deepseek_client()
                msg = _winai_message("exam_anxiety", child_name, len(night_attempts), deepseek)
                alerts.append({
                    "type":        "exam_anxiety",
                    "severity":    "warn",
                    "message":     msg,
                    "detected_at": now.isoformat(),
                    "child_stats": {"night_sessions_count": len(night_attempts)},
                })

        # ── Détection 5 : surmenage (plus de travail, moins de résultats) ───────
        count_last_7 = len([a for a in attempts if a.CompletedAt and a.CompletedAt >= cutoff_7])
        count_prev_7 = len([a for a in attempts if a.CompletedAt and a.CompletedAt < cutoff_7])
        if (
            count_last_7 > 0
            and count_prev_7 > 0
            and count_last_7 >= count_prev_7 * 1.5
            and scores_last_7
            and scores_prev_7
            and not any(a["type"] == "surmenage" for a in alerts)
        ):
            avg_last = sum(scores_last_7) / len(scores_last_7)
            avg_prev = sum(scores_prev_7) / len(scores_prev_7)
            if avg_prev > 0 and (avg_prev - avg_last) / avg_prev * 100 >= 10:
                deepseek = get_deepseek_client()
                msg = _winai_message("surmenage", child_name, count_last_7, deepseek)
                alerts.append({
                    "type":        "surmenage",
                    "severity":    "warn",
                    "message":     msg,
                    "detected_at": now.isoformat(),
                    "child_stats": {
                        "sessions_this_week": count_last_7,
                        "sessions_prev_week": count_prev_7,
                        "avg_score_last_7d":  round(avg_last, 1),
                        "avg_score_prev_7d":  round(avg_prev, 1),
                    },
                })

        # Persistance dans ParentAlerts (historique + source du baromètre côté
        # .NET) — ne doit jamais faire échouer la réponse JSON déjà calculée,
        # d'où le try/except interne à _persist_alerts.
        if alerts:
            _persist_alerts(session, current_user.user_id, child_id, alerts)

    except HTTPException:
        raise  # ex. le 403 de _assert_can_access_child  ne doit pas devenir un 500 générique ci-dessous.
    except Exception as e:
        logger.error(f"Error computing parent alerts for child {child_id}: {e}")
        raise HTTPException(status_code=500, detail="Alert computation failed")
    finally:
        session.close()

    return {"alerts": alerts}
