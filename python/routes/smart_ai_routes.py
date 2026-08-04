"""
WinAI Smart Routes — Fonctionnalités IA cross-comptes
  POST /api/ai/generate-notification   — notification personnalisée DeepSeek
  POST /api/ai/summarize-notifications — résumé bullet-points des notifications non lues
  POST /api/ai/content-fit-analysis   — analyse d'adéquation contenu/profil utilisateur
"""

from fastapi import APIRouter, HTTPException, status, Depends, Request
from pydantic import BaseModel
from typing import Optional, List, Dict, Any
import logging
import time
from datetime import datetime, timedelta

from services.deepseek_client import get_deepseek_client
from auth import verify_token, UserTokenData
from database import Database, User, QuizAttempt, DailyScore, Subject

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
    notification_texts: List[str]   # [title + " — " + body, ...]


class ContentFitRequest(BaseModel):
    user_id: int
    content_id: int


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
    prompt = (
        f"Tu es WinAI, l'assistant IA de WinPlus. Génère une notification mobile courte et personnalisée.\n\n"
        f"Prénom de l'utilisateur : {first_name or 'l\\'utilisateur'}\n"
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
        # Fallback générique — ne jamais lever d'erreur pour les notifications
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
            else f"Données limitées ({quiz_count} quiz — résultat indicatif)"
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
