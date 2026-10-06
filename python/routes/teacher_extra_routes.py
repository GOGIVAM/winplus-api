"""
WinAI  Endpoints IA pour le compte Professeur.

- POST /ai/generate-quiz-questions   → Génération de questions calibrées (subject+level)
- POST /ai/optimize-title            → Optimisation SEO du titre de contenu
- POST /ai/generate-description      → Description catalogue 2-3 phrases
- POST /teacher/class-analysis       → Analyse collective des apprenants d'un contenu
- GET  /teacher/content-impact/{id}  → Score d'impact pédagogique d'un contenu
- POST /teacher/generate-correction  → Correction IA d'une épreuve question/question
- POST /teacher/predict-popularity   → Prédiction de popularité avant publication
- POST /teacher/analyze-submission   → Analyse d'une soumission d'élève
"""

import json
import logging
import os
from datetime import datetime, timedelta, timezone
from typing import Any, List, Optional

from fastapi import APIRouter, Depends, HTTPException
from pydantic import BaseModel
from sqlalchemy import func

# Module 20 : la vérification d'authentification était présente partout, la
# vérification de RÔLE nulle part, alors que l'assistant require_role existe
# déjà et est utilisé dans admin_routes.py. Un élève authentifié pouvait donc
# appeler la génération de corrigé, l'analyse de soumission ou le rapport de
# coaching de n'importe quel professeur.
#
# Exception documentée : /messaging/quick-replies et /messaging/generate-reply
# restent ouverts à tout compte authentifié. Ce sont des aides à la rédaction
# de message génériques, appelées par la messagerie web pour TOUS les rôles
# (MessagesPage les déclenche au chargement d'une conversation, sans condition
# de rôle) : les restreindre au professeur casserait la messagerie de l'élève
# et du parent. Seul /messaging/parent-report, réservé au professeur dans
# l'interface, est passé sous contrôle de rôle.
from auth import verify_token, require_role, require_user_or_service, UserTokenData
from database import (
    Database, QuizAttempt, DailyScore, Enrollment, Subject, User,
    TutorProfile, TutorSubject, TutorLevel, CourseContent, Order, OrderItem,
    DownloadHistory,
)
from services.deepseek_client import get_deepseek_client

logger = logging.getLogger(__name__)

teacher_ai_router = APIRouter()

MONTHS_FR = [
    "", "janvier", "février", "mars", "avril", "mai", "juin",
    "juillet", "août", "septembre", "octobre", "novembre", "décembre",
]
EXAM_PEAK_MONTHS = {
    "bac":  [2, 3, 4, 5],
    "ens":  [1, 2, 3],
    "bts":  [3, 4, 5],
    "concours": [1, 2, 3, 4],
}


# ─────────────────────────────────────────────────────────────────────────────
# Helpers
# ─────────────────────────────────────────────────────────────────────────────

def _deepseek_json(prompt: str, system: str, max_tokens: int = 600) -> Any:
    try:
        ds = get_deepseek_client()
        res = ds.chat(
            messages=[{"role": "user", "content": prompt}],
            system_prompt=system,
            max_tokens=max_tokens,
            temperature=0.5,
            json_mode=True,
        )
        raw = res.get("content", "").strip()
        if raw.startswith("```"):
            raw = "\n".join(raw.split("\n")[1:])
        if raw.endswith("```"):
            raw = raw.rsplit("```", 1)[0].strip()
        return json.loads(raw)
    except Exception as e:
        logger.warning(f"_deepseek_json parse error: {e}")
        return None


def _deepseek_text(prompt: str, system: str, max_tokens: int = 200) -> str:
    try:
        ds = get_deepseek_client()
        res = ds.chat(
            messages=[{"role": "user", "content": prompt}],
            system_prompt=system,
            max_tokens=max_tokens,
            temperature=0.7,
        )
        return res.get("content", "").strip()
    except Exception as e:
        logger.warning(f"_deepseek_text error: {e}")
        return ""


# ─────────────────────────────────────────────────────────────────────────────
# Feature 1a  POST /ai/generate-quiz-questions
# Accepts {topic?, subject?, level?, topics?}  returns 10 calibrated QCM
# ─────────────────────────────────────────────────────────────────────────────

class QuizOptionOut(BaseModel):
    id: str
    text: str

class QuizQuestionOut(BaseModel):
    id: str
    text: str
    options: List[QuizOptionOut]
    correctOptionId: str
    explanation: str

class GenerateQuizRequest(BaseModel):
    topic: Optional[str] = None
    subject: Optional[str] = None
    level: Optional[str] = None
    topics: Optional[List[str]] = None

class GenerateQuizResponse(BaseModel):
    questions: List[QuizQuestionOut]

@teacher_ai_router.post("/ai/generate-quiz-questions", response_model=GenerateQuizResponse)
async def generate_quiz_questions(
    body: GenerateQuizRequest,
    current_user: UserTokenData = Depends(require_role("teacher", "admin")),
):
    topic_str = body.topic or (", ".join(body.topics) if body.topics else "")
    subject_str = body.subject or ""
    level_str = body.level or ""

    prompt = (
        f"Génère exactement 10 questions QCM de niveau {level_str} en {subject_str} "
        f"sur le thème : {topic_str}. "
        "Chaque question doit avoir 4 options (A/B/C/D), une seule correcte. "
        "Format JSON : tableau de 10 objets avec les champs exactement : "
        '{"id":"q1","text":"...","options":[{"id":"a","text":"..."},{"id":"b","text":"..."},{"id":"c","text":"..."},{"id":"d","text":"..."}],"correctOptionId":"a","explanation":"..."} '
        "Les options doivent être plausibles, l'explication doit justifier la bonne réponse. "
        "Réponds avec UNIQUEMENT le tableau JSON, rien d'autre."
    )
    system = (
        "Tu es WinAI, expert en création de QCM pédagogiques pour les examens africains. "
        "Réponds uniquement avec un JSON valide : un tableau de 10 objets."
    )

    raw = _deepseek_json(prompt, system, max_tokens=3000)

    questions: List[QuizQuestionOut] = []
    if isinstance(raw, list):
        for i, q in enumerate(raw[:10]):
            try:
                questions.append(QuizQuestionOut(
                    id=str(q.get("id", f"q{i+1}")),
                    text=str(q.get("text", "")),
                    options=[QuizOptionOut(id=str(o["id"]), text=str(o["text"])) for o in q.get("options", [])],
                    correctOptionId=str(q.get("correctOptionId", "a")),
                    explanation=str(q.get("explanation", "")),
                ))
            except Exception:
                pass

    if not questions:
        questions = [QuizQuestionOut(
            id=f"q{i+1}",
            text=f"Question {i+1} sur {topic_str or 'le sujet'}",
            options=[
                QuizOptionOut(id="a", text="Option A"),
                QuizOptionOut(id="b", text="Option B"),
                QuizOptionOut(id="c", text="Option C"),
                QuizOptionOut(id="d", text="Option D"),
            ],
            correctOptionId="a",
            explanation="WinAI n'a pas pu générer les questions  réessayez ou formulez le sujet différemment.",
        ) for i in range(5)]

    return {"questions": questions}


# ─────────────────────────────────────────────────────────────────────────────
# Feature 1b  POST /ai/optimize-title
# ─────────────────────────────────────────────────────────────────────────────

class OptimizeTitleRequest(BaseModel):
    title: str
    subject: Optional[str] = None
    level: Optional[str] = None
    type: Optional[str] = None

class OptimizeTitleResponse(BaseModel):
    optimized_title: str
    rationale: str

@teacher_ai_router.post("/ai/optimize-title", response_model=OptimizeTitleResponse)
async def optimize_title(
    body: OptimizeTitleRequest,
    current_user: UserTokenData = Depends(require_role("teacher", "admin")),
):
    type_labels = {
        "epreuve": "Épreuve", "correction": "Corrigé", "quiz": "Quiz",
        "livre": "Manuel", "pack": "Pack",
    }
    prompt = (
        f'Titre actuel : "{body.title}"\n'
        f'Matière : {body.subject or "non précisée"}\n'
        f'Niveau : {body.level or "non précisé"}\n'
        f'Type : {type_labels.get(body.type or "", "Contenu")}\n\n'
        "Propose un titre plus attractif, précis et mieux référencé pour le catalogue WinPlus. "
        "Le titre doit : indiquer clairement matière + niveau + type + année si pertinent. "
        "Exemple de bon titre : « Épreuves BAC C Mathématiques 2023  Probabilités et Analyse ».\n"
        'Format JSON strict : {"optimized_title":"...","rationale":"..."}'
    )
    system = (
        "Tu es WinAI, expert éditorial pour plateformes éducatives africaines. "
        "Réponds uniquement avec du JSON valide, sans markdown."
    )

    raw = _deepseek_json(prompt, system, max_tokens=200)
    if raw and isinstance(raw, dict) and raw.get("optimized_title"):
        return {
            "optimized_title": raw["optimized_title"],
            "rationale": raw.get("rationale", "Titre optimisé pour le référencement catalogue."),
        }

    type_label = type_labels.get(body.type or "", "Contenu")
    subject_part = f" {body.subject}" if body.subject else ""
    level_part = f" {body.level}" if body.level else ""
    return {
        "optimized_title": f"{type_label}{subject_part}{level_part}  {body.title}",
        "rationale": "Titre enrichi avec le type et le niveau pour une meilleure visibilité.",
    }


# ─────────────────────────────────────────────────────────────────────────────
# Feature 1c  POST /ai/generate-description
# ─────────────────────────────────────────────────────────────────────────────

class GenerateDescriptionRequest(BaseModel):
    title: str
    subject: Optional[str] = None
    level: Optional[str] = None
    type: Optional[str] = None
    year: Optional[str] = None
    difficulty: Optional[str] = None

class GenerateDescriptionResponse(BaseModel):
    description: str

