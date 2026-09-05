"""
WinAI Smart Routes  Fonctionnalités IA cross-comptes
  POST /api/ai/generate-notification    notification personnalisée DeepSeek
  POST /api/ai/summarize-notifications  résumé bullet-points des notifications non lues
  POST /api/ai/content-fit-analysis    analyse d'adéquation contenu/profil utilisateur
"""

from fastapi import APIRouter, HTTPException, status, Depends, Request
from pydantic import BaseModel
from typing import Optional, List, Dict, Any
import logging
import time
from datetime import datetime, timedelta

from services.deepseek_client import get_deepseek_client
from auth import verify_token, UserTokenData
from database import Database, User, QuizAttempt, DailyScore, Subject, DownloadHistory, Goal, QuizMistake

logger = logging.getLogger(__name__)

smart_ai_router = APIRouter(tags=["smart-ai"])

# ─── In-memory cache & rate-limit (TTL 24h, max 3 notifs/user/jour) ─────────

_notif_cache: Dict[tuple, tuple[float, dict]] = {}   # (user_id, type, hash) → (ts, result)
_notif_daily: Dict[int, tuple[int, str]] = {}         # user_id → (count, date_str)

_CACHE_TTL  = 86_400   # 24 h en secondes
_RATE_LIMIT = 3        # max notifications personnalisées par user par jour


def _rate_ok(user_id: int) -> bool:
    today = datetime.utcnow().strftime("%Y-%m-%d")
    entry = _notif_daily.get(user_id)
    if entry is None or entry[1] != today:
        _notif_daily[user_id] = (0, today)
        entry = _notif_daily[user_id]
    count, _ = entry
    if count >= _RATE_LIMIT:
        return False
    _notif_daily[user_id] = (count + 1, today)
    return True


def _cached(key: tuple) -> Optional[dict]:
    entry = _notif_cache.get(key)
    if entry and (time.time() - entry[0]) < _CACHE_TTL:
        return entry[1]
    return None


def _set_cache(key: tuple, value: dict) -> None:
    _notif_cache[key] = (time.time(), value)


# ─── Schemas ─────────────────────────────────────────────────────────────────

class GenerateNotificationRequest(BaseModel):
    user_id: int
    notification_type: str       # quiz_ready | subscription_expiry | progress_update | ...
    context_data: Dict[str, Any] = {}


class SummarizeNotificationsRequest(BaseModel):
    notification_ids: List[int] = []
    notification_texts: List[str]   # [title + "  " + body, ...]


class ContentFitRequest(BaseModel):
    user_id: int
    content_id: int


class RevisionContentRequest(BaseModel):
    user_id: int
    subject: str
    topic: Optional[str] = None


class QuizContentRequest(BaseModel):
    user_id: int
    subject: Optional[str] = None
    topic: Optional[str] = None
    level: Optional[str] = None
    context_hint: Optional[str] = None


# ─── 1. Generate smart notification ──────────────────────────────────────────

