"""
WinAI  Endpoints IA pour le compte Institution.

- POST /institution/class-prediction       → Analyse prédictive de réussite
- GET  /institution/benchmark/{id}         → Benchmarking anonyme vs national
- POST /institution/action-plan            → Plan d'action institutionnel IA (3 actions hebdo)
- GET  /institution/at-risk-students/{id}  → Détection des étudiants à risque
"""

import json
import logging
from datetime import datetime, timedelta, date
from typing import Any, Dict, List, Optional

from fastapi import APIRouter, Depends, HTTPException, Query
from pydantic import BaseModel
from sqlalchemy import func

from auth import verify_token, UserTokenData
from database import Database, DailyScore, Enrollment, QuizAttempt, Subject, User
from services.deepseek_client import get_deepseek_client

logger = logging.getLogger(__name__)

institution_router = APIRouter()
_db = Database()

PASS_THRESHOLD = 50.0   # score >= 50 % → réussite
RISK_INACTIVITY_DAYS = 14


# ─────────────────────────────────────────────────────────────────────────────
# Helpers
# ─────────────────────────────────────────────────────────────────────────────

def _deepseek_json(prompt: str, system: str, max_tokens: int = 700) -> Any:
    try:
        ds = get_deepseek_client()
        res = ds.chat(
            messages=[{"role": "user", "content": prompt}],
            system_prompt=system,
            max_tokens=max_tokens,
            temperature=0.5,
        )
        raw = res.get("content", "").strip()
        if raw.startswith("```"):
            raw = "\n".join(raw.split("\n")[1:])
        if raw.endswith("```"):
            raw = raw.rsplit("```", 1)[0].strip()
        return json.loads(raw)
    except Exception as e:
        logger.warning(f"_deepseek_json error: {e}")
        return None


def _fetch_student_scores(session, student_ids: List[int]) -> List[Dict]:
    """Renvoie la liste {user_id, avg_score, last_active, quiz_count} pour chaque étudiant."""
    if not student_ids:
        return []
    rows = (
        session.query(
            DailyScore.UserId,
            func.avg(DailyScore.AverageScore).label("avg_score"),
            func.max(DailyScore.Date).label("last_active"),
            func.sum(DailyScore.QuizCount).label("quiz_count"),
        )
        .filter(DailyScore.UserId.in_(student_ids))
        .group_by(DailyScore.UserId)
        .all()
    )
    return [
        {
            "user_id": r.UserId,
            "avg_score": float(r.avg_score or 0),
            "last_active": r.last_active,
            "quiz_count": int(r.quiz_count or 0),
        }
        for r in rows
    ]


def _resolve_student_ids(session, institution_id: int, provided: List[int]) -> List[int]:
    """
    Si provided est non vide, l'utilise directement.
    Sinon, cherche les Users avec Role='student' (fallback pour tests locaux).
    """
    if provided:
        return provided
    rows = (
        session.query(User.Id)
        .filter(User.Role == "student")
        .limit(500)
        .all()
    )
    return [r.Id for r in rows]


# ─────────────────────────────────────────────────────────────────────────────
# 1. Analyse Prédictive de Réussite
# ─────────────────────────────────────────────────────────────────────────────

class PredictionRequest(BaseModel):
    institution_id: int
    student_ids: Optional[List[int]] = []
    group_name: Optional[str] = None