@teacher_ai_router.post("/ai/generate-description", response_model=GenerateDescriptionResponse)
async def generate_description(
    body: GenerateDescriptionRequest,
    current_user: UserTokenData = Depends(require_role("teacher", "admin")),
):
    type_labels = {
        "epreuve": "épreuve", "correction": "corrigé", "quiz": "quiz",
        "livre": "manuel", "pack": "pack",
    }
    diff_labels = {"easy": "accessible", "medium": "intermédiaire", "hard": "avancé"}

    prompt = (
        "Génère une description courte (2-3 phrases max, 120 mots max) pour ce contenu éducatif sur WinPlus :\n"
        f"Titre : {body.title}\n"
        f"Type : {type_labels.get(body.type or '', 'contenu')}\n"
        f"Matière : {body.subject or 'non précisée'}\n"
        f"Niveau : {body.level or 'non précisé'}\n"
        f"Année : {body.year or 'non précisée'}\n"
        f"Difficulté : {diff_labels.get(body.difficulty or '', 'standard')}\n\n"
        "La description doit : présenter le contenu, préciser ce que l'élève va apprendre, "
        "et mentionner le niveau ciblé. Ton professionnel et concis. "
        "Ne mentionne pas de prix. Réponds directement avec le texte de description."
    )
    system = (
        "Tu es WinAI, rédacteur de fiches pédagogiques pour WinPlus. "
        "Réponds en français, 2-3 phrases, sans guillemets."
    )

    description = _deepseek_text(prompt, system, max_tokens=150)

    if not description:
        tl = type_labels.get(body.type or "", "contenu")
        description = (
            f"Ce {tl} de {body.subject or 'niveau'} {body.level or ''} "
            f"couvre les points essentiels du programme. "
            f"Idéal pour les élèves préparant l'examen de {body.year or 'cette année'}."
        )

    return {"description": description[:500]}


# ─────────────────────────────────────────────────────────────────────────────
# Feature 2  POST /teacher/class-analysis
# ─────────────────────────────────────────────────────────────────────────────

class HardQuestion(BaseModel):
    question_id: int
    wrong_answer_rate: float
    topic: str

class ClassAnalysisRequest(BaseModel):
    teacher_id: int
    content_id: int

class ClassAnalysisResponse(BaseModel):
    avg_score: float
    hardest_questions: List[HardQuestion]
    common_mistakes: List[str]
    mastery_distribution: dict
    recommended_actions: List[str]
    student_count: int

@teacher_ai_router.post("/teacher/class-analysis", response_model=ClassAnalysisResponse)
async def get_class_analysis(
    body: ClassAnalysisRequest,
    current_user: UserTokenData = Depends(require_role("teacher", "admin")),
):
    db = Database()
    session = db.SessionLocal()
    now = datetime.now(timezone.utc)
    cutoff_90 = now - timedelta(days=90)

    try:
        enrollments = (
            session.query(Enrollment)
            .filter(Enrollment.SubjectId == body.content_id, Enrollment.IsDeleted == False)
            .all()
        )
        student_ids = [e.UserId for e in enrollments]
        student_count = len(student_ids)

        if not student_ids:
            return {
                "avg_score": 0.0,
                "hardest_questions": [],
                "common_mistakes": ["Aucun apprenant inscrit à ce contenu."],
                "mastery_distribution": {"excellent": 0, "good": 0, "struggling": 0, "at_risk": 0},
                "recommended_actions": ["Partagez ce contenu avec vos élèves pour commencer l'analyse."],
                "student_count": 0,
            }

        # Per-student score averages
        scores_by_student: dict[int, list[float]] = {}
        daily_scores = (
            session.query(DailyScore)
            .filter(
                DailyScore.UserId.in_(student_ids),
                DailyScore.SubjectId == body.content_id,
                DailyScore.CreatedAt >= cutoff_90,
            )
            .all()
        )
        # Fallback: all scores for these students (when SubjectId not granular)
        if not daily_scores:
            daily_scores = (
                session.query(DailyScore)
                .filter(DailyScore.UserId.in_(student_ids), DailyScore.CreatedAt >= cutoff_90)
                .all()
            )

        for ds in daily_scores:
            scores_by_student.setdefault(ds.UserId, []).append(float(ds.AverageScore))

        student_avgs = [
            sum(v) / len(v)
            for v in scores_by_student.values()
            if v
        ]
        overall_avg = round(sum(student_avgs) / max(len(student_avgs), 1), 1) if student_avgs else 0.0

        dist = {"excellent": 0, "good": 0, "struggling": 0, "at_risk": 0}
        for avg in student_avgs:
            if avg >= 80:   dist["excellent"] += 1
            elif avg >= 60: dist["good"] += 1
            elif avg >= 40: dist["struggling"] += 1
            else:           dist["at_risk"] += 1
        dist["at_risk"] += student_count - len(student_avgs)  # no-activity students

        # Hardest quiz attempts (low correct/total ratio)
        quiz_attempts = (
            session.query(QuizAttempt)
            .filter(QuizAttempt.UserId.in_(student_ids), QuizAttempt.CompletedAt >= cutoff_90)
            .all()
        )
        seen: set[int] = set()
        hard_questions: list[HardQuestion] = []
        for a in quiz_attempts:
            if a.QuizId and a.QuizId not in seen:
                # Score est déjà un pourcentage (0-100) de bonnes réponses 
                # équivalent à 1 - CorrectAnswers/TotalQuestions sans dépendre
                # d'une colonne TotalQuestions qui n'existe pas réellement.
                wr = round(1.0 - float(a.Score or 0) / 100.0, 2)
                if wr >= 0.5:
                    hard_questions.append(HardQuestion(
                        question_id=int(a.QuizId),
                        wrong_answer_rate=wr,
                        topic="À identifier",
                    ))
                    seen.add(int(a.QuizId))
        hard_questions.sort(key=lambda q: q.wrong_answer_rate, reverse=True)

        # AI insights
        subject = session.query(Subject).filter(Subject.Id == body.content_id).first()
        subject_title = subject.Title if subject else f"contenu #{body.content_id}"
        dist_str = (
            f"{dist['excellent']} excellents, {dist['good']} bons, "
            f"{dist['struggling']} en difficulté, {dist['at_risk']} à risque"
        )
        ai_prompt = (
            f"Classe de {student_count} élèves  contenu « {subject_title} ».\n"
            f"Score moyen : {overall_avg}%\nDistribution : {dist_str}\n"
            f"Nombre de quiz avec taux d'erreur >50% : {len(hard_questions)}\n\n"
            "Génère des insights pédagogiques actionnables :\n"
            '{"common_mistakes":["erreur 1","erreur 2","erreur 3"],'
            '"recommended_actions":["action 1","action 2","action 3"]}'
        )
        ai_system = (
            "Tu es WinAI, assistant pédagogique pour enseignants. "
            "Génère des insights concrets et actionnables en français. "
            "Réponds uniquement avec du JSON valide."
        )
        ai_res = _deepseek_json(ai_prompt, ai_system, max_tokens=400)

        if ai_res and isinstance(ai_res, dict):
            common_mistakes = ai_res.get("common_mistakes", [])[:3]
            recommended_actions = ai_res.get("recommended_actions", [])[:3]
        else:
            common_mistakes = [
                "Lacunes dans les fondamentaux du chapitre.",
                "Confusion entre notions proches (dérivée/primitive, etc.).",
                "Erreurs de signe et de calcul sous pression.",
            ]
            recommended_actions = [
                "Créer un quiz ciblé sur les points les plus échoués.",
                "Publier une correction détaillée commentée.",
                "Organiser une session live de révision pour les élèves à risque.",
            ]

        return {
            "avg_score": overall_avg,
            "hardest_questions": hard_questions[:3],
            "common_mistakes": common_mistakes,
            "mastery_distribution": dist,
            "recommended_actions": recommended_actions,
            "student_count": student_count,
        }

    except Exception as e:
        logger.error(f"class_analysis error for content {body.content_id}: {e}")
        raise HTTPException(status_code=500, detail="Analyse collective indisponible")
    finally:
        session.close()


# ─────────────────────────────────────────────────────────────────────────────
# Feature 3  GET /teacher/content-impact/{content_id}
# ─────────────────────────────────────────────────────────────────────────────

class ContentImpactResponse(BaseModel):
    impact_score: int
    interpretation: str
    completion_rate: float
    avg_score_improvement: float
    student_retention: float
    avg_rating: float

@teacher_ai_router.get("/teacher/content-impact/{content_id}", response_model=ContentImpactResponse)
async def get_content_impact(
    content_id: int,
    current_user: UserTokenData = Depends(require_role("teacher", "admin")),
):
    db = Database()
    session = db.SessionLocal()

    try:
        subject = session.query(Subject).filter(Subject.Id == content_id).first()
        if not subject:
            raise HTTPException(status_code=404, detail="Contenu introuvable")

        enrollments = (
            session.query(Enrollment)
            .filter(Enrollment.SubjectId == content_id, Enrollment.IsDeleted == False)
            .all()
        )
        if not enrollments:
            return {
                "impact_score": 0,
                "interpretation": "Aucun apprenant inscrit  partagez ce contenu pour obtenir votre score d'impact.",
                "completion_rate": 0.0,
                "avg_score_improvement": 0.0,
                "student_retention": 0.0,
                "avg_rating": float(subject.AverageRating or 0),
            }

        student_ids = [e.UserId for e in enrollments]

        # Completion rate
        completed = sum(1 for e in enrollments if e.IsCompleted)
        completion_rate = round(completed / len(enrollments), 2)

        # Score improvement: 30d before vs 30d after enrollment
        improvements: list[float] = []
        for enr in enrollments:
            ea = enr.EnrolledAt
            if not ea:
                continue
            pre = (
                session.query(DailyScore)
                .filter(
                    DailyScore.UserId == enr.UserId,
                    DailyScore.CreatedAt >= ea - timedelta(days=30),
                    DailyScore.CreatedAt < ea,
                )
                .all()
            )
            post = (
                session.query(DailyScore)
                .filter(
                    DailyScore.UserId == enr.UserId,
                    DailyScore.CreatedAt >= ea,
                    DailyScore.CreatedAt < ea + timedelta(days=30),
                )
                .all()
            )
            if pre and post:
                avg_pre = sum(float(s.AverageScore) for s in pre) / len(pre)
                avg_post = sum(float(s.AverageScore) for s in post) / len(post)
                improvements.append(avg_post - avg_pre)

        avg_improvement = round(sum(improvements) / max(len(improvements), 1), 1) if improvements else 0.0

        # Retention: students with >1 quiz attempt
        all_attempts = (
            session.query(QuizAttempt).filter(QuizAttempt.UserId.in_(student_ids)).all()
        )
        attempt_count: dict[int, int] = {}
        for a in all_attempts:
            attempt_count[a.UserId] = attempt_count.get(a.UserId, 0) + 1
        returning = sum(1 for uid in student_ids if attempt_count.get(uid, 0) > 1)
        student_retention = round(returning / max(len(student_ids), 1), 2)

        avg_rating = float(subject.AverageRating or 0)

        # Weighted impact score (max 100)
        score = (
            int(completion_rate * 30)
            + int(min(avg_improvement / 20.0, 1.0) * 30)
            + int(student_retention * 20)
            + int((avg_rating / 5.0) * 20)
        )
        score = min(100, max(0, score))

        if score >= 70:
            interpretation = "Excellent  ce contenu améliore significativement les scores des apprenants."
        elif score >= 50:
            interpretation = "Bon impact  ajoutez des exercices associés pour amplifier les résultats."
        else:
            interpretation = "Impact limité pour l'instant  enrichissez le contenu ou ajoutez un quiz associé."

        return {
            "impact_score": score,
            "interpretation": interpretation,
            "completion_rate": completion_rate,
            "avg_score_improvement": avg_improvement,
            "student_retention": student_retention,
            "avg_rating": avg_rating,
        }

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"content_impact error for {content_id}: {e}")
        raise HTTPException(status_code=500, detail="Score d'impact indisponible")
    finally:
        session.close()