@smart_ai_router.post("/ai/generate-notification")
async def generate_notification(
    body: GenerateNotificationRequest,
    current_user: UserTokenData = Depends(verify_token),
):
    """
    Génère un titre + corps de notification personnalisés via DeepSeek.
    Cache 24h · Rate-limit 3/user/jour · Fallback sur message générique.
    """
    cache_key = (body.user_id, body.notification_type, str(sorted(body.context_data.items())))
    cached = _cached(cache_key)
    if cached:
        logger.info(f"[smart-notif] cache hit user={body.user_id} type={body.notification_type}")
        return cached

    if not _rate_ok(body.user_id):
        raise HTTPException(
            status_code=status.HTTP_429_TOO_MANY_REQUESTS,
            detail="Rate limit: max 3 notifications personnalisées par jour par utilisateur."
        )

    # Charger le prénom de l'utilisateur
    first_name = body.context_data.get("first_name", "")
    try:
        db = Database()
        session = db.SessionLocal()
        try:
            user = session.query(User).filter(User.Id == body.user_id).first()
            if user:
                first_name = user.FirstName or first_name
        finally:
            session.close()
    except Exception as e:
        logger.warning(f"Could not load user for notification: {e}")

    ctx_str = "\n".join(f"  {k}: {v}" for k, v in body.context_data.items())
    prenom_affiche = first_name or "l'utilisateur"

    prompt = (
        f"Tu es WinAI, l'assistant IA de WinPlus. Génère une notification mobile courte et personnalisée.\n\n"
        f"Prénom de l'utilisateur : {prenom_affiche}\n"
        f"Type de notification : {body.notification_type}\n"
        f"Données contextuelles :\n{ctx_str}\n\n"
        f"Génère UNIQUEMENT un JSON avec deux champs :\n"
        f'  "title": (max 60 caractères, accrocheur, personnel, commence par le prénom si possible)\n'
        f'  "body": (max 120 caractères, 1-2 phrases, ton chaleureux, info utile + invitation à agir)\n'
        f"Réponds en JSON pur, sans markdown."
    )

    try:
        client = get_deepseek_client()
        result = client.chat(
            messages=[{"role": "user", "content": prompt}],
            max_tokens=200,
            temperature=0.8
        )
        content = result.get("content", "").strip()
        # Strip code fences if any
        if content.startswith("```"):
            lines = content.split("\n")
            content = "\n".join(lines[1:]).rstrip("`").strip()

        import json as _json
        data = _json.loads(content)
        out = {
            "success": True,
            "title": str(data.get("title", ""))[:60],
            "body":  str(data.get("body",  ""))[:120],
        }
        _set_cache(cache_key, out)
        logger.info(f"[smart-notif] generated for user={body.user_id} type={body.notification_type}")
        return out
    except Exception as e:
        logger.error(f"[smart-notif] DeepSeek error: {e}")
        # Fallback générique  ne jamais lever d'erreur pour les notifications
        return {"success": False, "title": None, "body": None}


# ─── 2. Summarize unread notifications ───────────────────────────────────────

@smart_ai_router.post("/ai/summarize-notifications")
async def summarize_notifications(
    body: SummarizeNotificationsRequest,
    current_user: UserTokenData = Depends(verify_token),
):
    """
    Résume jusqu'à 50 notifications non lues en 3-5 bullet-points (DeepSeek).
    """
    if not body.notification_texts:
        raise HTTPException(status_code=400, detail="notification_texts est requis.")

    texts_sample = body.notification_texts[:50]
    texts_joined = "\n".join(f"- {t}" for t in texts_sample)

    prompt = (
        f"Tu es WinAI, l'assistant pédagogique de WinPlus.\n"
        f"Voici {len(texts_sample)} notifications non lues d'un utilisateur :\n\n"
        f"{texts_joined}\n\n"
        f"Résume les points importants en 3 à 5 bullet-points courts en français.\n"
        f"Format de chaque bullet : '• [point important]'\n"
        f"Sois concis, orienté action, ne répète pas les notifications mot pour mot.\n"
        f"Réponds UNIQUEMENT par les bullet-points, sans introduction ni conclusion."
    )

    try:
        client = get_deepseek_client()
        result = client.chat(
            messages=[{"role": "user", "content": prompt}],
            max_tokens=400,
            temperature=0.5
        )
        summary = result.get("content", "").strip()
        bullets = [line.strip() for line in summary.split("\n") if line.strip().startswith("•")]
        if not bullets:
            # Si DeepSeek n'a pas mis de bullet, on split par ligne
            bullets = [f"• {line.strip()}" for line in summary.split("\n") if line.strip()][:5]

        return {
            "success": True,
            "bullets": bullets,
            "count": len(body.notification_texts),
        }
    except Exception as e:
        logger.error(f"[summarize-notif] error: {e}")
        raise HTTPException(status_code=500, detail="Impossible de générer le résumé.")


# ─── 3. Content fit analysis ──────────────────────────────────────────────────

