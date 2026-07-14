"""
WinAI — Explications pédagogiques pour les erreurs de quiz.

POST /api/quiz/explain-error
  - Auth via verify_token
  - Rate limit per-user : 3 appels / minute (in-memory)
  - Cache SHA256 dans la table QuizExplanations
  - Streaming SSE via DeepSeek
"""

import hashlib
import json
import logging
from collections import defaultdict
from time import time
from typing import Optional

from fastapi import APIRouter, Depends, HTTPException, status
from fastapi.responses import StreamingResponse
from pydantic import BaseModel

from auth import verify_token, UserTokenData
from database import Database, QuizExplanation
from services.deepseek_client import get_deepseek_client

logger = logging.getLogger(__name__)

quiz_explain_router = APIRouter()

# ── Per-user rate limit ───────────────────────────────────────────────────────

_user_calls: dict[int, list[float]] = defaultdict(list)
_RATE_WINDOW = 60   # secondes
_RATE_MAX    = 3    # appels par fenêtre


def _allow(user_id: int) -> bool:
    now = time()
    history = [t for t in _user_calls[user_id] if now - t < _RATE_WINDOW]
    if len(history) >= _RATE_MAX:
        return False
    history.append(now)
    _user_calls[user_id] = history
    return True


# ── Schéma de requête ─────────────────────────────────────────────────────────

class ExplainErrorBody(BaseModel):
    question_text: str
    wrong_answer: str
    correct_answer: str
    subject: Optional[str] = ""
    level: Optional[str] = ""


# ── Endpoint ──────────────────────────────────────────────────────────────────

@quiz_explain_router.post('/explain-error')
async def explain_error(
    body: ExplainErrorBody,
    current_user: UserTokenData = Depends(verify_token),
):
    """
    Génère (ou récupère en cache) une explication pédagogique pour une erreur de quiz.
    Retourne un flux SSE : `data: {"delta": "..."}\\n\\n` + `data: [DONE]\\n\\n`.
    """
    if not _allow(current_user.user_id):
        raise HTTPException(
            status_code=status.HTTP_429_TOO_MANY_REQUESTS,
            detail="Limite d'explications WinAI atteinte. Réessaie dans une minute."
        )

    cache_key = hashlib.sha256(
        f"{body.question_text}{body.wrong_answer}{body.correct_answer}".encode()
    ).hexdigest()

    subject_label = body.subject or "cette matière"
    level_label   = body.level   or "cet examen"

    prompt = (
        f"Tu es WinAI, assistant pédagogique de WinPlus. "
        f"Un étudiant camerounais préparant {level_label} en {subject_label} "
        f"a répondu « {body.wrong_answer} » à la question : « {body.question_text} ». "
        f"La bonne réponse est « {body.correct_answer} ». "
        "Explique en 3-5 phrases maximum, en français simple et encourageant, "
        "pourquoi sa réponse est incorrecte et pourquoi la bonne réponse est juste. "
        "Sois concis et pédagogique. "
        "Ne commence pas par « La réponse est... » — commence par expliquer le concept."
    )

    def generate():
        db = Database()

        # ── Check cache ───────────────────────────────────────────────────────
        session = db.SessionLocal()
        try:
            cached = session.query(QuizExplanation).filter(
                QuizExplanation.QuestionHash == cache_key
            ).first()
            if cached:
                text = cached.ExplanationText
                chunk_size = 24
                for i in range(0, len(text), chunk_size):
                    yield f'data: {json.dumps({"delta": text[i:i + chunk_size]})}\n\n'
                yield "data: [DONE]\n\n"
                return
        finally:
            session.close()

        # ── Stream from DeepSeek + accumulate ────────────────────────────────
        client = get_deepseek_client()
        full_text = ""

        for sse_chunk in client.chat_stream(
            messages=[{"role": "user", "content": prompt}],
            max_tokens=400,
            temperature=0.5,
        ):
            if sse_chunk == "data: [DONE]\n\n":
                break
            yield sse_chunk
            try:
                data_str = sse_chunk[6:].strip()
                full_text += json.loads(data_str).get("delta", "")
            except (json.JSONDecodeError, KeyError):
                pass

        yield "data: [DONE]\n\n"

        # ── Save to cache ─────────────────────────────────────────────────────
        if full_text:
            session = db.SessionLocal()
            try:
                session.add(QuizExplanation(
                    QuestionHash=cache_key,
                    ExplanationText=full_text,
                    Subject=body.subject or None,
                ))
                session.commit()
                logger.info(f"[quiz-explain] Cached explanation for hash {cache_key[:8]}…")
            except Exception as exc:
                logger.error(f"[quiz-explain] Cache write failed: {exc}")
                session.rollback()
            finally:
                session.close()

    return StreamingResponse(
        generate(),
        media_type="text/event-stream",
        headers={"Cache-Control": "no-cache", "X-Accel-Buffering": "no"},
    )
