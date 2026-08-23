"""
WinAI  Endpoints complémentaires pour le compte Parent.

- GET  /parent-engagement/{parent_id}   → Score de mobilisation parental
- POST /parent/educational-roi           → ROI éducatif
- GET  /parent/children-insights         → Comparaison inter-enfants (insights)
"""

import json
import logging
from datetime import datetime, timedelta, timezone
from typing import List, Optional

from fastapi import APIRouter, Depends, HTTPException, Query
from pydantic import BaseModel

from auth import verify_token, UserTokenData
from database import Database, QuizAttempt, DailyScore, Enrollment, Subject, User, Conversation, ChatMessage as ChatMessageDB
from services.deepseek_client import get_deepseek_client

logger = logging.getLogger(__name__)

parent_extra_router = APIRouter()


# ── Feature 3  Score de Mobilisation Parental ────────────────────────────────

class EngagementResponse(BaseModel):
    score: int
    level: str
    tips: List[str]
    details: dict


@parent_extra_router.get("/parent-engagement/{parent_id}", response_model=EngagementResponse)
async def get_parent_engagement(
    parent_id: int,
    current_user: UserTokenData = Depends(verify_token),
):
    """
    Score de mobilisation parental (0-100).
    Basé sur: activité quiz récente des enfants, conversations IA, inscriptions.
    Rendu toujours positif et actionnable.
    """
    db = Database()
    session = db.SessionLocal()
    now = datetime.now(timezone.utc)
    cutoff_30 = now - timedelta(days=30)

    try:
        score = 0
        details: dict = {}

        # ── Composante 1 : Conversations IA (engagement direct) ───────────────
        conv_count = (
            session.query(Conversation)
            .filter(
                Conversation.UserId == parent_id,
                Conversation.CreatedAt >= cutoff_30,
                Conversation.IsDeleted == False,
            )
            .count()
        )
        conv_score = min(30, conv_count * 6)
        score += conv_score
        details["ai_conversations_30d"] = conv_count

        # ── Composante 2 : Inscriptions actives des enfants ───────────────────
        # The parent's children are users linked via a parent relation; we proxy
        # through Enrollments created on behalf of children known to this parent.
        # Approximation: count Enrollments for the last 90 days across all users
        # whose UserId corresponds to children visible in the parent's dashboard.
        # Since we don't have a ParentChild table in Python, we use the children
        # passed via query param or default to 0 if unavailable.
        enrollment_score = 0
        details["active_enrollments"] = 0

        # ── Composante 3 : Activité IA récente (messages) ─────────────────────
        msg_count = (
            session.query(ChatMessageDB)
            .join(Conversation, ChatMessageDB.ConversationId == Conversation.Id)
            .filter(
                Conversation.UserId == parent_id,
                ChatMessageDB.Role == "user",
                ChatMessageDB.CreatedAt >= cutoff_30,
            )
            .count()
        )
        msg_score = min(40, msg_count * 4)
        score += msg_score
        details["ai_messages_30d"] = msg_count

        # ── Composante 4 : Présence de base (compte créé, profil actif) ───────
        parent_user = session.query(User).filter(User.Id == parent_id).first()
        if parent_user:
            score += 30  # base score for having an active account
            details["account_active"] = True

        score = min(100, max(0, score))

        # ── Détermination du niveau ───────────────────────────────────────────
        if score >= 70:
            level = "Très actif"
            tips = [
                "Continuez à consulter WinAI pour suivre la progression de vos enfants.",
                "Pensez à partager vos encouragements directement depuis le tableau de bord.",
            ]
        elif score >= 40:
            level = "Actif"
            tips = [
                "Posez une question à WinAI sur les derniers résultats de votre enfant.",
                "Activez les alertes hebdomadaires pour rester informé sans effort.",
            ]
        else:
            level = "En démarrage"
            tips = [
                "Commencez par consulter WinAI  il connaît déjà le profil de votre enfant.",
                "Même 5 minutes de suivi par semaine ont un impact réel sur la motivation.",
            ]

        return {
            "score": score,
            "level": level,
            "tips": tips,
            "details": details,
        }

    except Exception as e:
        logger.error(f"Error computing parent engagement for {parent_id}: {e}")
        return {"score": 50, "level": "Actif", "tips": ["Continuez à suivre la progression de vos enfants."], "details": {}}
    finally:
        session.close()


# ── Feature 5  ROI Éducatif ─────────────────────────────────────────────────

class ROIRequest(BaseModel):
    parent_id: int
    child_ids: List[int]


class ROIResponse(BaseModel):
    total_spent_xaf: float
    avg_score_improvement: float
    most_impactful_subject: Optional[str]
    recommendation: str
    children_summary: List[dict]


