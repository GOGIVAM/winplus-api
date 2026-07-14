"""
WinAI — Exam Coach: plan de révision personnalisé Ebbinghaus.

POST /api/exam-coach/generate        — Génère un plan de révision
POST /api/exam-coach/recalibrate     — Recalibre un plan existant
GET  /api/exam-coach/today/{user_id} — Session du jour + message WinAI
"""

import json
import logging
from datetime import date, datetime, timedelta
from typing import List, Optional

from fastapi import APIRouter, Depends, HTTPException, status
from pydantic import BaseModel

from auth import verify_token, UserTokenData
from database import Database, ExamCoachPlanAI
from services.deepseek_client import get_deepseek_client
from services.user_performance_analyzer import UserPerformanceAnalyzer
from models.nlp_analyzer import NLPAnalyzer
from models.recommender import Recommender

logger = logging.getLogger(__name__)

exam_coach_router = APIRouter()


# ── Pydantic schemas ──────────────────────────────────────────────────────────

class GeneratePlanRequest(BaseModel):
    user_id: int
    exam_type: str
    exam_date: str  # ISO YYYY-MM-DD
    hours_per_day: float


class DayResult(BaseModel):
    day: int
    activity_completed: bool
    quiz_score_if_applicable: Optional[float] = None


class RecalibrateRequest(BaseModel):
    user_id: int
    plan_id: int
    week_results: List[DayResult]


# ── Ebbinghaus scheduling helper ─────────────────────────────────────────────

def _generate_plan(user_id, exam_type, exam_date_str, hours_per_day, weak_areas, enrolled_subjects):
    today = date.today()
    exam_dt = date.fromisoformat(exam_date_str)
    days_remaining = (exam_dt - today).days

    # Normalize subjects — weak areas first
    all_subjects = list(dict.fromkeys((weak_areas or []) + (enrolled_subjects or [])))[:8]
    if not all_subjects:
        all_subjects = ['Mathématiques', 'Français', 'Physique-Chimie', 'Sciences de la Vie']

    # Priority weights: 3 for top weak areas, 2 for other weak, 1 for enrolled
    weights = {}
    for s in all_subjects:
        if s in (weak_areas or [])[:3]:
            weights[s] = 3
        elif s in (weak_areas or []):
            weights[s] = 2
        else:
            weights[s] = 1

    duration_min = min(int(hours_per_day * 60), 120)
    plan = []
    last_reviewed = {s: -999 for s in all_subjects}
    review_count = {s: 0 for s in all_subjects}

    for day in range(1, days_remaining + 1):
        current_date = today + timedelta(days=day - 1)

        # Sunday = rest day
        if current_date.weekday() == 6:
            plan.append({
                'day': day,
                'date': current_date.isoformat(),
                'focus_area': 'Repos — Recharge mentale',
                'activity_type': 'rest',
                'duration_minutes': 0,
                'subject_ids': [],
                'priority': 'low'
            })
            continue

        # Score: weight * days since last review (Ebbinghaus-inspired)
        scores = {s: weights[s] * (day - last_reviewed[s]) for s in all_subjects}
        focus = max(scores, key=lambda s: scores[s])
        rc = review_count[focus]
        days_to_exam = days_remaining - day

        if days_to_exam < 7:
            activity = 'quiz'
        elif rc == 0:
            activity = 'revision'
        elif rc == 1:
            activity = 'quiz'
        elif rc == 2:
            activity = 'adaptive_quiz'
        else:
            activity = 'quiz' if rc % 2 == 1 else 'adaptive_quiz'

        priority = (
            'high' if focus in (weak_areas or [])[:3]
            else ('medium' if focus in (weak_areas or []) else 'low')
        )

        plan.append({
            'day': day,
            'date': current_date.isoformat(),
            'focus_area': focus,
            'activity_type': activity,
            'duration_minutes': duration_min,
            'subject_ids': [],
            'priority': priority
        })
        last_reviewed[focus] = day
        review_count[focus] += 1

    # Group into weeks
    weeks = []
    for i in range(0, len(plan), 7):
        weeks.append({'week': i // 7 + 1, 'days': plan[i:i + 7]})

    confidence = round(
        min(
            0.95,
            0.5
            + (len(weak_areas or []) > 0) * 0.15
            + (days_remaining > 30) * 0.2
            + (hours_per_day >= 2) * 0.1,
        ),
        2,
    )

    return {
        'exam_info': {
            'exam_type': exam_type,
            'exam_date': exam_date_str,
            'days_remaining': days_remaining,
            'hours_per_day': hours_per_day,
            'focus_subjects': all_subjects[:5],
        },
        'total_days': days_remaining,
        'weeks': weeks,
        'confidence_score': confidence,
    }


# ── Endpoint 1: Generate ──────────────────────────────────────────────────────

@exam_coach_router.post('/generate')
async def generate_plan(
    body: GeneratePlanRequest,
    current_user: UserTokenData = Depends(verify_token),
):
    """Génère un plan de révision Ebbinghaus personnalisé et le stocke en BD."""
    try:
        exam_dt = date.fromisoformat(body.exam_date)
    except ValueError:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="exam_date invalide — format attendu: YYYY-MM-DD",
        )

    days_remaining = (exam_dt - date.today()).days
    if days_remaining <= 0:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="La date d'examen doit être dans le futur.",
        )

    # Fetch user performance data
    weak_areas: list = []
    enrolled_subjects: list = []
    try:
        db = Database()
        nlp = NLPAnalyzer()
        recommender = Recommender(db)
        performance_analyzer = UserPerformanceAnalyzer(db=db, nlp_analyzer=nlp, recommender=recommender)
        progress = performance_analyzer.analyze_user_progress(body.user_id)
        if progress.get('success'):
            weak_areas = progress.get('analysis', {}).get('weak_areas', [])
            enrolled_subjects = [
                e.get('subject_title', '')
                for e in (progress.get('overview', {}).get('enrollments') or [])
                if e.get('subject_title')
            ]
    except Exception as exc:
        logger.warning(f"[exam-coach] Could not fetch user performance for {body.user_id}: {exc}")

    plan = _generate_plan(
        body.user_id,
        body.exam_type,
        body.exam_date,
        body.hours_per_day,
        weak_areas,
        enrolled_subjects,
    )

    # Persist to DB
    session = None
    try:
        db = Database()
        session = db.SessionLocal()
        record = ExamCoachPlanAI(
            UserId=body.user_id,
            ExamType=body.exam_type,
            ExamDate=datetime.fromisoformat(body.exam_date),
            HoursPerDay=body.hours_per_day,
            PlanJson=json.dumps(plan),
            ConfidenceScore=plan['confidence_score'],
        )
        session.add(record)
        session.commit()
        session.refresh(record)
        plan_id = record.Id
        logger.info(f"[exam-coach] Plan {plan_id} created for user {body.user_id}")
    except Exception as exc:
        logger.error(f"[exam-coach] DB write failed: {exc}")
        if session:
            session.rollback()
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail="Erreur lors de la sauvegarde du plan.",
        )
    finally:
        if session:
            session.close()

    plan['plan_id'] = plan_id
    return {'success': True, 'data': plan}