@smart_ai_router.post("/ai/content-fit-analysis")
async def content_fit_analysis(
    body: ContentFitRequest,
    current_user: UserTokenData = Depends(verify_token),
):
    """
    Analyse l'adéquation entre le profil d'un utilisateur et un contenu donné.
    Retourne : fit_score, explanation, warning, cta.
    """
    db = Database()
    session = db.SessionLocal()
    try:
        # Charger le contenu
        subject = session.query(Subject).filter(Subject.Id == body.content_id).first()
        if not subject:
            raise HTTPException(status_code=404, detail="Contenu introuvable.")

        subject_title    = subject.Title or ""
        subject_category = subject.Category or ""
        subject_desc     = (subject.Description or "")[:200]

        # Charger les performances de l'utilisateur
        cutoff = datetime.utcnow() - timedelta(days=90)
        attempts = (
            session.query(QuizAttempt)
            .filter(QuizAttempt.UserId == body.user_id, QuizAttempt.CreatedAt >= cutoff)
            .all()
        )
        quiz_count = len(attempts)
        avg_score: Optional[float] = None
        if attempts:
            avg_score = sum(float(a.Score or 0) for a in attempts) / len(attempts)

        perf_text = (
            f"{quiz_count} quiz tentés, score moyen {avg_score:.0f}%"
            if avg_score is not None
            else "Historique insuffisant (moins de 5 quiz)"
        )
        confidence_note = (
            f"Confiance élevée ({quiz_count} données)"
            if quiz_count >= 10
            else f"Données limitées ({quiz_count} quiz  résultat indicatif)"
        )
    finally:
        session.close()

    prompt = (
        f"Tu es WinAI, conseiller pédagogique de WinPlus.\n"
        f"Un utilisateur consulte le contenu suivant avant de l'acheter :\n"
        f"  Titre : {subject_title}\n"
        f"  Catégorie : {subject_category}\n"
        f"  Description : {subject_desc}\n\n"
        f"Profil utilisateur : {perf_text}.\n\n"
        f"Analyse l'adéquation et réponds UNIQUEMENT en JSON (sans markdown) :\n"
        "{\n"
        '  "fit_score": 0.XX,  // entre 0 et 1\n'
        '  "explanation": "...",  // 1-2 phrases, pourquoi ce contenu est/n\'est pas adapté\n'
        '  "warning": null,  // null ou phrase d\'avertissement si contenu redondant/inadapté\n'
        '  "cta": "Très recommandé pour toi"  // parmi : "Très recommandé pour toi" | "Recommandé" | "Optionnel" | "Déjà couvert"\n'
        "}"
    )

    try:
        client = get_deepseek_client()
        result = client.chat(
            messages=[{"role": "user", "content": prompt}],
            max_tokens=300,
            temperature=0.4
        )
        content = result.get("content", "").strip()
        if content.startswith("```"):
            lines = content.split("\n")
            content = "\n".join(lines[1:]).rstrip("`").strip()

        import json as _json
        data = _json.loads(content)

        fit_score = float(data.get("fit_score", 0.5))
        return {
            "success": True,
            "fit_score": round(min(max(fit_score, 0.0), 1.0), 2),
            "explanation": str(data.get("explanation", ""))[:300],
            "warning": data.get("warning"),
            "cta": data.get("cta", "Recommandé"),
            "confidence_note": confidence_note,
            "quiz_count": quiz_count,
        }
    except Exception as e:
        logger.error(f"[content-fit] error: {e}")
        raise HTTPException(status_code=500, detail="Analyse impossible pour le moment.")