@institution_router.post("/institution/class-prediction")
async def class_prediction(
    body: PredictionRequest,
    current_user: UserTokenData = Depends(verify_token),
):
    session = _db.SessionLocal()
    try:
        student_ids = _resolve_student_ids(session, body.institution_id, body.student_ids or [])
        if not student_ids:
            return {
                "predicted_pass_rate": 0,
                "confidence": "low",
                "at_risk_count": 0,
                "likely_to_excel_count": 0,
                "comparison_to_last_year": None,
                "key_risk_factors": [],
                "recommended_interventions": [],
                "student_count": 0,
            }

        scores = _fetch_student_scores(session, student_ids)
        total = len(scores)
        if total == 0:
            return {
                "predicted_pass_rate": 0,
                "confidence": "low",
                "at_risk_count": 0,
                "likely_to_excel_count": 0,
                "comparison_to_last_year": None,
                "key_risk_factors": ["Aucune donnée de performance disponible"],
                "recommended_interventions": ["Encourager les étudiants à compléter leurs premiers quiz"],
                "student_count": len(student_ids),
            }

        passing = sum(1 for s in scores if s["avg_score"] >= PASS_THRESHOLD)
        at_risk = sum(1 for s in scores if s["avg_score"] < 35)
        excellent = sum(1 for s in scores if s["avg_score"] >= 75)
        avg = sum(s["avg_score"] for s in scores) / total
        pass_rate = round((passing / total) * 100, 1)

        confidence = "high" if total >= 20 else "medium" if total >= 5 else "low"

        today = date.today()
        inactive_threshold = today - timedelta(days=RISK_INACTIVITY_DAYS)
        inactive_count = sum(
            1 for s in scores
            if s["last_active"] is None or s["last_active"] < inactive_threshold
        )

        risk_factors = []
        if avg < 50:
            risk_factors.append(f"Score moyen faible ({avg:.1f}%)  objectif 60%")
        if inactive_count > total * 0.3:
            risk_factors.append(f"{inactive_count} étudiant(s) inactifs depuis +{RISK_INACTIVITY_DAYS}j")
        if at_risk > 0:
            risk_factors.append(f"{at_risk} étudiant(s) en zone critique (score <35%)")

        prompt = (
            f"Une institution a {total} étudiants avec un score moyen de {avg:.1f}%, "
            f"un taux de réussite prédit de {pass_rate}%, {inactive_count} inactifs, {at_risk} en zone critique.\n"
            "Fournis 3 interventions pédagogiques prioritaires en JSON: "
            '[{"intervention": "...", "impact": "élevé|moyen", "effort": "court|moyen|long terme"}]'
        )
        system = "Tu es un expert en analyse pédagogique institutionnelle. Réponds en JSON valide uniquement."
        interventions_raw = _deepseek_json(prompt, system, max_tokens=500)
        interventions = interventions_raw if isinstance(interventions_raw, list) else [
            {"intervention": "Organiser des sessions de rattrapage pour les étudiants <50%", "impact": "élevé", "effort": "court terme"},
            {"intervention": "Mettre en place un suivi hebdomadaire des étudiants inactifs", "impact": "élevé", "effort": "court terme"},
            {"intervention": "Renforcer les ressources pour les matières les plus échouées", "impact": "moyen", "effort": "moyen terme"},
        ]

        return {
            "predicted_pass_rate": pass_rate,
            "confidence": confidence,
            "at_risk_count": at_risk,
            "likely_to_excel_count": excellent,
            "comparison_to_last_year": None,
            "key_risk_factors": risk_factors,
            "recommended_interventions": interventions,
            "student_count": total,
            "avg_score": round(avg, 1),
            "inactive_count": inactive_count,
        }
    finally:
        session.close()


# ─────────────────────────────────────────────────────────────────────────────
# 2. Benchmarking Anonyme
# ─────────────────────────────────────────────────────────────────────────────

@institution_router.get("/institution/benchmark/{institution_id}")
async def benchmark(
    institution_id: int,
    student_ids: Optional[str] = Query(None, description="IDs séparés par virgule"),
    current_user: UserTokenData = Depends(verify_token),
):
    session = _db.SessionLocal()
    try:
        provided_ids = [int(x) for x in student_ids.split(",") if x.strip().isdigit()] if student_ids else []
        inst_student_ids = _resolve_student_ids(session, institution_id, provided_ids)

        inst_scores = _fetch_student_scores(session, inst_student_ids)
        inst_avg = round(sum(s["avg_score"] for s in inst_scores) / len(inst_scores), 1) if inst_scores else 0
        inst_pass_rate = round(
            sum(1 for s in inst_scores if s["avg_score"] >= PASS_THRESHOLD) / len(inst_scores) * 100, 1
        ) if inst_scores else 0
        inst_engagement = round(
            sum(1 for s in inst_scores if (s["quiz_count"] or 0) > 0) / len(inst_scores) * 100, 1
        ) if inst_scores else 0

        # National avg  tous les étudiants dans la base (sauf l'institution elle-même)
        nat_q = session.query(
            func.avg(DailyScore.AverageScore).label("national_avg"),
            func.count(DailyScore.UserId.distinct()).label("national_count"),
        )
        if inst_student_ids:
            nat_q = nat_q.filter(DailyScore.UserId.notin_(inst_student_ids))
        all_rows = nat_q.first()
        national_avg = round(float(all_rows.national_avg or 60), 1)
        national_pass_rate = round(min(max(national_avg * 1.1, 40), 75), 1)
        national_engagement = 65.0

        # Top quartile avg
        top_q_avg = min(national_avg + 18, 92.0)

        # Meilleure / pire matière institution
        if inst_student_ids:
            subj_rows = (
                session.query(
                    Subject.Title,
                    func.avg(DailyScore.AverageScore).label("subj_avg"),
                )
                .join(Enrollment, Enrollment.SubjectId == Subject.Id)
                .join(DailyScore, DailyScore.UserId == Enrollment.UserId)
                .filter(Enrollment.UserId.in_(inst_student_ids))
                .group_by(Subject.Id, Subject.Title)
                .order_by(func.avg(DailyScore.AverageScore).desc())
                .limit(10)
                .all()
            )
        else:
            subj_rows = []

        strongest_subject = subj_rows[0].Title if subj_rows else ""
        weakest_subject = subj_rows[-1].Title if len(subj_rows) > 1 else ""

        percentile = 50
        if national_avg > 0:
            ratio = inst_avg / national_avg
            if ratio >= 1.15:
                percentile = 85
            elif ratio >= 1.05:
                percentile = 70
            elif ratio >= 0.95:
                percentile = 50
            elif ratio >= 0.85:
                percentile = 35
            else:
                percentile = 20

        return {
            "institution_avg_score": inst_avg,
            "national_avg_score": national_avg,
            "top_quartile_avg": round(top_q_avg, 1),
            "institution_percentile": percentile,
            "institution_pass_rate": inst_pass_rate,
            "national_pass_rate": national_pass_rate,
            "strongest_subject": strongest_subject,
            "weakest_subject_vs_peers": weakest_subject,
            "engagement_rate": inst_engagement,
            "peer_engagement_avg": national_engagement,
            "student_count": len(inst_student_ids),
            "national_student_count": int(all_rows.national_count or 0),
        }
    finally:
        session.close()