# ── Endpoint 2: Recalibrate ───────────────────────────────────────────────────

@exam_coach_router.post('/recalibrate')
async def recalibrate_plan(
    body: RecalibrateRequest,
    current_user: UserTokenData = Depends(verify_token),
):
    """Recalibre un plan existant à partir des résultats de la semaine écoulée."""
    session = None
    try:
        db = Database()
        session = db.SessionLocal()
        record = session.query(ExamCoachPlanAI).filter(
            ExamCoachPlanAI.Id == body.plan_id,
            ExamCoachPlanAI.IsActive == True,
        ).first()

        if not record:
            raise HTTPException(
                status_code=status.HTTP_404_NOT_FOUND,
                detail="Plan introuvable.",
            )
        if record.UserId != body.user_id:
            raise HTTPException(
                status_code=status.HTTP_403_FORBIDDEN,
                detail="Ce plan ne vous appartient pas.",
            )

        plan = json.loads(record.PlanJson)

        # Determine completion rate and struggling subjects
        completed_count = sum(1 for r in body.week_results if r.activity_completed)
        completion_rate = completed_count / len(body.week_results) if body.week_results else 0

        # Identify subjects from poor results
        weak_boost: list = []
        all_days_flat = [d for w in plan.get('weeks', []) for d in w.get('days', [])]

        for result in body.week_results:
            day_entry = next((d for d in all_days_flat if d['day'] == result.day), None)
            if day_entry:
                subject = day_entry.get('focus_area', '')
                if subject and subject != 'Repos — Recharge mentale':
                    poor_score = (
                        result.quiz_score_if_applicable is not None
                        and result.quiz_score_if_applicable < 60
                    )
                    if poor_score or not result.activity_completed:
                        if subject not in weak_boost:
                            weak_boost.append(subject)

        # Find which week was just completed
        today_day = (date.today() - datetime.fromisoformat(
            plan['exam_info']['exam_date']
        ).date()).days
        # completed_days = highest day number in week_results
        completed_days = max((r.day for r in body.week_results), default=0)

        # Re-generate remaining days
        exam_date_str = plan['exam_info']['exam_date']
        hours_per_day = float(record.HoursPerDay)
        exam_type = record.ExamType

        existing_weak = plan['exam_info'].get('focus_subjects', [])
        merged_weak = list(dict.fromkeys(weak_boost + existing_weak))

        remaining_days = (date.fromisoformat(exam_date_str) - date.today()).days
        if remaining_days > 0:
            new_partial = _generate_plan(
                body.user_id,
                exam_type,
                exam_date_str,
                hours_per_day,
                merged_weak,
                plan['exam_info'].get('focus_subjects', []),
            )

            # Offset new days so they start after completed_days
            offset = completed_days
            for week in new_partial.get('weeks', []):
                for d in week.get('days', []):
                    d['day'] += offset

            # Keep past weeks, replace future weeks
            past_weeks = []
            for w in plan.get('weeks', []):
                past_days = [d for d in w.get('days', []) if d['day'] <= completed_days]
                if past_days:
                    past_weeks.append({'week': w['week'], 'days': past_days})

            # Renumber future weeks
            future_weeks = new_partial.get('weeks', [])
            week_offset = len(past_weeks)
            for i, w in enumerate(future_weeks):
                w['week'] = week_offset + i + 1

            plan['weeks'] = past_weeks + future_weeks
            plan['confidence_score'] = new_partial['confidence_score']
            plan['exam_info']['focus_subjects'] = new_partial['exam_info']['focus_subjects']

        record.PlanJson = json.dumps(plan)
        record.ConfidenceScore = plan['confidence_score']
        session.commit()
        logger.info(
            f"[exam-coach] Plan {body.plan_id} recalibrated. "
            f"Completion rate: {completion_rate:.0%}. Boosted: {weak_boost}"
        )
        return {'success': True, 'data': plan}

    except HTTPException:
        raise
    except Exception as exc:
        logger.error(f"[exam-coach] Recalibrate error: {exc}")
        if session:
            session.rollback()
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=str(exc),
        )
    finally:
        if session:
            session.close()


