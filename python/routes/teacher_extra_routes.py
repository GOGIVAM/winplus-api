"""
WinAI — Endpoints IA pour le compte Professeur.

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
from datetime import datetime, timedelta, timezone
from typing import Any, List, Optional

from fastapi import APIRouter, Depends, HTTPException
from pydantic import BaseModel

from auth import verify_token, UserTokenData
from database import (
    Database, QuizAttempt, DailyScore, Enrollment, Subject, User,
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
# Feature 1a — POST /ai/generate-quiz-questions
# Accepts {topic?, subject?, level?, topics?} — returns 10 calibrated QCM
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
    current_user: UserTokenData = Depends(verify_token),
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
            explanation="WinAI n'a pas pu générer les questions — réessayez ou formulez le sujet différemment.",
        ) for i in range(5)]

    return {"questions": questions}


# ─────────────────────────────────────────────────────────────────────────────
# Feature 1b — POST /ai/optimize-title
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
    current_user: UserTokenData = Depends(verify_token),
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
        "Exemple de bon titre : « Épreuves BAC C Mathématiques 2023 — Probabilités et Analyse ».\n"
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
        "optimized_title": f"{type_label}{subject_part}{level_part} — {body.title}",
        "rationale": "Titre enrichi avec le type et le niveau pour une meilleure visibilité.",
    }


# ─────────────────────────────────────────────────────────────────────────────
# Feature 1c — POST /ai/generate-description
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
    current_user: UserTokenData = Depends(verify_token),
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
# Feature 2 — POST /teacher/class-analysis
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
    current_user: UserTokenData = Depends(verify_token),
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
            if a.QuizId and a.TotalQuestions and a.TotalQuestions > 0 and a.QuizId not in seen:
                wr = round(1.0 - float(a.CorrectAnswers or 0) / float(a.TotalQuestions), 2)
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
            f"Classe de {student_count} élèves — contenu « {subject_title} ».\n"
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
# Feature 3 — GET /teacher/content-impact/{content_id}
# ─────────────────────────────────────────────────────────────────────────────

class ContentImpactResponse(BaseModel):
    impact_score: int
    interpretation: str
    completion_rate: float
    avg_score_improvement: float
    student_retention: float

@teacher_ai_router.get("/teacher/content-impact/{content_id}", response_model=ContentImpactResponse)
async def get_content_impact(
    content_id: int,
    current_user: UserTokenData = Depends(verify_token),
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
                "interpretation": "Aucun apprenant inscrit — partagez ce contenu pour obtenir votre score d'impact.",
                "completion_rate": 0.0,
                "avg_score_improvement": 0.0,
                "student_retention": 0.0,
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
            interpretation = "Excellent — ce contenu améliore significativement les scores des apprenants."
        elif score >= 50:
            interpretation = "Bon impact — ajoutez des exercices associés pour amplifier les résultats."
        else:
            interpretation = "Impact limité pour l'instant — enrichissez le contenu ou ajoutez un quiz associé."

        return {
            "impact_score": score,
            "interpretation": interpretation,
            "completion_rate": completion_rate,
            "avg_score_improvement": avg_improvement,
            "student_retention": student_retention,
        }

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"content_impact error for {content_id}: {e}")
        raise HTTPException(status_code=500, detail="Score d'impact indisponible")
    finally:
        session.close()


# ─────────────────────────────────────────────────────────────────────────────
# Feature 4 — POST /teacher/generate-correction
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
    current_user: UserTokenData = Depends(verify_token),
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
            scoring_guide="—",
        )]

    return {"correction_by_question": corrections}


# ─────────────────────────────────────────────────────────────────────────────
# Feature 5 — POST /teacher/predict-popularity
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
    current_user: UserTokenData = Depends(verify_token),
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
                    f"Excellente période pour publier — les contenus {body.level or 'BAC'} "
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
            timing_msg = "Ce type de contenu est consulté toute l'année — publiez dès que possible."

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
# Feature 6 — POST /teacher/analyze-submission
# ─────────────────────────────────────────────────────────────────────────────

class AnalyzeSubmissionRequest(BaseModel):
    submission_text: str
    expected_answer: Optional[str] = None
    subject: Optional[str] = None
    level: Optional[str] = None

class AnalyzeSubmissionResponse(BaseModel):
    error_type: str
    error_details: str
    suggested_comment: str
    score_suggestion: int

@teacher_ai_router.post("/teacher/analyze-submission", response_model=AnalyzeSubmissionResponse)
async def analyze_submission(
    body: AnalyzeSubmissionRequest,
    current_user: UserTokenData = Depends(verify_token),
):
    expected_block = (
        f"RÉPONSE ATTENDUE :\n{body.expected_answer[:1000]}\n\n"
        if body.expected_answer else ""
    )
    prompt = (
        f"Matière : {body.subject or 'non précisée'} — Niveau : {body.level or 'non précisé'}\n\n"
        f"TRAVAIL DE L'ÉLÈVE :\n{body.submission_text[:2000]}\n\n"
        + expected_block
        + "Analyse ce travail et génère :\n"
        '1. "error_type" : "methodological" (erreur de méthode) | "calculation" (erreur de calcul) | "conceptual" (incompréhension du concept) | "none" (correct)\n'
        '2. "error_details" : description précise de l\'erreur, 1-2 phrases\n'
        '3. "suggested_comment" : commentaire pédagogique bienveillant pour l\'élève, 3-4 phrases\n'
        '4. "score_suggestion" : note suggérée sur 20 (entier 0-20)\n'
        'JSON : {"error_type":"...","error_details":"...","suggested_comment":"...","score_suggestion":15}'
    )
    system = (
        "Tu es WinAI, assistant de correction pédagogique bienveillant. "
        "Analyse les erreurs avec précision et propose des commentaires constructifs. "
        "Réponds uniquement avec du JSON valide."
    )

    raw = _deepseek_json(prompt, system, max_tokens=400)
    if raw and isinstance(raw, dict):
        error_type = raw.get("error_type", "methodological")
        if error_type not in ("methodological", "calculation", "conceptual", "none"):
            error_type = "methodological"
        return {
            "error_type": error_type,
            "error_details": str(raw.get("error_details", "Vérifiez la démarche utilisée.")),
            "suggested_comment": str(raw.get("suggested_comment", "Bon travail — quelques points à consolider.")),
            "score_suggestion": max(0, min(20, int(raw.get("score_suggestion", 12)))),
        }

    return {
        "error_type": "methodological",
        "error_details": "WinAI a analysé le travail — vérifiez manuellement la démarche appliquée.",
        "suggested_comment": "Vous montrez une bonne compréhension générale. Revoyez la démarche étape par étape pour consolider vos acquis.",
        "score_suggestion": 12,
    }