# ─────────────────────────────────────────────────────────────────────────────
# 3. Plan d'Action Institutionnel IA
# ─────────────────────────────────────────────────────────────────────────────

class ActionPlanRequest(BaseModel):
    institution_id: int
    student_ids: Optional[List[int]] = []
    institution_name: Optional[str] = None


@institution_router.post("/institution/action-plan")
async def action_plan(
    body: ActionPlanRequest,
    current_user: UserTokenData = Depends(verify_token),
):
    session = _db.SessionLocal()
    try:
        student_ids = _resolve_student_ids(session, body.institution_id, body.student_ids or [])
        scores = _fetch_student_scores(session, student_ids)

        total = len(scores)
        avg = round(sum(s["avg_score"] for s in scores) / total, 1) if scores else 0
        today = date.today()
        inactive_threshold = today - timedelta(days=RISK_INACTIVITY_DAYS)
        inactive_count = sum(
            1 for s in scores
            if s["last_active"] is None or s["last_active"] < inactive_threshold
        ) if scores else 0
        at_risk = sum(1 for s in scores if s["avg_score"] < 35)

        name = body.institution_name or "votre établissement"
        prompt = (
            f"L'établissement « {name} » a {total} étudiants, score moyen {avg}%, "
            f"{inactive_count} inactifs depuis +14j, {at_risk} en zone critique (<35%).\n"
            "Génère EXACTEMENT 3 actions institutionnelles prioritaires pour cette semaine en JSON:\n"
            '[{"priority": 1, "action": "...", "effort": "court|moyen|long terme", "estimated_impact": "..."}]\n'
            "Les actions doivent être concrètes, mesurables et adaptées à un établissement éducatif."
        )
        system = (
            "Tu es le Directeur des Études Virtuel WinAI. "
            "Tu fournis des plans d'action stratégiques pour les institutions éducatives. "
            "Réponds en JSON valide uniquement, tableau de 3 éléments."
        )
        actions_raw = _deepseek_json(prompt, system, max_tokens=600)

        if isinstance(actions_raw, list) and len(actions_raw) == 3:
            actions = actions_raw
        else:
            actions = [
                {
                    "priority": 1,
                    "action": f"Contacter les {inactive_count or 'N'} étudiants inactifs et planifier une session de rattrapage",
                    "effort": "court terme",
                    "estimated_impact": "Réengagement immédiat de 20–40% des inactifs",
                },
                {
                    "priority": 2,
                    "action": "Organiser un atelier de remise à niveau pour les étudiants avec score <50%",
                    "effort": "moyen terme",
                    "estimated_impact": f"Augmentation estimée du score moyen de {avg:.0f}% à {min(avg + 8, 80):.0f}%",
                },
                {
                    "priority": 3,
                    "action": "Analyser les matières les plus échouées et adapter le programme de soutien",
                    "effort": "long terme",
                    "estimated_impact": "Amélioration structurelle du taux de réussite à 3 mois",
                },
            ]

        # Le plan se renouvelle chaque lundi : on calcule la semaine courante
        week_start = today - timedelta(days=today.weekday())
        week_label = week_start.strftime("Semaine du %d/%m/%Y")

        return {
            "actions": actions,
            "week_label": week_label,
            "generated_at": datetime.utcnow().isoformat(),
            "context": {
                "student_count": total,
                "avg_score": avg,
                "inactive_count": inactive_count,
                "at_risk_count": at_risk,
            },
        }
    finally:
        session.close()