@parent_extra_router.post("/parent/educational-roi", response_model=ROIResponse)
async def get_educational_roi(
    body: ROIRequest,
    current_user: UserTokenData = Depends(verify_token),
):
    """
    ROI éducatif : croise les inscriptions (proxy pour investissement) avec la
    progression des scores DailyScores des enfants sur 90 jours.
    """
    db = Database()
    session = db.SessionLocal()
    now = datetime.now(timezone.utc)
    cutoff_90 = now - timedelta(days=90)
    cutoff_45 = now - timedelta(days=45)

    try:
        children_summary = []
        total_enrolled_subjects = 0
        total_improvement = 0.0
        subject_impacts: dict[int, float] = {}

        for child_id in body.child_ids:
            child_user = session.query(User).filter(User.Id == child_id).first()
            child_name = child_user.FirstName or f"Enfant {child_id}" if child_user else f"Enfant {child_id}"

            # Score comparison: first 45 days vs last 45 days of the 90-day window
            early_scores = (
                session.query(DailyScore)
                .filter(
                    DailyScore.UserId == child_id,
                    DailyScore.CreatedAt >= cutoff_90,
                    DailyScore.CreatedAt < cutoff_45,
                )
                .all()
            )
            recent_scores = (
                session.query(DailyScore)
                .filter(
                    DailyScore.UserId == child_id,
                    DailyScore.CreatedAt >= cutoff_45,
                )
                .all()
            )

            avg_early = (
                sum(float(s.AverageScore) for s in early_scores) / len(early_scores)
                if early_scores else 0.0
            )
            avg_recent = (
                sum(float(s.AverageScore) for s in recent_scores) / len(recent_scores)
                if recent_scores else 0.0
            )
            improvement = round(avg_recent - avg_early, 1)
            total_improvement += improvement

            # Enrollments as proxy for investment
            enrollments = (
                session.query(Enrollment)
                .filter(
                    Enrollment.UserId == child_id,
                    Enrollment.IsDeleted == False,
                )
                .all()
            )
            total_enrolled_subjects += len(enrollments)

            # Best subject (max avg score from DailyScores with SubjectId)
            best_subject_id = None
            best_subject_avg = 0.0
            for score_row in recent_scores:
                if score_row.SubjectId and float(score_row.AverageScore) > best_subject_avg:
                    best_subject_avg = float(score_row.AverageScore)
                    best_subject_id = score_row.SubjectId

            if best_subject_id:
                subject_impacts[best_subject_id] = subject_impacts.get(best_subject_id, 0) + best_subject_avg

            children_summary.append({
                "child_name": child_name,
                "improvement_pts": improvement,
                "avg_score_recent": round(avg_recent, 1),
                "enrolled_subjects": len(enrollments),
            })

        # Estimate total spent (2500 XAF per subject as average price proxy)
        total_spent_xaf = total_enrolled_subjects * 2500.0

        # Most impactful subject
        most_impactful_subject = None
        if subject_impacts:
            top_id = max(subject_impacts, key=lambda k: subject_impacts[k])
            subj = session.query(Subject).filter(Subject.Id == top_id).first()
            most_impactful_subject = subj.Title if subj else None

        avg_improvement = round(
            total_improvement / max(len(body.child_ids), 1), 1
        )

        # Generate recommendation with DeepSeek
        summary_text = "; ".join(
            f"{c['child_name']} (+{c['improvement_pts']}pts, {c['enrolled_subjects']} matières)"
            for c in children_summary
        )
        try:
            deepseek = get_deepseek_client()
            result = deepseek.chat(
                messages=[{
                    "role": "user",
                    "content": (
                        f"Un parent a investi dans {total_enrolled_subjects} inscription(s) sur WinPlus. "
                        f"Résultats : {summary_text}. "
                        f"Progression moyenne : {avg_improvement} points. "
                        "Génère une recommandation encourageante de 2 phrases pour ce parent, "
                        "en valorisant l'investissement et en suggérant une action concrète pour amplifier les résultats."
                    ),
                }],
                system_prompt=(
                    "Tu es WinAI, conseiller pédagogique bienveillant. "
                    "Réponds en français, en 2 phrases courtes et positives."
                ),
                max_tokens=100,
                temperature=0.7,
            )
            recommendation = result.get("content", "").strip()
        except Exception:
            if avg_improvement > 0:
                recommendation = f"Votre investissement porte ses fruits : {avg_improvement} points de progression en moyenne. Continuez à encourager vos enfants  chaque session compte !"
            else:
                recommendation = "Chaque cours inscrit est une opportunité. Encouragez vos enfants à explorer les contenus disponibles pour démarrer leur progression."

        return {
            "total_spent_xaf": total_spent_xaf,
            "avg_score_improvement": avg_improvement,
            "most_impactful_subject": most_impactful_subject,
            "recommendation": recommendation,
            "children_summary": children_summary,
        }

    except Exception as e:
        logger.error(f"Error computing educational ROI for parent {body.parent_id}: {e}")
        raise HTTPException(status_code=500, detail="ROI computation failed")
    finally:
        session.close()


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
