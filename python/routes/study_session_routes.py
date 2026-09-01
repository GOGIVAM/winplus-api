"""
WinAI  Guided Study Sessions
POST /api/study-session/generate        Generate briefing (phase 1)
POST /api/study-session/phase2/quiz     Generate quiz questions (phase 2)
POST /api/study-session/phase3/summary  Generate synthesis summary (phase 3)
POST /api/study-session/complete        Save completed session to DB
"""

import json
import logging
from datetime import datetime
from typing import Optional, List

from fastapi import APIRouter, Depends, HTTPException, status
from pydantic import BaseModel

from auth import verify_token, UserTokenData
from database import Database, StudySession, Subject, Enrollment, QuizAttempt, DailyScore
from services.deepseek_client import get_deepseek_client

logger = logging.getLogger(__name__)

study_session_router = APIRouter()


class GenerateSessionRequest(BaseModel):
    user_id: int
    subject_id: int
    duration_minutes: int  # 15 | 25 | 45 | 60


class Phase2QuizRequest(BaseModel):
    user_id: int
    subject_id: int


class Phase3SummaryRequest(BaseModel):
    user_id: int
    subject_id: int
    score: Optional[float] = None  # 0-100, score du quiz phase 2


class CompleteSessionRequest(BaseModel):
    user_id: int
    subject_id: int
    duration_minutes: int
    score: Optional[float] = None  # 0-100
    key_points: Optional[List[str]] = None


@study_session_router.post('/generate')
async def generate_session(
    body: GenerateSessionRequest,
    current_user: UserTokenData = Depends(verify_token),
):
    """
    Génère le contenu d'une session guidée en 3 phases:
    1. Briefing (recap derniers concepts)
    2. Quiz progressif (5 questions easy→hard)
    3. Prompt pour la synthèse finale
    """
    db_obj = Database()
    session = db_obj.SessionLocal()
    try:
        subject = session.query(Subject).filter(Subject.Id == body.subject_id).first()
        if not subject:
            raise HTTPException(status_code=404, detail="Sujet introuvable.")

        subject_name = subject.Title
        subject_category = subject.Category or 'général'

        # Get past performance on this subject
        attempts = session.query(QuizAttempt).filter(
            QuizAttempt.UserId == body.user_id
        ).order_by(QuizAttempt.CreatedAt.desc()).limit(10).all()
        past_scores = [float(a.Score) for a in attempts if a.Score is not None]
        avg_score = sum(past_scores) / len(past_scores) if past_scores else 50.0
        level = 'débutant' if avg_score < 50 else ('intermédiaire' if avg_score < 75 else 'avancé')

        client = get_deepseek_client()

        # Phase 1: Briefing
        briefing_prompt = (
            f"Tu es WinAI, tuteur pour un étudiant camerounais (niveau {level}) "
            f"en {subject_name} ({subject_category}). "
            f"Génère un briefing de révision de 3-4 phrases qui: "
            f"(1) rappelle les 2-3 concepts clés du sujet, "
            f"(2) mentionne les points importants à l'examen. "
            f"Sois direct, motivant, en français."
        )
        briefing_result = client.chat(
            messages=[{'role': 'user', 'content': briefing_prompt}],
            max_tokens=200,
            temperature=0.6,
        )
        briefing = briefing_result.get('content', '').strip()

        # Phase 2: 5 quiz questions (easy to hard)
        quiz_prompt = (
            f"Génère exactement 5 questions QCM sur {subject_name} pour un étudiant de niveau {level}. "
            f"Questions 1-2: facile, 3-4: moyen, 5: difficile. "
            f"Format JSON strict: "
            f'[{{"q":"question","options":["A)...","B)...","C)...","D)..."],"correct":0,"explanation":"..."}},...] '
            f"correct est l'index (0-3) de la bonne réponse. "
            f"Réponds UNIQUEMENT avec le tableau JSON, sans texte autour."
        )
        quiz_result = client.chat(
            messages=[{'role': 'user', 'content': quiz_prompt}],
            max_tokens=800,
            temperature=0.4,
        )
        questions_raw = quiz_result.get('content', '').strip()
        questions = []
        try:
            import re
            match = re.search(r'\[.*\]', questions_raw, re.DOTALL)
            if match:
                questions = json.loads(match.group())
        except Exception:
            questions = []

        # Phase 3: Synthesis prompt (will be streamed later)
        synthesis_prompt_text = (
            f"À la fin de la session d'étude sur {subject_name}, "
            f"génère une synthèse de 3 points clés à retenir, "
            f"adaptée à un niveau {level}, en français."
        )

        return {
            'success': True,
            'data': {
                'subject': {'id': body.subject_id, 'name': subject_name, 'category': subject_category},
                'duration_minutes': body.duration_minutes,
                'level': level,
                'briefing': briefing,
                'questions': questions,
                'synthesis_prompt': synthesis_prompt_text,
                'phases': [
                    {'id': 'briefing', 'label': 'Briefing', 'duration_pct': 15},
                    {'id': 'quiz', 'label': 'Quiz progressif', 'duration_pct': 65},
                    {'id': 'synthesis', 'label': 'Synthèse WinAI', 'duration_pct': 20},
                ],
            }
        }
    except HTTPException:
        raise
    except Exception as exc:
        logger.error(f"[study-session] generate error: {exc}")
        raise HTTPException(status_code=500, detail=str(exc))
    finally:
        session.close()