# ─────────────────────────────────────────────────────────────────────────────
# Feature 4  POST /teacher/generate-correction
# ─────────────────────────────────────────────────────────────────────────────

class CorrectionQuestionItem(BaseModel):
    question: str
    correct_answer: str
    explanation: str
    scoring_guide: str

class GenerateCorrectionRequest(BaseModel):
    exam_text: str
    subject: Optional[str] = None
    level: Optional[str] = None

class GenerateCorrectionResponse(BaseModel):
    correction_by_question: List[CorrectionQuestionItem]

@teacher_ai_router.post("/teacher/generate-correction", response_model=GenerateCorrectionResponse)
async def generate_correction(
    body: GenerateCorrectionRequest,
    current_user: UserTokenData = Depends(require_role("teacher", "admin")),
):
    prompt = (
        f"Voici une épreuve de {body.subject or 'mathématiques'} niveau {body.level or 'BAC'}.\n\n"
        f"ÉPREUVE :\n{body.exam_text[:4000]}\n\n"
        "Génère une correction structurée question par question. "
        "Format JSON : tableau d'objets avec exactement ces 4 champs : "
        '{"question":"énoncé court","correct_answer":"réponse complète","explanation":"démarche détaillée","scoring_guide":"barème"} '
        "Sois précis, pédagogique. Utilise LaTeX pour les formules mathématiques ($f(x)=...$). "
        "Réponds UNIQUEMENT avec le tableau JSON."
    )
    system = (
        "Tu es WinAI, expert en correction d'épreuves africaines (BAC, BTS, ENS, Concours). "
        "Génère une correction complète, détaillée et pédagogique. "
        "Réponds uniquement avec un JSON valide : tableau d'objets."
    )

    raw = _deepseek_json(prompt, system, max_tokens=3000)
    corrections: list[CorrectionQuestionItem] = []
    if isinstance(raw, list):
        for item in raw:
            try:
                corrections.append(CorrectionQuestionItem(
                    question=str(item.get("question", "")),
                    correct_answer=str(item.get("correct_answer", "")),
                    explanation=str(item.get("explanation", "")),
                    scoring_guide=str(item.get("scoring_guide", "")),
                ))
            except Exception:
                pass

    if not corrections:
        corrections = [CorrectionQuestionItem(
            question="Épreuve analysée",
            correct_answer="WinAI n'a pas pu structurer la correction. Vérifiez que l'épreuve contient des numéros de questions clairs (Q1, Q2…) et réessayez.",
            explanation="Pour de meilleurs résultats, numérotez clairement chaque question de l'épreuve.",
            scoring_guide="",
        )]

    return {"correction_by_question": corrections}


# ─────────────────────────────────────────────────────────────────────────────
# Feature 5  POST /teacher/predict-popularity
# ─────────────────────────────────────────────────────────────────────────────

class PredictPopularityRequest(BaseModel):
    type: Optional[str] = None
    subject: Optional[str] = None
    level: Optional[str] = None
    year: Optional[str] = None
    price: Optional[str] = None

class PredictPopularityResponse(BaseModel):
    predicted_downloads_30d: int
    price_recommendation: int
    similar_top_sellers: List[str]
    timing_advice: str

@teacher_ai_router.post("/teacher/predict-popularity", response_model=PredictPopularityResponse)
async def predict_popularity(
    body: PredictPopularityRequest,
    current_user: UserTokenData = Depends(require_role("teacher", "admin")),
):
    db = Database()
    session = db.SessionLocal()
    now = datetime.now(timezone.utc)
    current_month = now.month

    try:
        query = (
            session.query(Subject)
            .filter(Subject.IsPublished == True, Subject.IsDeleted == False)
        )
        if body.subject:
            query = query.filter(Subject.Category.ilike(f"%{body.subject}%"))

        similar = query.order_by(Subject.EnrollmentCount.desc()).limit(30).all()

        prices = [float(s.Price) for s in similar if float(s.Price or 0) > 0]
        avg_price = int(sum(prices) / len(prices)) if prices else 2500

        counts = [int(s.EnrollmentCount) for s in similar if s.EnrollmentCount]
        avg_dl = int(sum(counts) / len(counts)) if counts else 15

        top_sellers = [s.Title for s in similar[:5] if s.Title]

        # Timing advice based on level keyword
        level_lower = (body.level or "").lower()
        peak_months: list[int] = []
        for key, months in EXAM_PEAK_MONTHS.items():
            if key in level_lower:
                peak_months = months
                break

        if peak_months:
            peak_names = " et ".join(MONTHS_FR[m] for m in peak_months[:2])
            if current_month in peak_months:
                timing_msg = (
                    f"Excellente période pour publier  les contenus {body.level or 'BAC'} "
                    f"sont très recherchés en {MONTHS_FR[current_month]}."
                )
            else:
                best = peak_months[0]
                pre_month = MONTHS_FR[max(1, best - 1)]
                timing_msg = (
                    f"Les contenus {body.level or 'BAC'} se téléchargent 3× plus en {peak_names}. "
                    f"Publiez en {pre_month} pour maximiser la visibilité."
                )
        else:
            timing_msg = "Ce type de contenu est consulté toute l'année  publiez dès que possible."

        # Price recommendation
        price_rec = avg_price
        if body.type in ("pack", "livre"):
            price_rec = int(avg_price * 1.4)
        elif body.type == "quiz":
            price_rec = max(500, int(avg_price * 0.6))

        # Download estimate
        base = avg_dl
        if body.type == "epreuve":
            base = int(base * 1.3)
        elif body.type == "pack":
            base = int(base * 0.8)
        if peak_months and current_month in peak_months:
            base = int(base * 1.6)

        return {
            "predicted_downloads_30d": max(5, base),
            "price_recommendation": price_rec,
            "similar_top_sellers": top_sellers[:3],
            "timing_advice": timing_msg,
        }

    except Exception as e:
        logger.error(f"predict_popularity error: {e}")
        return {
            "predicted_downloads_30d": 20,
            "price_recommendation": 2500,
            "similar_top_sellers": [],
            "timing_advice": "Publiez maintenant pour commencer à construire votre audience.",
        }
    finally:
        session.close()


# ─────────────────────────────────────────────────────────────────────────────
# Feature 6  POST /teacher/analyze-submission
# ─────────────────────────────────────────────────────────────────────────────

class AnalyzeSubmissionRequest(BaseModel):
    submission_text: str
    expected_answer: Optional[str] = None
    subject: Optional[str] = None
    level: Optional[str] = None
    #  mcq | short | essay (US-COR-06) : conditionne la forme de l'analyse.
    # Pour "mcq", is_correct doit être fourni par l'appelant (la justesse
    # d'un QCM se vérifie mécaniquement, pas par un LLM) ; l'IA n'est même
    # pas appelée dans ce cas.
    question_type: str = "essay"
    is_correct: Optional[bool] = None
    max_score: int = 20

class AnalyzeSubmissionResponse(BaseModel):
    error_type: str
    error_details: str
    suggested_comment: str
    score_suggestion: int
    #  Formulations alternatives du même commentaire (chips cliquables,
    # US-COR-02), générées dans le même appel plutôt que par un endpoint
    # séparé  pas de round-trip supplémentaire, toujours cohérentes avec
    # l'analyse ci-dessus.
    alternative_comments: List[str] = []
    #  Intervalle de confiance pour une réponse courte (US-COR-06), ex "13-15".
    score_range: Optional[str] = None
    #  Éléments présents/absents par rapport au barème, pour un développement long.
    highlights_present: List[str] = []
    highlights_absent: List[str] = []