@smart_ai_router.post("/revisions/generate-content", tags=["ai"])
async def generate_revision_content(
    body: RevisionContentRequest,
    current_user: UserTokenData = Depends(verify_token),
):
    """
    Génère une fiche de révision personnalisée pour un élève, à partir de :
      - ses erreurs récentes de quiz dans la matière (QuizMistakes)  ce qu'il
        faut vraiment revoir, pas un résumé générique du chapitre ;
      - les épreuves qu'il a téléchargées récemment  le niveau/contexte réel
        de ce qu'il étudie ;
      - ses objectifs actifs  pour orienter le ton (échéance proche = plus
        direct et pratique).
    Le .NET (RevisionsController) enregistre le résultat comme une nouvelle
    Revision (IsAIGenerated=true) et l'attribue à l'élève.
    """
    db = Database()
    session = db.SessionLocal()
    try:
        cutoff = datetime.utcnow() - timedelta(days=60)

        mistakes = (
            session.query(QuizMistake)
            .filter(
                QuizMistake.UserId == body.user_id,
                QuizMistake.Subject == body.subject,
                QuizMistake.CreatedAt >= cutoff,
            )
            .order_by(QuizMistake.CreatedAt.desc())
            .limit(10)
            .all()
        )
        mistakes_text = "\n".join(
            f"- {m.Question[:200]}"
            + (f" (réponse donnée : {m.GivenAnswer})" if m.GivenAnswer else "")
            + (f" (bonne réponse : {m.CorrectAnswer})" if m.CorrectAnswer else "")
            for m in mistakes
        ) or "Aucune erreur récente enregistrée  base-toi sur les fondamentaux du sujet."

        downloads = (
            session.query(Subject.Title)
            .join(DownloadHistory, DownloadHistory.SubjectId == Subject.Id)
            .filter(DownloadHistory.UserId == body.user_id)
            .order_by(DownloadHistory.CreatedAt.desc())
            .limit(5)
            .all()
        )
        downloads_text = ", ".join(d[0] for d in downloads) or "aucune épreuve téléchargée récemment"

        goals = (
            session.query(Goal)
            .filter(Goal.UserId == body.user_id, Goal.Status == "active")
            .order_by(Goal.TargetDate.asc())
            .limit(3)
            .all()
        )
        goals_text = "; ".join(g.Title for g in goals if g.Title) or "aucun objectif défini"
    finally:
        session.close()

    topic_line = f"Sous-thème demandé : {body.topic}\n" if body.topic else ""
    prompt = (
        f"Tu es WinAI, professeur particulier pour un lycéen camerounais préparant ses examens.\n"
        f"Rédige une fiche de révision personnalisée en {body.subject}.\n"
        f"{topic_line}\n"
        f"Erreurs récentes de l'élève dans cette matière :\n{mistakes_text}\n\n"
        f"Épreuves récemment téléchargées (contexte de niveau) : {downloads_text}\n"
        f"Objectifs actifs de l'élève : {goals_text}\n\n"
        f"Concentre la fiche sur ce que l'élève a réellement raté, pas un résumé générique du "
        f"programme. Réponds UNIQUEMENT en JSON (sans balises markdown autour) :\n"
        "{\n"
        '  "title": "...",  // court, spécifique au sous-thème réellement travaillé\n'
        '  "content_markdown": "...",  // fiche complète en Markdown : ## sections, explications, '
        'exemples chiffrés, astuces méthode  400 à 700 mots\n'
        '  "difficulty": "easy" | "medium" | "hard",\n'
        '  "estimated_duration_minutes": 15\n'
        "}"
    )

    try:
        client = get_deepseek_client()
        result = client.chat(
            messages=[{"role": "user", "content": prompt}],
            max_tokens=1800,
            temperature=0.5,
        )
        content = result.get("content", "").strip()
        if content.startswith("```"):
            lines = content.split("\n")
            content = "\n".join(lines[1:]).rstrip("`").strip()

        import json as _json
        data = _json.loads(content)

        return {
            "success": True,
            "title": str(data.get("title") or f"Révision  {body.subject}")[:255],
            "content_markdown": str(data.get("content_markdown") or ""),
            "difficulty": data.get("difficulty") if data.get("difficulty") in ("easy", "medium", "hard") else "medium",
            "estimated_duration_minutes": int(data.get("estimated_duration_minutes") or 15),
        }
    except Exception as e:
        logger.error(f"[revision-content] error: {e}")
        raise HTTPException(status_code=500, detail="Génération de la fiche impossible pour le moment.")


QUIZ_QUESTION_COUNT = 8