# ── Endpoint 3: Today ─────────────────────────────────────────────────────────

@exam_coach_router.get('/today/{user_id}')
async def get_today_session(
    user_id: int,
    current_user: UserTokenData = Depends(verify_token),
):
    """Retourne la session du jour avec un message motivant WinAI."""
    session = None
    try:
        db = Database()
        session = db.SessionLocal()
        record = session.query(ExamCoachPlanAI).filter(
            ExamCoachPlanAI.UserId == user_id,
            ExamCoachPlanAI.IsActive == True,
        ).order_by(ExamCoachPlanAI.CreatedAt.desc()).first()

        if not record:
            raise HTTPException(
                status_code=status.HTTP_404_NOT_FOUND,
                detail="Aucun plan actif trouvé. Génère un plan d'abord.",
            )

        plan = json.loads(record.PlanJson)
        created_date = record.CreatedAt.date() if hasattr(record.CreatedAt, 'date') else date.today()
        today_day_number = (date.today() - created_date).days + 1
        exam_type = record.ExamType

        # Find today's day entry across all weeks
        today_entry = None
        for week in plan.get('weeks', []):
            for d in week.get('days', []):
                if d['day'] == today_day_number:
                    today_entry = d
                    break
            if today_entry:
                break

        if not today_entry:
            # Plan may have ended or not started yet
            raise HTTPException(
                status_code=status.HTTP_404_NOT_FOUND,
                detail="Pas de session planifiée pour aujourd'hui.",
            )

        focus_area = today_entry.get('focus_area', 'Révision générale')
        activity_type = today_entry.get('activity_type', 'revision')
        duration_minutes = today_entry.get('duration_minutes', 60)
        days_remaining = (
            date.fromisoformat(plan['exam_info']['exam_date']) - date.today()
        ).days

        # Generate WinAI motivational message (non-streaming)
        winai_message = ""
        try:
            client = get_deepseek_client()
            prompt = (
                f"Tu es WinAI, coach IA de WinPlus. "
                f"Message motivant court (2-3 phrases max) pour un étudiant camerounais "
                f"qui prépare {exam_type} dans {days_remaining} jours. "
                f"Aujourd'hui il révise {focus_area} ({activity_type}). "
                f"Message encourageant et personnalisé, en français."
            )
            result = client.chat(
                messages=[{'role': 'user', 'content': prompt}],
                max_tokens=120,
                temperature=0.7,
            )
            winai_message = result.get('content', '').strip()
        except Exception as exc:
            logger.warning(f"[exam-coach] DeepSeek motivational message failed: {exc}")
            winai_message = f"Continue comme ça ! Chaque heure de révision te rapproche de la réussite. Bon courage pour {focus_area} aujourd'hui !"

        return {
            'success': True,
            'data': {
                'activity': today_entry,
                'focus_area': focus_area,
                'duration_minutes': duration_minutes,
                'subject': focus_area,
                'is_completed': False,
                'winai_message': winai_message,
                'day_number': today_day_number,
                'days_remaining': days_remaining,
            }
        }

    except HTTPException:
        raise
    except Exception as exc:
        logger.error(f"[exam-coach] Today session error: {exc}")
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=str(exc),
        )
    finally:
        if session:
            session.close()