@teacher_ai_router.post("/teacher/analyze-submission", response_model=AnalyzeSubmissionResponse)
async def analyze_submission(
    body: AnalyzeSubmissionRequest,
    current_user: UserTokenData = Depends(require_role("teacher", "admin")),
):
    # QCM : correction mécanique, pas d'appel IA (US-COR-06 "notation
    # automatique directe"). is_correct vient de la comparaison faite par
    # l'appelant entre la réponse choisie et la bonne réponse du quiz.
    if body.question_type == "mcq":
        correct = bool(body.is_correct)
        score = body.max_score if correct else 0
        return {
            "error_type": "none" if correct else "conceptual",
            "error_details": "Réponse correcte." if correct else "Réponse incorrecte pour ce QCM.",
            "suggested_comment": "Bonne réponse !" if correct else "Revois cette notion avant le prochain contrôle.",
            "score_suggestion": score,
            "alternative_comments": [],
            "score_range": None,
            "highlights_present": [],
            "highlights_absent": [],
        }

    expected_block = (
        f"RÉPONSE ATTENDUE :\n{body.expected_answer[:1000]}\n\n"
        if body.expected_answer else ""
    )
    is_essay = body.question_type == "essay"
    type_instructions = (
        (
            '5. "highlights_present" : tableau des éléments/notions du barème correctement traités par l\'élève\n'
            '6. "highlights_absent" : tableau des éléments/notions du barème manquants ou mal traités\n'
        ) if is_essay else (
            '5. "score_range" : intervalle de confiance de la note sous forme "min-max" (ex: "13-15"), max_score inclus\n'
        )
    )
    prompt = (
        f"Matière : {body.subject or 'non précisée'}  Niveau : {body.level or 'non précisé'}  "
        f"Type de question : {'développement long' if is_essay else 'réponse courte'}  Barème sur {body.max_score}.\n\n"
        f"TRAVAIL DE L'ÉLÈVE :\n{body.submission_text[:2000]}\n\n"
        + expected_block
        + "Analyse ce travail et génère :\n"
        '1. "error_type" : "methodological" (erreur de méthode) | "calculation" (erreur de calcul) | "conceptual" (incompréhension du concept) | "none" (correct)\n'
        '2. "error_details" : description précise de l\'erreur, 1-2 phrases\n'
        '3. "suggested_comment" : commentaire pédagogique bienveillant pour l\'élève, 3-4 phrases\n'
        f'4. "score_suggestion" : note suggérée sur {body.max_score} (entier)\n'
        + type_instructions +
        '7. "alternative_comments" : tableau de 2-3 reformulations courtes et différentes du commentaire, même sens\n'
        'Réponds en JSON strict avec exactement ces champs.'
    )
    system = (
        "Tu es WinAI, assistant de correction pédagogique bienveillant. "
        "Analyse les erreurs avec précision et propose des commentaires constructifs. "
        "Réponds uniquement avec du JSON valide."
    )

    raw = _deepseek_json(prompt, system, max_tokens=600)
    if raw and isinstance(raw, dict):
        error_type = raw.get("error_type", "methodological")
        if error_type not in ("methodological", "calculation", "conceptual", "none"):
            error_type = "methodological"
        alt = raw.get("alternative_comments", [])
        alt = [str(a) for a in alt][:3] if isinstance(alt, list) else []
        return {
            "error_type": error_type,
            "error_details": str(raw.get("error_details", "Vérifiez la démarche utilisée.")),
            "suggested_comment": str(raw.get("suggested_comment", "Bon travail  quelques points à consolider.")),
            "score_suggestion": max(0, min(body.max_score, int(raw.get("score_suggestion", body.max_score // 2)))),
            "alternative_comments": alt,
            "score_range": str(raw.get("score_range")) if not is_essay and raw.get("score_range") else None,
            "highlights_present": [str(h) for h in raw.get("highlights_present", [])] if is_essay else [],
            "highlights_absent": [str(h) for h in raw.get("highlights_absent", [])] if is_essay else [],
        }

    return {
        "error_type": "methodological",
        "error_details": "WinAI a analysé le travail  vérifiez manuellement la démarche appliquée.",
        "suggested_comment": "Vous montrez une bonne compréhension générale. Revoyez la démarche étape par étape pour consolider vos acquis.",
        "score_suggestion": body.max_score // 2,
        "alternative_comments": [],
        "score_range": None,
        "highlights_present": [],
        "highlights_absent": [],
    }


# ─────────────────────────────────────────────────────────────────────────────
# Feature 7  POST /teacher/suggest-tutor-rate (Module 1, US-PRO-05/US-PRO-07)
# Suggère un tarif horaire pour le Mode Répétiteur, basé en priorité sur les
# tarifs réels déjà pratiqués sur WinPlus pour la même matière/le même niveau
# (Median réel), et seulement à défaut de données suffisantes sur une
# estimation raisonnée par DeepSeek  pour ne jamais présenter un chiffre
# inventé comme une donnée de marché.
# ─────────────────────────────────────────────────────────────────────────────

class SuggestTutorRateRequest(BaseModel):
    subject: str
    level: Optional[str] = None
    city: Optional[str] = None


class SuggestTutorRateResponse(BaseModel):
    suggested_rate_xaf: int
    range_low_xaf: int
    range_high_xaf: int
    based_on: str  # "market_data" | "estimation"
    sample_size: int
    explanation: str


MIN_SAMPLE_FOR_MARKET_DATA = 3


@teacher_ai_router.post("/teacher/suggest-tutor-rate", response_model=SuggestTutorRateResponse)
async def suggest_tutor_rate(
    body: SuggestTutorRateRequest,
    current_user: UserTokenData = Depends(require_role("teacher", "admin")),
):
    db = Database()
    session = db.SessionLocal()
    try:
        query = (
            session.query(TutorProfile.HourlyRateXaf)
            .join(TutorSubject, TutorSubject.TutorProfileId == TutorProfile.Id)
            .filter(
                TutorProfile.IsActive == True,  # noqa: E712
                TutorProfile.HourlyRateXaf.isnot(None),
                func.lower(TutorSubject.Subject) == body.subject.strip().lower(),
            )
        )
        if body.level:
            query = query.join(TutorLevel, TutorLevel.TutorProfileId == TutorProfile.Id).filter(
                func.lower(TutorLevel.Level) == body.level.strip().lower()
            )
        rates = sorted(r[0] for r in query.all() if r[0] is not None)
    finally:
        session.close()

    if len(rates) >= MIN_SAMPLE_FOR_MARKET_DATA:
        mid = len(rates) // 2
        median = rates[mid] if len(rates) % 2 else (rates[mid - 1] + rates[mid]) / 2
        low, high = rates[0], rates[-1]
        return SuggestTutorRateResponse(
            suggested_rate_xaf=int(median),
            range_low_xaf=int(low),
            range_high_xaf=int(high),
            based_on="market_data",
            sample_size=len(rates),
            explanation=(
                f"Basé sur {len(rates)} répétiteurs déjà actifs sur WinPlus en {body.subject}"
                f"{f' niveau {body.level}' if body.level else ''} : tarif médian observé."
            ),
        )

    # Pas assez de données réelles : estimation raisonnée, explicitement
    # signalée comme telle (based_on="estimation") plutôt que présentée comme
    # un fait de marché.
    prompt = (
        f"Estime un tarif horaire de cours particulier au Cameroun (Douala/Yaoundé), "
        f"en Francs CFA (XAF), pour un répétiteur en {body.subject}"
        f"{f', niveau {body.level}' if body.level else ''}"
        f"{f', ville {body.city}' if body.city else ''}.\n"
        "Réponds en JSON strict : "
        '{"suggested_rate_xaf": 3000, "range_low_xaf": 2000, "range_high_xaf": 4500, '
        '"explanation": "1-2 phrases justifiant l\'estimation"}'
    )
    system = (
        "Tu es WinAI, expert du marché du soutien scolaire privé au Cameroun. "
        "Donne des estimations réalistes en Francs CFA, jamais en euros ou dollars."
    )
    raw = _deepseek_json(prompt, system, max_tokens=250)
    if raw and isinstance(raw, dict) and raw.get("suggested_rate_xaf"):
        return SuggestTutorRateResponse(
            suggested_rate_xaf=int(raw["suggested_rate_xaf"]),
            range_low_xaf=int(raw.get("range_low_xaf", raw["suggested_rate_xaf"] * 0.7)),
            range_high_xaf=int(raw.get("range_high_xaf", raw["suggested_rate_xaf"] * 1.5)),
            based_on="estimation",
            sample_size=len(rates),
            explanation=str(raw.get("explanation", "Estimation WinAI faute de données suffisantes sur la plateforme.")),
        )

    return SuggestTutorRateResponse(
        suggested_rate_xaf=3000,
        range_low_xaf=2000,
        range_high_xaf=5000,
        based_on="estimation",
        sample_size=len(rates),
        explanation="Estimation par défaut  pas encore assez de répétiteurs actifs sur WinPlus dans cette matière pour une donnée de marché fiable.",
    )


# ─────────────────────────────────────────────────────────────────────────────
# Feature 8  POST /teacher/analyze-tutor-profile (Module 1, US-PRO-04)
# Analyse le profil répétiteur et suggère des améliorations concrètes.
# Le score de complétude "structurel" (champs remplis) est déjà calculé côté
# .NET (TutorProfileService.ComputeCompletion) ; WinAI ajoute ici un avis
# qualitatif que seul un LLM peut donner (qualité de la bio, différenciation).
# ─────────────────────────────────────────────────────────────────────────────

class AnalyzeTutorProfileRequest(BaseModel):
    title: Optional[str] = None
    tutor_bio: Optional[str] = None
    subjects: List[str] = []
    levels: List[str] = []
    hourly_rate_xaf: Optional[int] = None
    has_video: bool = False
    missing_fields: List[str] = []


class TutorProfileSuggestion(BaseModel):
    field: str
    suggestion: str


class AnalyzeTutorProfileResponse(BaseModel):
    suggestions: List[TutorProfileSuggestion]


@teacher_ai_router.post("/teacher/analyze-tutor-profile", response_model=AnalyzeTutorProfileResponse)
async def analyze_tutor_profile(
    body: AnalyzeTutorProfileRequest,
    current_user: UserTokenData = Depends(require_role("teacher", "admin")),
):
    missing_line = f"Champs structurels manquants : {', '.join(body.missing_fields)}.\n" if body.missing_fields else "Tous les champs structurels sont remplis.\n"
    prompt = (
        "Voici le profil répétiteur d'un professeur sur WinPlus :\n"
        f"Titre : {body.title or '(vide)'}\n"
        f"Bio : {body.tutor_bio or '(vide)'}\n"
        f"Matières : {', '.join(body.subjects) or '(aucune)'}\n"
        f"Niveaux : {', '.join(body.levels) or '(aucun)'}\n"
        f"Tarif horaire : {body.hourly_rate_xaf or '(non défini)'} XAF\n"
        f"Vidéo de présentation : {'oui' if body.has_video else 'non'}\n"
        f"{missing_line}\n"
        "Donne 3 à 5 suggestions concrètes et actionnables pour améliorer l'attractivité de ce profil "
        "auprès d'élèves camerounais (pas de généralités  chaque suggestion doit se rattacher à un "
        "champ précis du profil). Format JSON strict : "
        '[{"field":"bio","suggestion":"..."}, ...]  field parmi : title, bio, subjects, levels, hourlyRate, video, general.'
    )
    system = (
        "Tu es WinAI, coach en optimisation de profil pour répétiteurs. "
        "Réponds uniquement avec un tableau JSON valide, suggestions courtes et concrètes."
    )
    raw = _deepseek_json(prompt, system, max_tokens=500)

    suggestions: List[TutorProfileSuggestion] = []
    if isinstance(raw, list):
        for item in raw[:5]:
            field = str(item.get("field", "general"))
            suggestion = str(item.get("suggestion", "")).strip()
            if suggestion:
                suggestions.append(TutorProfileSuggestion(field=field, suggestion=suggestion))

    if not suggestions:
        suggestions.append(TutorProfileSuggestion(
            field="general",
            suggestion="Complète ta bio et ajoute une vidéo de présentation : les profils avec vidéo reçoivent nettement plus de réservations.",
        ))

    return AnalyzeTutorProfileResponse(suggestions=suggestions)


# ─────────────────────────────────────────────────────────────────────────────
# Feature 9  GET /teacher/recommended-purchases (Module 2, US-CAT-07)
# Recommandations d'achat pour le professeur-acheteur : contenus bien notés
# dans ses propres matières, qu'il ne possède pas encore (ni auteur, ni acheté).
# ─────────────────────────────────────────────────────────────────────────────

class RecommendedPurchase(BaseModel):
    subjectId: int
    title: str
    category: Optional[str]
    averageRating: float
    price: float
    justification: str


class RecommendedPurchasesResponse(BaseModel):
    items: List[RecommendedPurchase]


@teacher_ai_router.get("/teacher/recommended-purchases", response_model=RecommendedPurchasesResponse)
async def get_recommended_purchases(current_user: UserTokenData = Depends(require_role("teacher", "admin"))):
    db = Database()
    session = db.SessionLocal()
    try:
        teacher_id = current_user.user_id

        my_categories = [
            row[0] for row in session.query(Subject.Category)
            .join(CourseContent, CourseContent.SubjectId == Subject.Id)
            .filter(CourseContent.CreatedByUserId == teacher_id, Subject.Category.isnot(None))
            .distinct().all()
        ]

        owned_ids = {
            row[0] for row in session.query(OrderItem.SubjectId)
            .join(Order, Order.Id == OrderItem.OrderId)
            .filter(Order.UserId == teacher_id, Order.Status == 'completed')
            .all()
        }
        owned_ids |= {
            row[0] for row in session.query(Subject.Id).filter(Subject.AuthorUserId == teacher_id).all()
        }

        query = session.query(Subject).filter(
            Subject.IsPublished == True,  # noqa: E712
            Subject.IsDeleted == False,  # noqa: E712
            ~Subject.Id.in_(owned_ids) if owned_ids else True,
        )
        used_fallback = False
        if my_categories:
            query = query.filter(Subject.Category.in_(my_categories))
        else:
            used_fallback = True

        top = query.order_by(Subject.AverageRating.desc(), Subject.EnrollmentCount.desc()).limit(5).all()

        items = []
        for s in top:
            if used_fallback:
                justification = f"Contenu très bien noté ({float(s.AverageRating):.1f}/5) pour démarrer ta bibliothèque."
            else:
                justification = f"Bien noté ({float(s.AverageRating):.1f}/5) en {s.Category}  ta matière de prédilection."
            items.append(RecommendedPurchase(
                subjectId=s.Id, title=s.Title, category=s.Category,
                averageRating=float(s.AverageRating or 0), price=float(s.Price or 0),
                justification=justification,
            ))
        return RecommendedPurchasesResponse(items=items)
    finally:
        session.close()


# ─────────────────────────────────────────────────────────────────────────────
# Feature 10  GET /teacher/editorial-watch (Module 2, US-CAT-09 "veille éditoriale")
# Matières où la demande (téléchargements) dépasse largement l'offre
# (contenus publiés par des professeurs) : opportunité de publication.
# ─────────────────────────────────────────────────────────────────────────────

class EditorialWatchItem(BaseModel):
    category: str
    downloadsLast30Days: int
    publishedContentCount: int
    message: str


class EditorialWatchResponse(BaseModel):
    items: List[EditorialWatchItem]


@teacher_ai_router.get("/teacher/editorial-watch", response_model=EditorialWatchResponse)
async def get_editorial_watch(current_user: UserTokenData = Depends(require_role("teacher", "admin"))):
    db = Database()
    session = db.SessionLocal()
    try:
        cutoff = datetime.utcnow() - timedelta(days=30)

        demand_rows = (
            session.query(Subject.Category)
            .join(DownloadHistory, DownloadHistory.SubjectId == Subject.Id)
            .filter(DownloadHistory.CreatedAt >= cutoff, Subject.Category.isnot(None))
            .all()
        )
        # Comptage manuel (évite de dépendre d'un import func supplémentaire ici).
        demand: dict = {}
        for (category,) in demand_rows:
            demand[category] = demand.get(category, 0) + 1

        supply_rows = (
            session.query(Subject.Category)
            .join(CourseContent, CourseContent.SubjectId == Subject.Id)
            .filter(Subject.Category.isnot(None))
            .all()
        )
        supply: dict = {}
        for (category,) in supply_rows:
            supply[category] = supply.get(category, 0) + 1

        items = []
        for category, downloads in sorted(demand.items(), key=lambda kv: kv[1], reverse=True):
            published = supply.get(category, 0)
            if downloads < 10:
                continue  # signal trop faible pour être actionnable
            ratio = downloads / max(published, 1)
            if ratio < 3:
                continue  # offre déjà correcte face à la demande
            items.append(EditorialWatchItem(
                category=category,
                downloadsLast30Days=downloads,
                publishedContentCount=published,
                message=(
                    f"{downloads} téléchargements ce mois-ci en {category} pour seulement {published} "
                    f"contenu(s) publié(s) : publier maintenant te positionnerait tôt sur ce créneau."
                ),
            ))
        return EditorialWatchResponse(items=items[:5])
    finally:
        session.close()


# ─────────────────────────────────────────────────────────────────────────────
# Feature 11  POST /teacher/session-summary (Module 5, US-SES-04)
# Résume une transcription collée par le professeur  aucune capture
# audio/vidéo n'existe dans ce projet (le live se tient sur un lien externe,
# WinPlus n'enregistre rien), donc pas de "transcription automatique" au sens
# propre : WinAI structure un texte déjà obtenu par le professeur.
# ─────────────────────────────────────────────────────────────────────────────

class SessionSummaryRequest(BaseModel):
    transcript_text: str


class SessionSummaryResponse(BaseModel):
    points_covered: List[str]
    student_questions: List[str]
    decisions: List[str]
    homework: List[str]
    summary_text: str


@teacher_ai_router.post("/teacher/session-summary", response_model=SessionSummaryResponse)
async def generate_session_summary(
    body: SessionSummaryRequest,
    current_user: UserTokenData = Depends(require_role("teacher", "admin")),
):
    if len(body.transcript_text.strip()) < 30:
        raise HTTPException(status_code=422, detail="Transcription trop courte pour être résumée.")

    prompt = (
        "Voici la transcription (ou les notes) d'une session de cours en ligne :\n\n"
        f"{body.transcript_text[:6000]}\n\n"
        "Génère un compte-rendu structuré en JSON avec exactement ces champs :\n"
        '1. "points_covered" : tableau des points/notions abordés\n'
        '2. "student_questions" : tableau des questions posées par les élèves (vide si aucune identifiable)\n'
        '3. "decisions" : tableau des décisions prises (dates de rattrapage, changements de programme...)\n'
        '4. "homework" : tableau des devoirs annoncés (vide si aucun)\n'
        '5. "summary_text" : le compte-rendu complet en 4-6 phrases, prêt à être envoyé aux élèves tel quel\n'
        'Réponds en JSON strict, sans texte autour.'
    )
    system = (
        "Tu es WinAI, assistant de compte-rendu pédagogique. Sois fidèle au contenu fourni, "
        "n'invente rien qui ne soit pas dans la transcription. Réponds uniquement en JSON valide."
    )

    raw = _deepseek_json(prompt, system, max_tokens=800)
    if raw and isinstance(raw, dict):
        return SessionSummaryResponse(
            points_covered=[str(p) for p in raw.get("points_covered", [])],
            student_questions=[str(q) for q in raw.get("student_questions", [])],
            decisions=[str(d) for d in raw.get("decisions", [])],
            homework=[str(h) for h in raw.get("homework", [])],
            summary_text=str(raw.get("summary_text", "")).strip(),
        )

    raise HTTPException(status_code=500, detail="Génération du compte-rendu impossible pour le moment.")


# ─────────────────────────────────────────────────────────────────────────────
# Feature 12  POST /teacher/student-revision-sheet (Module 6, US-REP-11)
# Fiche de révision personnalisée générée à partir des comptes-rendus de
# séances précédentes du couple répétiteur/élève (aucune source de "scores
# aux quiz" n'est fournie ici  la fiche s'appuie sur les comptes-rendus
# textuels réellement disponibles, voir TutorBookingController).
# ─────────────────────────────────────────────────────────────────────────────

class RevisionSheetRequest(BaseModel):
    student_name: str
    subject: Optional[str] = None
    session_summaries: List[str]


class RevisionSheetResponse(BaseModel):
    priority_topics: List[str]
    recommended_exercises: List[str]
    key_methods: List[str]
    sheet_text: str


@teacher_ai_router.post("/teacher/student-revision-sheet", response_model=RevisionSheetResponse)
async def generate_revision_sheet(
    body: RevisionSheetRequest,
    current_user: UserTokenData = Depends(require_role("teacher", "admin")),
):
    if not body.session_summaries:
        raise HTTPException(status_code=422, detail="Aucun compte-rendu de séance disponible pour cet élève.")

    summaries_text = "\n---\n".join(s[:800] for s in body.session_summaries[:20])
    prompt = (
        f"Élève : {body.student_name}" + (f"  Matière : {body.subject}" if body.subject else "") + "\n\n"
        f"Comptes-rendus des séances précédentes de cours particulier :\n{summaries_text}\n\n"
        "Génère une fiche de révision personnalisée en JSON avec exactement ces champs :\n"
        '1. "priority_topics" : notions à revoir en priorité, identifiées depuis les comptes-rendus\n'
        '2. "recommended_exercises" : types d\'exercices recommandés pour ces notions\n'
        '3. "key_methods" : méthodes et formules clés à rappeler\n'
        '4. "sheet_text" : la fiche complète rédigée, prête à être partagée avec l\'élève\n'
        'Réponds en JSON strict, sans texte autour.'
    )
    system = (
        "Tu es WinAI, assistant pédagogique pour répétiteurs. Base-toi uniquement sur les comptes-rendus "
        "fournis, n'invente aucune notion qui n'y apparaît pas. Réponds uniquement en JSON valide."
    )

    raw = _deepseek_json(prompt, system, max_tokens=900)
    if raw and isinstance(raw, dict):
        return RevisionSheetResponse(
            priority_topics=[str(t) for t in raw.get("priority_topics", [])],
            recommended_exercises=[str(e) for e in raw.get("recommended_exercises", [])],
            key_methods=[str(m) for m in raw.get("key_methods", [])],
            sheet_text=str(raw.get("sheet_text", "")).strip(),
        )

    raise HTTPException(status_code=500, detail="Génération de la fiche de révision impossible pour le moment.")


# ─────────────────────────────────────────────────────────────────────────────
# Feature 13  POST /teacher/coaching-report (Module 6, US-REP-12)
# Rapport mensuel de coaching pédagogique, généré depuis les avis élèves
# reçus par le répétiteur sur la période.
# ─────────────────────────────────────────────────────────────────────────────

class CoachingReportRequest(BaseModel):
    month_label: str
    reviews: List[str]
    subjects_taught: List[str] = []
    sessions_count: int = 0
    average_rating: Optional[float] = None


class CoachingReportResponse(BaseModel):
    strengths: List[str]
    subjects_improving: List[str]
    stagnation_patterns: List[str]
    recommendations: List[str]
    report_text: str


@teacher_ai_router.post("/teacher/coaching-report", response_model=CoachingReportResponse)
async def generate_coaching_report(
    body: CoachingReportRequest,
    # Module 23 : TutorCoachingReportService (.NET, tâche mensuelle) appelle
    # cette route sans utilisateur connecté : jeton technique « ai.coaching-report ».
    current_user: UserTokenData = Depends(
        require_user_or_service("ai.coaching-report", roles=("teacher", "admin"))),
):
    if not body.reviews and body.sessions_count == 0:
        raise HTTPException(status_code=422, detail="Pas assez de données pour générer un rapport ce mois-ci.")

    reviews_text = "\n---\n".join(r[:400] for r in body.reviews[:30]) or "(aucun avis reçu ce mois-ci)"
    prompt = (
        f"Période : {body.month_label}\n"
        f"Séances effectuées : {body.sessions_count}\n"
        f"Note moyenne : {body.average_rating if body.average_rating is not None else 'N/A'}\n"
        f"Matières enseignées : {', '.join(body.subjects_taught) or 'N/A'}\n\n"
        f"Avis élèves reçus ce mois :\n{reviews_text}\n\n"
        "Génère un rapport de coaching pédagogique en JSON avec exactement ces champs :\n"
        '1. "strengths" : points forts relevés dans les avis élèves\n'
        '2. "subjects_improving" : matières où les élèves progressent le plus (déduit du contexte)\n'
        '3. "stagnation_patterns" : patterns de stagnation ou signaux d\'alerte identifiés\n'
        '4. "recommendations" : recommandations concrètes et actionnables sur la pratique pédagogique\n'
        '5. "report_text" : le rapport complet rédigé en 5-8 phrases\n'
        'Réponds en JSON strict, sans texte autour. Si les données sont limitées, reste factuel et concis '
        'plutôt que d\'inventer des détails.'
    )
    system = (
        "Tu es WinAI, coach pédagogique pour répétiteurs sur la plateforme WinPlus. Base-toi uniquement "
        "sur les données fournies. Réponds uniquement en JSON valide."
    )

    raw = _deepseek_json(prompt, system, max_tokens=900)
    if raw and isinstance(raw, dict):
        return CoachingReportResponse(
            strengths=[str(s) for s in raw.get("strengths", [])],
            subjects_improving=[str(s) for s in raw.get("subjects_improving", [])],
            stagnation_patterns=[str(s) for s in raw.get("stagnation_patterns", [])],
            recommendations=[str(r) for r in raw.get("recommendations", [])],
            report_text=str(raw.get("report_text", "")).strip(),
        )

    raise HTTPException(status_code=500, detail="Génération du rapport de coaching impossible pour le moment.")


# ─────────────────────────────────────────────────────────────────────────────
# Module 7  Messagerie : suggestions WinAI (US-MSG-07) et rapport parent (US-MSG-10)
# ─────────────────────────────────────────────────────────────────────────────

class QuickRepliesRequest(BaseModel):
    message_text: str


class QuickRepliesResponse(BaseModel):
    suggestions: List[str]


@teacher_ai_router.post("/messaging/quick-replies", response_model=QuickRepliesResponse)
async def generate_quick_replies(
    body: QuickRepliesRequest,
    current_user: UserTokenData = Depends(verify_token),
):
    if len(body.message_text.strip()) < 2:
        raise HTTPException(status_code=422, detail="Message trop court.")

    prompt = (
        f"Message reçu d'un élève ou parent sur WinPlus :\n\"{body.message_text[:1000]}\"\n\n"
        "Propose exactement 3 réponses courtes et naturelles (une phrase chacune) qu'un professeur "
        "pourrait envoyer directement. Réponds en JSON strict : "
        '{"suggestions": ["réponse 1", "réponse 2", "réponse 3"]}'
    )
    system = (
        "Tu es WinAI, assistant de messagerie pour professeurs sur WinPlus. Reste bref, naturel, "
        "et pertinent au message reçu. Réponds uniquement en JSON valide."
    )
    raw = _deepseek_json(prompt, system, max_tokens=300)
    if raw and isinstance(raw, dict) and raw.get("suggestions"):
        return QuickRepliesResponse(suggestions=[str(s) for s in raw["suggestions"]][:3])
    raise HTTPException(status_code=500, detail="Génération des suggestions impossible pour le moment.")


class GenerateReplyRequest(BaseModel):
    message_text: str
    context: Optional[str] = None


class GenerateReplyResponse(BaseModel):
    reply_text: str


@teacher_ai_router.post("/messaging/generate-reply", response_model=GenerateReplyResponse)
async def generate_long_reply(
    body: GenerateReplyRequest,
    current_user: UserTokenData = Depends(verify_token),
):
    prompt = (
        f"Message reçu :\n\"{body.message_text[:1500]}\"\n\n"
        + (f"Contexte additionnel : {body.context[:500]}\n\n" if body.context else "")
        + "Rédige une réponse complète, professionnelle et bienveillante à ce message, prête à être "
        "envoyée telle quelle (2-5 phrases). Réponds uniquement avec le texte de la réponse, sans JSON ni guillemets."
    )
    system = "Tu es WinAI, assistant de messagerie pour professeurs sur WinPlus. Sois clair, chaleureux et concret."
    reply = _deepseek_text(prompt, system, max_tokens=350)
    if reply:
        return GenerateReplyResponse(reply_text=reply.strip())
    raise HTTPException(status_code=500, detail="Génération de la réponse impossible pour le moment.")


class ParentReportRequest(BaseModel):
    student_name: str
    summary_text: str


class ParentReportResponse(BaseModel):
    report_text: str


@teacher_ai_router.post("/messaging/parent-report", response_model=ParentReportResponse)
async def generate_parent_report(
    body: ParentReportRequest,
    current_user: UserTokenData = Depends(require_role("teacher", "admin")),
):
    if len(body.summary_text.strip()) < 10:
        raise HTTPException(status_code=422, detail="Résumé trop court pour générer un rapport.")

    prompt = (
        f"Élève : {body.student_name}\n"
        f"Résumé donné par le professeur : {body.summary_text[:800]}\n\n"
        "Rédige un message formel et bienveillant destiné aux parents de cet élève, incluant si "
        "pertinent : la progression, des points positifs soulignés, des axes d'amélioration, et les "
        "devoirs à faire. Reste fidèle au résumé fourni, n'invente aucun détail chiffré qui n'y figure "
        "pas. Réponds uniquement avec le texte du message, sans JSON ni guillemets."
    )
    system = "Tu es WinAI, assistant de communication pédagogique pour professeurs sur WinPlus."
    report = _deepseek_text(prompt, system, max_tokens=400)
    if report:
        return ParentReportResponse(report_text=report.strip())
    raise HTTPException(status_code=500, detail="Génération du rapport impossible pour le moment.")


# ─────────────────────────────────────────────────────────────────────────────
# Module 9  Formations structurées : relance élève inactif (US-FOR-07)
# ─────────────────────────────────────────────────────────────────────────────

class InactivityRelaunchRequest(BaseModel):
    course_title: str


class InactivityRelaunchResponse(BaseModel):
    message_text: str


@teacher_ai_router.post("/teacher/inactivity-relaunch-message", response_model=InactivityRelaunchResponse)
async def generate_inactivity_relaunch_message(
    body: InactivityRelaunchRequest,
    current_user: UserTokenData = Depends(require_role("teacher", "admin")),
):
    prompt = (
        f"Un élève n'a pas repris sa formation « {body.course_title} » depuis plusieurs jours. "
        "Rédige un message court, bienveillant et motivant (2-3 phrases) du professeur pour "
        "l'encourager à reprendre, sans culpabiliser. Réponds uniquement avec le texte du message, "
        "sans JSON ni guillemets."
    )
    system = "Tu es WinAI, assistant pédagogique pour professeurs sur WinPlus. Reste chaleureux et concis."
    message = _deepseek_text(prompt, system, max_tokens=200)
    if message:
        return InactivityRelaunchResponse(message_text=message.strip())
    raise HTTPException(status_code=500, detail="Génération du message de relance impossible pour le moment.")


# ─────────────────────────────────────────────────────────────────────────────
# Module 8  Détection de décrochage (scoring). Appelé quotidiennement par
# CourseInactivityAlertService (C#, appel interne service-à-service  pas de
# jeton utilisateur disponible en tâche de fond, comme WeeklyParentReportService
# pour /api/chatbot/chat) avec les métriques déjà calculées côté C# (inactivité,
# scores de quiz récents). Endpoint purement calculatoire, aucun accès DB.
# ─────────────────────────────────────────────────────────────────────────────

class DecrochageEleveInput(BaseModel):
    user_id: int
    name: str
    days_inactive: int
    recent_quiz_scores: List[float] = []  # ordonnés du plus ancien au plus récent, 0-100


class DetectionDecrochageRequest(BaseModel):
    course_title: str
    students: List[DecrochageEleveInput]


class DecrochageAlert(BaseModel):
    user_id: int
    name: str
    niveau: str  # "faible" | "eleve"
    signaux: List[str]


class DetectionDecrochageResponse(BaseModel):
    alerts: List[DecrochageAlert]


def _score_en_baisse(scores: List[float]) -> bool:
    """Compare la moyenne de la 1re moitié des tentatives à la 2e moitié. Baisse significative = -10 points."""
    if len(scores) < 2:
        return False
    mid = len(scores) // 2
    prev_avg = sum(scores[:mid]) / mid
    recent_avg = sum(scores[mid:]) / (len(scores) - mid)
    return recent_avg <= prev_avg - 10


@teacher_ai_router.post("/winai/detection-decrochage", response_model=DetectionDecrochageResponse)
async def detection_decrochage(
    body: DetectionDecrochageRequest,
    # Module 36 : la route était publique (aucune dépendance d'authentification),
    # faute de jeton disponible en tâche de fond. Elle n'accepte plus que le
    # jeton technique « ai.decrochage » de CourseInactivityAlertService : seul
    # appelant connu, aucun écran ne l'appelle directement (roles=()).
    current_user: UserTokenData = Depends(require_user_or_service("ai.decrochage", roles=())),
):
    alerts: List[DecrochageAlert] = []
    for eleve in body.students:
        declining = _score_en_baisse(eleve.recent_quiz_scores)
        signaux: List[str] = []
        if eleve.days_inactive >= 7:
            signaux.append(f"inactif depuis {eleve.days_inactive} jours")
        if declining:
            signaux.append("scores de quiz en baisse")

        if eleve.days_inactive >= 14 or (eleve.days_inactive >= 7 and declining):
            niveau = "eleve"
        elif eleve.days_inactive >= 7:
            niveau = "faible"
        else:
            continue  # pas de signal suffisant, élève non à risque

        alerts.append(DecrochageAlert(user_id=eleve.user_id, name=eleve.name, niveau=niveau, signaux=signaux))
    return DetectionDecrochageResponse(alerts=alerts)


# ─────────────────────────────────────────────────────────────────────────────
# Module 9  Formations structurées : génération de syllabus WinAI (US-FOR-09)
# ─────────────────────────────────────────────────────────────────────────────

class GenerateSyllabusRequest(BaseModel):
    subject: str
    level: str
    duration_weeks: int
    objectives: List[str] = []
    target_exams: List[str] = []


class SyllabusWeek(BaseModel):
    week: int
    title: str
    concepts: List[str]
    activities: List[str]
    duration_minutes: int
    suggested_resources: List[str]


class GenerateSyllabusResponse(BaseModel):
    weeks: List[SyllabusWeek]


@teacher_ai_router.post("/teacher/generate-syllabus", response_model=GenerateSyllabusResponse)
async def generate_syllabus(
    body: GenerateSyllabusRequest,
    current_user: UserTokenData = Depends(require_role("teacher", "admin")),
):
    weeks = max(1, min(body.duration_weeks, 24))
    prompt = (
        f"Matière : {body.subject}\nNiveau : {body.level}\nDurée : {weeks} semaines\n"
        f"Objectifs généraux : {', '.join(body.objectives) or 'non précisés'}\n"
        f"Examens visés : {', '.join(body.target_exams) or 'non précisés'}\n\n"
        f"Génère un plan de cours structuré sur {weeks} semaines. Réponds en JSON strict : "
        '{"weeks": [{"week": 1, "title": "...", "concepts": ["..."], "activities": ["..."], '
        '"duration_minutes": 60, "suggested_resources": ["..."]}]}. '
        f"Le tableau \"weeks\" doit contenir exactement {weeks} éléments, un par semaine, "
        "progressifs et cohérents avec le niveau et les objectifs indiqués."
    )
    system = (
        "Tu es WinAI, assistant pédagogique pour professeurs sur WinPlus. Construis un plan de "
        "cours réaliste et actionnable. Réponds uniquement en JSON valide."
    )
    raw = _deepseek_json(prompt, system, max_tokens=2200)
    if raw and isinstance(raw, dict) and raw.get("weeks"):
        parsed_weeks = []
        for w in raw["weeks"][:weeks]:
            if not isinstance(w, dict):
                continue
            parsed_weeks.append(SyllabusWeek(
                week=int(w.get("week", len(parsed_weeks) + 1)),
                title=str(w.get("title", "")).strip(),
                concepts=[str(c) for c in w.get("concepts", [])],
                activities=[str(a) for a in w.get("activities", [])],
                duration_minutes=int(w.get("duration_minutes", 60) or 60),
                suggested_resources=[str(r) for r in w.get("suggested_resources", [])],
            ))
        if parsed_weeks:
            return GenerateSyllabusResponse(weeks=parsed_weeks)
    raise HTTPException(status_code=500, detail="Génération du syllabus impossible pour le moment.")


# ─────────────────────────────────────────────────────────────────────────────
# Module 8  WinAI : suggestion de champs à l'upload (lecture réelle du fichier)
# Module 7  même lecture réutilisée pour le score de commission catalogue
#
# Point d'entrée SYNCHRONE unique : lit le fichier réellement déposé (texte
# natif, OCR de secours, description d'image, transcription vidéo  mêmes
# briques que RAG/api/ingestion, jamais recréées ici) UNE SEULE FOIS, puis en
# tire à la fois un préremplissage de formulaire (Module 8) et un score de
# pertinence/valeur pédagogique avec sa justification (Module 7), pour ne pas
# lire le fichier deux fois. Le fichier n'est jamais indexé dans le pipeline
# RAG du chatbot ici : ce dernier reste la responsabilité exclusive de
# RAG/router.py (voir attachment_processor.py).
# ─────────────────────────────────────────────────────────────────────────────

import requests as _requests
import tempfile as _tempfile

_ANALYSIS_MAX_BYTES = 30 * 1024 * 1024  # 30 Mo : au-delà, hors budget d'un appel synchrone
_ANALYSIS_DOWNLOAD_TIMEOUT_S = 20
_ANALYSIS_VIDEO_EXTENSIONS = {".mp4", ".mov", ".mkv", ".webm", ".avi", ".m4v"}
_ANALYSIS_AUDIO_EXTENSIONS = {".mp3", ".m4a", ".wav"}
_ANALYSIS_IMAGE_EXTENSIONS = {".jpg", ".jpeg", ".png", ".webp"}
_ANALYSIS_MAX_CHARS = 12000
# PDF au-delà de ce nombre de pages : seules la 1re et la dernière page servent
# aux métadonnées (titre, matière, niveau, année sont en général sur ces pages).
# 30 pages : la lecture complète dépasserait l'enveloppe de l'appel synchrone
# (OCR de tout le document) alors que le prompt est plafonné à 12 000 caractères.
_ANALYSIS_LARGE_PDF_PAGES = 30
# En-deçà de ce nombre de caractères, la couche texte native est jugée insuffisante -> OCR.
_ANALYSIS_MIN_NATIVE_CHARS = 200


class AnalyzeContentUploadRequest(BaseModel):
    file_url: str
    filename: Optional[str] = None
    # epreuve | correction | livre | quiz | pack | formation | video
    content_kind: str = "epreuve"
    # Si le client a des listes fermées (ex. <select> matière/niveau cote
    # professeur), les lui transmettre ici fait choisir WinAI dans CETTE
    # liste exacte plutot que de deviner un rapprochement apres coup cote
    # client (matiere/niveau libres cote admin : laisser vide).
    allowed_categories: Optional[List[str]] = None
    allowed_levels: Optional[List[str]] = None


class SuggestedContentFields(BaseModel):
    title: Optional[str] = None
    description: Optional[str] = None
    description_courte: Optional[str] = None
    category: Optional[str] = None
    level: Optional[str] = None
    difficulty: Optional[str] = None
    year: Optional[str] = None
    exam_type: Optional[str] = None
    tags: List[str] = []
    objectives: List[str] = []
    prerequisites: List[str] = []
    duration_seconds: Optional[int] = None
    price_suggestion: Optional[float] = None
    author: Optional[str] = None
    publisher: Optional[str] = None
    page_count: Optional[int] = None


class AnalyzeContentUploadResponse(BaseModel):
    suggested: SuggestedContentFields
    winai_score: Optional[float] = None
    winai_justification: Optional[str] = None
    extraction_warning: Optional[str] = None


def _download_for_analysis(file_url: str) -> tuple[bytes, Optional[str]]:
    """Télécharge le fichier déposé pour analyse. Lève HTTPException si trop
    volumineux ou injoignable  jamais d'attente indéfinie (cas limite Module 8)."""
    try:
        resp = _requests.get(file_url, timeout=_ANALYSIS_DOWNLOAD_TIMEOUT_S, stream=True)
        resp.raise_for_status()
    except Exception as e:
        logger.warning(f"[Module8] Téléchargement du fichier à analyser échoué : {e}")
        raise HTTPException(status_code=502, detail="Fichier déposé injoignable pour l'analyse automatique.")

    chunks = []
    total = 0
    for chunk in resp.iter_content(chunk_size=1024 * 256):
        total += len(chunk)
        if total > _ANALYSIS_MAX_BYTES:
            raise HTTPException(
                status_code=413,
                detail="Fichier trop volumineux pour une analyse immédiate (30 Mo max)  remplis le formulaire manuellement.",
            )
        chunks.append(chunk)
    return b"".join(chunks), resp.headers.get("content-type")


def _ocr_pdf_chain(pdf_path: str) -> tuple[str, str]:
    """OCR d'un PDF : Mistral OCR (API) d'abord, puis OCR local self-hosted
    (RAG/self_hosted, GLM-OCR) page par page en secours si Mistral échoue.
    Renvoie (texte, moteur_utilisé). Lève si aucun moteur ne répond."""
    from RAG.api.ingestion.ocr_client import ocr_pdf

    try:
        return ocr_pdf(pdf_path), "mistral"
    except Exception as primary_err:
        logger.error(f"[Module8] OCR Mistral indisponible, secours OCR local : {primary_err}", exc_info=True)

    from RAG.self_hosted.ingestion.ocr_engine import ocr_transcribe
    from RAG.shared.pdf_utils import page_count, render_page_image

    parts = []
    for page_no in range(1, page_count(pdf_path) + 1):
        page_text = (ocr_transcribe(render_page_image(pdf_path, page_no, dpi=150)) or "").strip()
        if page_text:
            parts.append(f"--- page {page_no} ---\n{page_text}")
    return "\n\n".join(parts), "local"


def _write_first_last_pdf(pdf_path: str, total_pages: int) -> str:
    """Écrit dans un PDF temporaire la 1re et la dernière page seulement.
    Le chemin renvoyé est ensuite passé tel quel au natif et à l'OCR."""
    import fitz

    src = fitz.open(pdf_path)
    out = fitz.open()
    try:
        out.insert_pdf(src, from_page=0, to_page=0)
        out.insert_pdf(src, from_page=total_pages - 1, to_page=total_pages - 1)
        with _tempfile.NamedTemporaryFile(suffix=".pdf", delete=False) as tmp:
            out_path = tmp.name
        try:
            out.save(out_path)
        except Exception:
            os.remove(out_path)
            raise
        return out_path
    finally:
        out.close()
        src.close()


def _extract_pdf_for_analysis(pdf_path: str) -> tuple[Optional[str], Optional[int], Optional[str]]:
    """PDF : texte natif d'abord, OCR en secours si la couche texte est vide
    ou insuffisante. Au-delà de _ANALYSIS_LARGE_PDF_PAGES pages, seules la
    1re et la dernière page sont lues (natif comme OCR), texte brut sans
    re-découpage. Une erreur d'OCR est remontée en avertissement explicite."""
    from RAG.shared.pdf_utils import extract_native_text, page_count

    try:
        total = page_count(pdf_path)
    except Exception as e:
        logger.warning(f"[Module8] PDF non ouvrable : {e}")
        return None, None, "Document illisible ou protégé : impossible à analyser automatiquement."

    large = total > _ANALYSIS_LARGE_PDF_PAGES
    page_cap = _ANALYSIS_MAX_CHARS // 2 if large else _ANALYSIS_MAX_CHARS
    sample_path = None
    if large:
        try:
            sample_path = _write_first_last_pdf(pdf_path, total)
        except Exception as e:
            logger.warning(f"[Module8] Extraction 1re/dernière page échouée : {e}")
            return None, None, "Document illisible ou protégé : impossible à analyser automatiquement."
    work_path = sample_path or pdf_path
    scope = "première et dernière page seulement" if large else None

    try:
        # 1) Texte natif (couche texte du PDF).
        native_text = ""
        try:
            extraction = extract_native_text(work_path)
            pages = [p.text for p in extraction.pages if p.text and p.text.strip()]
            if large:
                pages = [t[:page_cap] for t in pages]
            native_text = "\n".join(pages).strip()
        except Exception as e:
            logger.warning(f"[Module8] Extraction native échouée : {e}")

        if len(native_text) >= _ANALYSIS_MIN_NATIVE_CHARS:
            warning = (
                f"Document volumineux ({total} pages) : métadonnées déduites de la {scope}."
                if large else None
            )
            return native_text[:_ANALYSIS_MAX_CHARS], None, warning

        # 2) Secours OCR : texte natif vide ou insuffisant (PDF scanné).
        try:
            ocr_raw, engine = _ocr_pdf_chain(work_path)
            ocr_text = (ocr_raw or "").strip()
        except Exception as e:
            logger.error(f"[Module8] OCR du PDF échoué : {e}", exc_info=True)
            if native_text:
                return (
                    native_text[:_ANALYSIS_MAX_CHARS],
                    None,
                    "Reconnaissance de caractères (OCR) indisponible : seul un peu de texte a pu être lu, relis les suggestions.",
                )
            return (
                None,
                None,
                "La reconnaissance de caractères (OCR) a échoué pour l'instant : remplis le formulaire manuellement.",
            )

        if ocr_text:
            warning = "Document scanné : lecture par reconnaissance de caractères (OCR)"
            if engine == "local":
                warning += " locale de secours (Mistral indisponible)"
            if large:
                warning += f", {scope}"
            return ocr_text[:_ANALYSIS_MAX_CHARS], None, warning + ", relis les suggestions."

        if native_text:
            return native_text[:_ANALYSIS_MAX_CHARS], None, None
        return None, None, "Document illisible (vide après extraction native et OCR)."
    finally:
        if sample_path:
            try:
                os.remove(sample_path)
            except OSError:
                pass


def _extract_text_for_analysis(raw_bytes: bytes, filename: Optional[str]) -> tuple[Optional[str], Optional[int], Optional[str]]:
    """Lecture réelle du fichier  réutilise les briques RAG/api/ingestion,
    jamais recréées. Renvoie (texte_extrait, duree_secondes, avertissement)."""
    ext = os.path.splitext(filename or "")[1].lower() or ".pdf"
    warning = None

    with _tempfile.NamedTemporaryFile(suffix=ext, delete=False) as tmp:
        tmp.write(raw_bytes)
        tmp_path = tmp.name

    try:
        if ext == ".pdf":
            return _extract_pdf_for_analysis(tmp_path)

        if ext in _ANALYSIS_IMAGE_EXTENSIONS:
            try:
                from RAG.api.ingestion.vision_client import caption_image
                caption = caption_image(raw_bytes)
                ocr_text = ""
                try:
                    ocr_text = ocr_image(raw_bytes)
                except Exception:
                    pass
                text = f"{caption}\n{ocr_text}".strip()
                return (text or None), None, None
            except Exception as e:
                logger.warning(f"[Module8] Description d'image échouée : {e}")
                try:
                    from RAG.api.ingestion.ocr_client import ocr_image
                    ocr_only = (ocr_image(raw_bytes) or "").strip()
                except Exception as e2:
                    logger.error(f"[Module8] OCR image échoué : {e2}", exc_info=True)
                    return None, None, "Image illisible par l'IA de vision."
                if ocr_only:
                    return ocr_only[:_ANALYSIS_MAX_CHARS], None, "Description visuelle indisponible : texte de l'image lu par OCR, relis les suggestions."
                return None, None, "Image illisible par l'IA de vision."

        if ext in _ANALYSIS_VIDEO_EXTENSIONS or ext in _ANALYSIS_AUDIO_EXTENSIONS:
            try:
                from RAG.api.ingestion.transcription_client import transcribe_video
                segments = transcribe_video(tmp_path)
                text = " ".join(s.text for s in segments).strip()
                duration = int(segments[-1].end) if segments else None
                if not text:
                    return None, duration, "Aucune piste audio exploitable : la transcription n'a rien donné (vidéo muette ou silencieuse)."
                return text[:_ANALYSIS_MAX_CHARS], duration, None
            except Exception as e:
                logger.warning(f"[Module8] Transcription vidéo échouée : {e}")
                return None, None, "Transcription de la vidéo impossible pour l'instant."

        return None, None, f"Type de fichier non couvert par l'analyse automatique ({ext})."
    finally:
        try:
            os.remove(tmp_path)
        except OSError:
            pass


_CONTENT_KIND_FIELD_HINTS = {
    "epreuve": "titre, description, matière, niveau, difficulté estimée, année/session si mentionnée, type d'épreuve si détectable",
    "correction": "titre, description, matière, niveau",
    "livre": "titre, description, matière, niveau, étiquettes, auteur (tel qu'indiqué sur la page de titre/couverture, jamais inventé), éditeur si mentionné, nombre de pages si détectable",
    "pack": "titre, description, matière, niveau, étiquettes",
    "quiz": "titre, description, matière, niveau",
    "formation": "titre, description longue, description courte, matière, niveau, étiquettes, objectifs, prérequis",
    "video": "titre, description, matière, niveau, étiquettes",
}


@teacher_ai_router.post("/teacher/analyze-content-upload", response_model=AnalyzeContentUploadResponse)
def analyze_content_upload(
    body: AnalyzeContentUploadRequest,
    current_user: UserTokenData = Depends(require_role("teacher", "admin")),
):
    """
    Synchrone (def, pas async def  FastAPI l'exécute dans un threadpool,
    les appels bloquants qu'elle fait  requests, OCR, transcription  ne
    gèlent donc pas la boucle d'événements). Ne publie jamais rien : ne
    renvoie que des suggestions modifiables (Module 8) et une évaluation
    consultative (Module 7)  jamais de décision de publication.
    """
    raw_bytes, content_type = _download_for_analysis(body.file_url)
    text, duration_seconds, warning = _extract_text_for_analysis(raw_bytes, body.filename)

    if not text:
        # Fichier illisible : le formulaire doit rester utilisable en saisie
        # manuelle (cas limite explicite du Module 8), on ne bloque jamais.
        return AnalyzeContentUploadResponse(
            suggested=SuggestedContentFields(duration_seconds=duration_seconds),
            winai_score=None,
            winai_justification=None,
            extraction_warning=warning or "Contenu illisible  remplis le formulaire manuellement.",
        )

    hints = _CONTENT_KIND_FIELD_HINTS.get(body.content_kind, _CONTENT_KIND_FIELD_HINTS["epreuve"])
    constraint_lines = []
    if body.allowed_categories:
        constraint_lines.append(
            "Pour \"category\", choisis EXACTEMENT une valeur parmi cette liste fermée (recopie-la telle quelle, "
            "caractère pour caractère), ou une chaîne vide si aucune ne correspond au contenu réel : "
            + ", ".join(body.allowed_categories)
        )
    if body.allowed_levels:
        constraint_lines.append(
            "Pour \"level\", choisis EXACTEMENT une valeur parmi cette liste fermée (recopie-la telle quelle), "
            "ou une chaîne vide si aucune ne correspond : " + ", ".join(body.allowed_levels)
        )
    constraints = (" " + " ".join(constraint_lines)) if constraint_lines else ""
    prompt = (
        f"Voici le contenu réel d'un fichier déposé sur WinPlus (type déclaré : {body.content_kind}) :\n\n"
        f"{text}\n\n"
        f"À partir de CE contenu réel (pas du nom de fichier), propose des valeurs pour : {hints}.{constraints} "
        "Propose aussi un score de pertinence et de valeur pédagogique entre 0 et 100 (winai_score) avec une "
        "justification courte et lisible (winai_justification), et une suggestion de prix de vente en FCFA "
        "(price_suggestion) cohérente avec un contenu éducatif camerounais. "
        "Réponds en JSON strict avec exactement les clés : title, description, description_courte, category, "
        "level, difficulty (easy|medium|hard), year, exam_type, tags (liste), objectives (liste), "
        "prerequisites (liste), author, publisher, page_count (nombre), price_suggestion (nombre), "
        "winai_score (nombre 0-100), winai_justification (texte). "
        "Mets une chaîne vide ou une liste vide pour tout champ non pertinent ou non détectable  n'invente rien "
        "qui ne soit pas appuyé par le texte fourni. Limite description à 400 caractères maximum et "
        "description_courte à 120 caractères maximum, pour rester dans la place disponible."
    )
    system = (
        "Tu es WinAI, évaluateur pédagogique pour la plateforme WinPlus. Tu lis le contenu réel d'un document "
        "ou d'une transcription et tu en tires des métadonnées fidèles, jamais inventées. Réponds uniquement "
        "en JSON valide."
    )
    raw = _deepseek_json(prompt, system, max_tokens=2200)

    if not isinstance(raw, dict):
        return AnalyzeContentUploadResponse(
            suggested=SuggestedContentFields(duration_seconds=duration_seconds),
            winai_score=None,
            winai_justification=None,
            extraction_warning=warning or "WinAI n'a pas pu produire de suggestion pour l'instant  remplis le formulaire manuellement.",
        )

    def _str_list(v: Any) -> List[str]:
        if isinstance(v, list):
            return [str(x) for x in v if str(x).strip()]
        return []

    score = raw.get("winai_score")
    try:
        score = max(0.0, min(100.0, float(score))) if score is not None else None
    except (TypeError, ValueError):
        score = None

    price = raw.get("price_suggestion")
    try:
        price = float(price) if price is not None else None
    except (TypeError, ValueError):
        price = None

    suggested = SuggestedContentFields(
        title=(str(raw.get("title") or "").strip() or None),
        description=(str(raw.get("description") or "").strip() or None),
        description_courte=(str(raw.get("description_courte") or "").strip() or None),
        category=(str(raw.get("category") or "").strip() or None),
        level=(str(raw.get("level") or "").strip() or None),
        difficulty=(str(raw.get("difficulty") or "").strip() or None),
        year=(str(raw.get("year") or "").strip() or None),
        exam_type=(str(raw.get("exam_type") or "").strip() or None),
        tags=_str_list(raw.get("tags")),
        objectives=_str_list(raw.get("objectives")),
        prerequisites=_str_list(raw.get("prerequisites")),
        duration_seconds=duration_seconds,
        price_suggestion=price,
        author=(str(raw.get("author") or "").strip() or None),
        publisher=(str(raw.get("publisher") or "").strip() or None),
        page_count=(lambda v: int(v) if isinstance(v, (int, float)) and v > 0 else None)(raw.get("page_count")),
    )

    return AnalyzeContentUploadResponse(
        suggested=suggested,
        winai_score=score,
        winai_justification=(str(raw.get("winai_justification") or "").strip() or None),
        extraction_warning=warning,
    )