@study_session_router.post('/phase2/quiz')
async def generate_phase2_quiz(
    body: Phase2QuizRequest,
    current_user: UserTokenData = Depends(verify_token),
):
    """
    Phase 2 — Quiz interactif : génère 3 questions QCM via DeepSeek
    basées sur le sujet de la session.
    """
    db_obj = Database()
    session = db_obj.SessionLocal()
    try:
        subject = session.query(Subject).filter(Subject.Id == body.subject_id).first()
        if not subject:
            raise HTTPException(status_code=404, detail="Sujet introuvable.")

        subject_name = subject.Title
        subject_category = subject.Category or 'général'

        # Determine level from past attempts
        attempts = session.query(QuizAttempt).filter(
            QuizAttempt.UserId == body.user_id
        ).order_by(QuizAttempt.CreatedAt.desc()).limit(10).all()
        past_scores = [float(a.Score) for a in attempts if a.Score is not None]
        avg_score = sum(past_scores) / len(past_scores) if past_scores else 50.0
        level = 'débutant' if avg_score < 50 else ('intermédiaire' if avg_score < 75 else 'avancé')

        client = get_deepseek_client()
        quiz_prompt = (
            f"Génère exactement 3 questions QCM sur {subject_name} ({subject_category}) "
            f"pour un étudiant camerounais de niveau {level}. "
            f"Réponds UNIQUEMENT avec un tableau JSON valide, sans texte autour :\n"
            f'[{{"question": "Énoncé de la question ?", '
            f'"options": ["A) Option A", "B) Option B", "C) Option C", "D) Option D"], '
            f'"correct": "A) Option A", '
            f'"explanation": "Explication de la bonne réponse."}}]'
        )
        result = client.chat(
            messages=[{'role': 'user', 'content': quiz_prompt}],
            max_tokens=600,
            temperature=0.4,
        )
        questions_raw = result.get('content', '').strip()
        questions = []
        try:
            import re
            match = re.search(r'\[.*\]', questions_raw, re.DOTALL)
            if match:
                questions = json.loads(match.group())
        except Exception:
            questions = []

        # Fallback si DeepSeek ne retourne pas un JSON valide
        if not questions:
            questions = [
                {
                    "question": f"Quelle est une notion fondamentale en {subject_name} ?",
                    "options": ["A) Notion A", "B) Notion B", "C) Notion C", "D) Notion D"],
                    "correct": "A) Notion A",
                    "explanation": "Réponse générée automatiquement — relancez pour un quiz personnalisé.",
                }
            ]

        return {
            'success': True,
            'phase2': {
                'questions': questions[:3],
                'subject': subject_name,
                'level': level,
            }
        }
    except HTTPException:
        raise
    except Exception as exc:
        logger.error(f"[study-session] phase2 quiz error: {exc}")
        raise HTTPException(status_code=500, detail=str(exc))
    finally:
        session.close()


