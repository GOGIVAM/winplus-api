"""
Partie 14 (décisions produit du 2026-10-08, lot 6.1)  Endpoints service-à-
service, appelés uniquement par des tâches de fond .NET (jamais par un écran),
qui exploitent la base de contexte partagée parent_child_context.py :

- POST /api/parent-advisor/threshold-alerts  (14.1) Détecte les franchissements
  de seuil pour une liste de couples (parent, enfant) déjà vérifiés côté .NET
  (ParentStudentLink.Status = 'accepted'). Ne persiste rien : ParentAdvisor
  ThresholdService (.NET) décide de la déduplication (ParentAlerts.DedupKey)
  et déclenche la notification (NtfyService, lot 1 module 22).

- POST /api/parent-advisor/weekly-trend  (écart technique signalé au lot 6)
  Renvoie, pour une liste d'enfants, le même indicateur hebdomadaire que
  WeeklyParentReportService calculait seul jusqu'ici (voir
  compute_weekly_trend). Utilisé pour unifier la source de vérité sans changer
  le format de l'e-mail ni ses chiffres.

Même schéma d'authentification que /api/winai/detection-decrochage
(teacher_extra_routes.py) : require_user_or_service avec roles=() -- seule la
tâche de fond .NET porteuse du jeton technique correspondant peut appeler ces
routes, aucun écran ne les utilise.
"""

import logging
from typing import List, Optional

from fastapi import APIRouter, Depends
from pydantic import BaseModel

from auth import require_user_or_service, UserTokenData
from database import Database
from services.parent_child_context import detect_threshold_alerts, compute_weekly_trend

logger = logging.getLogger(__name__)

parent_advisor_router = APIRouter()


# ─────────────────────────────────────────────────────────────────────────────
# 14.1  Détection des alertes par seuil
# ─────────────────────────────────────────────────────────────────────────────

class ParentChildLink(BaseModel):
    parent_id: int
    child_id: int


class ThresholdScanRequest(BaseModel):
    links: List[ParentChildLink]


class ThresholdAlertOut(BaseModel):
    parent_id: int
    child_id: int
    type: str
    severity: str
    content: str
    detected_at: str
    dedup_key: str


class ThresholdScanResponse(BaseModel):
    alerts: List[ThresholdAlertOut]


@parent_advisor_router.post("/threshold-alerts", response_model=ThresholdScanResponse)
async def threshold_alerts(
    body: ThresholdScanRequest,
    # Module 36 : jeton technique uniquement, comme detection-decrochage.
    current_user: UserTokenData = Depends(require_user_or_service("ai.parent-advisor-thresholds", roles=())),
):
    db = Database()
    session = db.SessionLocal()
    alerts: List[ThresholdAlertOut] = []
    try:
        for link in body.links:
            try:
                for a in detect_threshold_alerts(link.child_id, session=session):
                    alerts.append(ThresholdAlertOut(
                        parent_id=link.parent_id,
                        child_id=link.child_id,
                        type=a["type"],
                        severity=a["severity"],
                        content=a["content"],
                        detected_at=a["detected_at"],
                        dedup_key=a["dedup_key"],
                    ))
            except Exception as e:
                logger.warning(f"threshold_alerts: échec pour l'enfant {link.child_id}: {e}")
    finally:
        session.close()
    return ThresholdScanResponse(alerts=alerts)


# ─────────────────────────────────────────────────────────────────────────────
# Écart technique lot 6  Tendance hebdomadaire unifiée
# ─────────────────────────────────────────────────────────────────────────────

class WeeklyTrendRequest(BaseModel):
    child_ids: List[int]


class WeeklyTrendOut(BaseModel):
    child_id: int
    week_avg: float
    prev_avg: float
    quiz_count: int
    delta: float


class WeeklyTrendResponse(BaseModel):
    results: List[WeeklyTrendOut]


@parent_advisor_router.post("/weekly-trend", response_model=WeeklyTrendResponse)
async def weekly_trend(
    body: WeeklyTrendRequest,
    current_user: UserTokenData = Depends(require_user_or_service("ai.parent-weekly-trend", roles=())),
):
    db = Database()
    session = db.SessionLocal()
    results: List[WeeklyTrendOut] = []
    try:
        for child_id in body.child_ids:
            try:
                r = compute_weekly_trend(child_id, session=session)
            except Exception:
                # Deuxième vérification (revue croisée) : un enfant omis ici
                # (plutôt qu'un 0.0/0 silencieux) fait retomber .NET sur son
                # repli EF direct UNIQUEMENT pour cet enfant, au lieu de lui
                # faire croire à une vraie valeur nulle.
                continue
            results.append(WeeklyTrendOut(**r))
    finally:
        session.close()
    return WeeklyTrendResponse(results=results)