@smart_ai_router.post("/quizzes/generate-content", tags=["ai"])
async def generate_quiz_content(
    body: QuizContentRequest,
    current_user: UserTokenData = Depends(verify_token),
):
    """
    Génère un quiz d'entraînement (QCM). Deux cas :
      - body.subject fourni : ciblé sur les erreurs récentes de l'élève dans
        cette matière quand elles existent (QuizMistakes), sinon questions
        standards de niveau lycée/examens camerounais ;
      - body.subject absent (élève sans historique de quiz) : DeepSeek choisit
        lui-même la matière la plus utile à partir du niveau scolaire et du
        contexte fourni (objectifs actifs), et la renvoie dans la réponse.
    Même forme de réponse que /api/exam-quiz/generate ({success, data:
    {questions, subject}}) pour que le .NET (QuizService) réutilise le même
    parseur.
    """
    mistakes_text = ""
    if body.subject:
        db = Database()
        session = db.SessionLocal()
        try:
            cutoff = datetime.utcnow() - timedelta(days=60)
            mistakes = (
                session.query(QuizMistake)
                .filter(
                    QuizMistake.UserId == body.user_id,
                    QuizMistake.Subject == body.subject,
                    QuizMistake.CreatedAt >= cutoff,
                )
                .order_by(QuizMistake.CreatedAt.desc())
                .limit(8)
                .all()
            )
            mistakes_text = "\n".join(f"- {m.Question[:200]}" for m in mistakes)
        finally:
            session.close()

    topic_line = f"Sous-thème : {body.topic}\n" if body.topic else ""
    level_line = f"Niveau scolaire de l'élève : {body.level}.\n" if body.level else ""

    if body.subject:
        subject_line = f"Matière : {body.subject}.\n"
        subject_json_field = ""
        context_line = (
            f"L'élève a récemment eu du mal avec des questions proches de celles-ci "
            f"(reste sur les mêmes notions, formulations différentes) :\n{mistakes_text}\n\n"
            if mistakes_text else
            "Aucune erreur récente enregistrée  couvre les notions fondamentales du programme.\n\n"
        )
    else:
        subject_line = (
            f"Aucune matière n'est précisée : choisis toi-même celle qui aidera le plus cet élève, "
            f"à partir de son niveau scolaire et des indices suivants : {body.context_hint or 'aucun indice disponible'}.\n"
        )
        subject_json_field = '"subject":"...",  // la matière que tu as choisie\n  '
        context_line = ""

    prompt = (
        f"Génère exactement {QUIZ_QUESTION_COUNT} questions à choix multiples de niveau lycée/examens "
        f"camerounais.\n{subject_line}{level_line}{topic_line}\n{context_line}"
        f"Mélange les niveaux de difficulté. "
        f'Format JSON strict, un objet unique : '
        f'{{{subject_json_field}"questions":[{{"id":"q1","question":"...",'
        f'"options":["A) ...","B) ...","C) ...","D) ..."],'
        f'"correctAnswer":"B) ...","explanation":"..."}}]}} '
        f"correctAnswer doit correspondre EXACTEMENT à une des chaînes de options. "
        f"Réponds UNIQUEMENT avec cet objet JSON, sans texte autour ni balises de code."
    )
    system = (
        "Tu es WinAI, expert en évaluation pédagogique pour les examens camerounais. "
        "Tu génères des QCM rigoureux et pertinents, jamais de questions hors sujet."
    )

    try:
        client = get_deepseek_client()
        result = client.chat(
            messages=[{"role": "user", "content": prompt}],
            system_prompt=system,
            max_tokens=2200,
            temperature=0.5,
        )
        raw = result.get("content", "").strip()

        import re as _re
        import json as _json
        match = _re.search(r"\{.*\}", raw, _re.DOTALL)
        payload = _json.loads(match.group()) if match else {}
        chosen_subject = body.subject or payload.get("subject")
        parsed = payload.get("questions", [])

        questions = []
        for i, q in enumerate(parsed[:QUIZ_QUESTION_COUNT]):
            options = [str(o) for o in q.get("options", [])]
            correct = str(q.get("correctAnswer", ""))
            if not options or correct not in options:
                continue
            questions.append({
                "id": str(q.get("id") or f"q{i + 1}"),
                "question": str(q.get("question", "")).strip(),
                "options": options,
                "correctAnswer": correct,
                "explanation": str(q.get("explanation", "")).strip(),
            })

        if not questions:
            raise HTTPException(status_code=422, detail="La génération des questions a échoué. Réessaie.")

        return {"success": True, "data": {"questions": questions, "subject": chosen_subject}}
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"[quiz-content] error: {e}")
        raise HTTPException(status_code=500, detail="Génération du quiz impossible pour le moment.")