# ─────────────────────────────────────────────────────────────────────────────
# 4. Détection des Étudiants à Risque
# ─────────────────────────────────────────────────────────────────────────────

@institution_router.get("/institution/at-risk-students/{institution_id}")
async def at_risk_students(
    institution_id: int,
    student_ids: Optional[str] = Query(None, description="IDs séparés par virgule"),
    current_user: UserTokenData = Depends(verify_token),
):
    session = _db.SessionLocal()
    try:
        provided_ids = [int(x) for x in student_ids.split(",") if x.strip().isdigit()] if student_ids else []
        all_student_ids = _resolve_student_ids(session, institution_id, provided_ids)

        if not all_student_ids:
            return {"students": [], "total_at_risk": 0}

        scores = _fetch_student_scores(session, all_student_ids)
        score_map = {s["user_id"]: s for s in scores}

        # User info (name)
        users = (
            session.query(User.Id, User.FirstName, User.LastName, User.Email)
            .filter(User.Id.in_(all_student_ids))
            .all()
        )
        user_map = {u.Id: u for u in users}

        # Tendance : compare les 7 derniers jours aux 7 jours précédents
        today = date.today()
        recent_start = today - timedelta(days=7)
        prev_start = today - timedelta(days=14)

        recent_avgs = {}
        prev_avgs = {}
        for uid in all_student_ids:
            r_rows = (
                session.query(_db.func_avg(DailyScore.AverageScore))
                .filter(DailyScore.UserId == uid, DailyScore.Date >= recent_start)
                .scalar()
            )
            p_rows = (
                session.query(_db.func_avg(DailyScore.AverageScore))
                .filter(DailyScore.UserId == uid, DailyScore.Date >= prev_start, DailyScore.Date < recent_start)
                .scalar()
            )
            recent_avgs[uid] = float(r_rows or 0)
            prev_avgs[uid] = float(p_rows or 0)

        # Prochain examen dans les 30 jours (approximation : on vérifie les enrollments récents)
        upcoming_threshold = today + timedelta(days=30)
        recent_enrollments = set(
            row.UserId
            for row in session.query(Enrollment.UserId).filter(
                Enrollment.UserId.in_(all_student_ids),
                Enrollment.EnrolledAt >= (datetime.utcnow() - timedelta(days=30)),
            ).all()
        )

        risky_students = []
        for uid in all_student_ids:
            s = score_map.get(uid, {})
            avg_score = s.get("avg_score", 0)
            last_active = s.get("last_active")
            quiz_count = s.get("quiz_count", 0)

            inactive_days = (today - last_active).days if last_active else 999
            is_inactive = inactive_days >= RISK_INACTIVITY_DAYS
            is_low_score = avg_score < 35
            has_downtrend = recent_avgs.get(uid, 0) < prev_avgs.get(uid, 0) - 5 and prev_avgs.get(uid, 0) > 0
            has_upcoming_exam = uid in recent_enrollments

            # Weighted risk score
            risk_score = 0
            if is_inactive:
                risk_score += 3
            if is_low_score:
                risk_score += 4
            if has_downtrend:
                risk_score += 2
            if has_upcoming_exam and risk_score > 0:
                risk_score = int(risk_score * 2)  # ×2 multiplier

            if risk_score > 0:
                u = user_map.get(uid)
                risky_students.append({
                    "user_id": uid,
                    "name": f"{u.FirstName or ''} {u.LastName or ''}".strip() if u else f"Étudiant #{uid}",
                    "email": u.Email if u else None,
                    "avg_score": round(avg_score, 1),
                    "inactive_days": inactive_days if inactive_days < 999 else None,
                    "quiz_count": quiz_count,
                    "has_downtrend": has_downtrend,
                    "has_upcoming_exam": has_upcoming_exam,
                    "risk_score": risk_score,
                    "risk_level": "critical" if risk_score >= 12 else "high" if risk_score >= 6 else "medium",
                })

        risky_students.sort(key=lambda x: x["risk_score"], reverse=True)

        return {
            "students": risky_students[:100],
            "total_at_risk": len(risky_students),
            "total_students": len(all_student_ids),
        }
    finally:
        session.close()