@study_session_router.post('/phase3/summary')
async def generate_phase3_summary(
    body: Phase3SummaryRequest,
    current_user: UserTokenData = Depends(verify_token),
):
    """
    Phase 3 — Synthèse : génère un résumé des points clés (3-5 bullet points)
    et, si un score est fourni, commente la performance de l'étudiant.
    """
    db_obj = Database()
    session = db_obj.SessionLocal()
    try:
        subject = session.query(Subject).filter(Subject.Id == body.subject_id).first()
        if not subject:
            raise HTTPException(status_code=404, detail="Sujet introuvable.")

        subject_name = subject.Title
        subject_category = subject.Category or 'général'

        # Determine level from past attempts
        attempts = session.query(QuizAttempt).filter(
            QuizAttempt.UserId == body.user_id
        ).order_by(QuizAttempt.CreatedAt.desc()).limit(10).all()
        past_scores = [float(a.Score) for a in attempts if a.Score is not None]
        avg_score = sum(past_scores) / len(past_scores) if past_scores else 50.0
        level = 'débutant' if avg_score < 50 else ('intermédiaire' if avg_score < 75 else 'avancé')

        score_context = ""
        if body.score is not None:
            if body.score >= 80:
                perf_comment = f"Excellent résultat ({body.score:.0f}/100) ! Continue sur cette lancée."
            elif body.score >= 60:
                perf_comment = f"Bon travail ({body.score:.0f}/100). Quelques points à renforcer."
            else:
                perf_comment = f"Score de {body.score:.0f}/100 — pas d'inquiétude, chaque erreur est une occasion d'apprendre."
            score_context = (
                f"Le score du quiz de l'étudiant est {body.score:.0f}/100. "
                f"Inclus une phrase de commentaire sur sa performance : \"{perf_comment}\""
            )

        client = get_deepseek_client()
        synthesis_prompt = (
            f"Tu es WinAI, tuteur pour un étudiant camerounais de niveau {level} en {subject_name} ({subject_category}). "
            f"Génère une synthèse de fin de session. {score_context} "
            f"Réponds UNIQUEMENT avec un objet JSON valide :\n"
            f'{{"summary": "Paragraphe de synthèse de 2-3 phrases résumant la session.", '
            f'"keypoints": ["Point clé 1", "Point clé 2", "Point clé 3", "Point clé 4", "Point clé 5"]}}\n'
            f"Les keypoints doivent être 3 à 5 bullet points essentiels sur {subject_name}."
        )
        result = client.chat(
            messages=[{'role': 'user', 'content': synthesis_prompt}],
            max_tokens=400,
            temperature=0.5,
        )
        synthesis_raw = result.get('content', '').strip()
        synthesis_data = {}
        try:
            import re
            match = re.search(r'\{.*\}', synthesis_raw, re.DOTALL)
            if match:
                synthesis_data = json.loads(match.group())
        except Exception:
            synthesis_data = {}

        summary = synthesis_data.get('summary', f"Session sur {subject_name} terminée. Revois les points clés régulièrement pour consolider tes acquis.")
        keypoints = synthesis_data.get('keypoints', [f"Révise les fondamentaux de {subject_name} régulièrement."])

        performance_comment = ""
        if body.score is not None:
            if body.score >= 80:
                performance_comment = f"Excellent résultat : {body.score:.0f}/100. Tu maîtrises bien ce sujet !"
            elif body.score >= 60:
                performance_comment = f"Bon résultat : {body.score:.0f}/100. Continue à pratiquer pour progresser."
            else:
                performance_comment = f"Score de {body.score:.0f}/100. Concentre-toi sur les points clés ci-dessus."

        return {
            'success': True,
            'phase3': {
                'summary': summary,
                'keypoints': keypoints[:5],
                'performance_comment': performance_comment,
                'subject': subject_name,
            }
        }
    except HTTPException:
        raise
    except Exception as exc:
        logger.error(f"[study-session] phase3 summary error: {exc}")
        raise HTTPException(status_code=500, detail=str(exc))
    finally:
        session.close()


@study_session_router.post('/complete')
async def complete_session(
    body: CompleteSessionRequest,
    current_user: UserTokenData = Depends(verify_token),
):
    """
    Enregistre une session d'étude terminée et met à jour le score journalier.
    """
    db_obj = Database()
    session = db_obj.SessionLocal()
    try:
        record = StudySession(
            UserId=body.user_id,
            SubjectId=body.subject_id,
            Duration=body.duration_minutes,
            Score=body.score,
            KeyPoints=json.dumps(body.key_points or []),
            CompletedAt=datetime.utcnow(),
        )
        session.add(record)

        # Update DailyScore if score provided
        if body.score is not None:
            today = datetime.utcnow().date()
            existing = session.query(DailyScore).filter(
                DailyScore.UserId == body.user_id,
                DailyScore.Date == today,
            ).first()
            if existing:
                existing.AverageScore = (float(existing.AverageScore) + body.score) / 2
                existing.QuizCount += 1
                existing.UpdatedAt = datetime.utcnow()
            else:
                session.add(DailyScore(
                    UserId=body.user_id,
                    Date=today,
                    AverageScore=body.score,
                    QuizCount=1,
                    SubjectId=body.subject_id,
                ))

        session.commit()
        logger.info(f"[study-session] User {body.user_id} completed session on subject {body.subject_id}, score={body.score}")
        return {'success': True, 'session_id': record.Id}
    except Exception as exc:
        logger.error(f"[study-session] complete error: {exc}")
        session.rollback()
        raise HTTPException(status_code=500, detail=str(exc))
    finally:
        session.close()


@study_session_router.get('/history/{user_id}')
async def get_session_history(
    user_id: int,
    current_user: UserTokenData = Depends(verify_token),
):
    """Retourne les 10 dernières sessions d'étude d'un utilisateur."""
    db_obj = Database()
    session = db_obj.SessionLocal()
    try:
        sessions = session.query(StudySession, Subject).join(
            Subject, StudySession.SubjectId == Subject.Id
        ).filter(
            StudySession.UserId == user_id,
            StudySession.CompletedAt.isnot(None),
        ).order_by(StudySession.CompletedAt.desc()).limit(10).all()

        return {
            'success': True,
            'data': [
                {
                    'id': s.StudySession.Id,
                    'subject': s.Subject.Title,
                    'duration': s.StudySession.Duration,
                    'score': float(s.StudySession.Score) if s.StudySession.Score else None,
                    'completedAt': s.StudySession.CompletedAt.isoformat() if s.StudySession.CompletedAt else None,
                }
                for s in sessions
            ]
        }
    except Exception as exc:
        logger.error(f"[study-session] history error: {exc}")
        raise HTTPException(status_code=500, detail=str(exc))
    finally:
        session.close()
