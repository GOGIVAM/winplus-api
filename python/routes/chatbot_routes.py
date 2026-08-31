"""
Chatbot Routes - Router FastAPI pour le chatbot IA
"""

from fastapi import APIRouter, HTTPException, status, Depends, Request
from fastapi.responses import StreamingResponse
from pydantic import BaseModel
import json
import time
import logging
from typing import Dict, Any, List, Optional

from services.deepseek_client import get_deepseek_client
from services.prompt_builder import build_system_prompt, UserContext, detect_language
from auth import verify_token, UserTokenData
from schemas import ChatRequest, ChatResponse, ChatbotHealthResponse, ChatMessage, ChatbotContextRequest
from database import Database, Conversation, ChatMessage as ChatMessageDB, UserAIMemory, User, QuizAttempt, DailyScore, QuizMistake

logger = logging.getLogger(__name__)

chatbot_router = APIRouter(tags=["chatbot"])


def _load_performance_history(user_id: int) -> dict:
    """Calcule le score moyen par matière sur les 30 derniers jours depuis QuizAttempts."""
    try:
        from datetime import datetime, timedelta, timezone
        db = Database()
        session = db.SessionLocal()
        cutoff = datetime.now(timezone.utc) - timedelta(days=30)
        try:
            attempts = (
                session.query(QuizAttempt)
                .filter(QuizAttempt.UserId == user_id, QuizAttempt.CompletedAt >= cutoff)
                .all()
            )
            by_subject: dict[str, list[float]] = {}
            for a in attempts:
                subject = getattr(a, "SubjectTitle", None) or "Général"
                score = float(a.Score or 0)
                by_subject.setdefault(subject, []).append(score)
            return {s: round(sum(v) / len(v), 1) for s, v in by_subject.items()}
        finally:
            session.close()
    except Exception as e:
        logger.warning(f"Could not load performance history for user {user_id}: {e}")
        return {}


def _load_quiz_mistakes(user_id: int, limit: int = 10) -> list:
    """Charge les questions récentes ratées et non résolues pour enrichir le contexte WinAI."""
    try:
        db = Database()
        session = db.SessionLocal()
        try:
            rows = (
                session.query(QuizMistake)
                .filter(QuizMistake.UserId == user_id, QuizMistake.IsResolved == False)
                .order_by(QuizMistake.CreatedAt.desc())
                .limit(limit)
                .all()
            )
            return [
                {
                    "subject": r.Subject or "Général",
                    "question": r.Question,
                    "given_answer": r.GivenAnswer,
                    "correct_answer": r.CorrectAnswer,
                }
                for r in rows
            ]
        finally:
            session.close()
    except Exception as e:
        logger.warning(f"Could not load quiz mistakes for user {user_id}: {e}")
        return []


def _load_user_memories(user_id: int) -> list:
    """Charge les mémoires WinAI persistantes pour un utilisateur."""
    try:
        db = Database()
        session = db.SessionLocal()
        try:
            memories = session.query(UserAIMemory).filter(
                UserAIMemory.UserId == user_id
            ).order_by(UserAIMemory.UpdatedAt.desc()).limit(10).all()
            return [{"type": m.MemoryType, "content": m.Content} for m in memories]
        finally:
            session.close()
    except Exception as e:
        logger.warning(f"Could not load AI memories for user {user_id}: {e}")
        return []


def _load_parent_children_data(child_ids: list) -> list:
    """Charge un résumé des enfants pour enrichir le contexte parental WinAI."""
    if not child_ids:
        return []
    try:
        from datetime import datetime, timedelta, timezone
        db = Database()
        session = db.SessionLocal()
        now = datetime.now(timezone.utc)
        cutoff_30 = now - timedelta(days=30)
        children = []
        try:
            for child_id in child_ids[:5]:
                child = session.query(User).filter(User.Id == child_id).first()
                if not child:
                    continue
                scores = (
                    session.query(DailyScore)
                    .filter(DailyScore.UserId == child_id, DailyScore.CreatedAt >= cutoff_30)
                    .all()
                )
                avg_score = (
                    sum(float(s.AverageScore) for s in scores) / len(scores) * 20 / 100
                    if scores else None
                )
                children.append({
                    "name": child.FirstName or f"Enfant {child_id}",
                    "avg_score": round(avg_score, 1) if avg_score is not None else None,
                    "subjects": [],
                })
        finally:
            session.close()
        return children
    except Exception as e:
        logger.warning(f"Could not load parent children data: {e}")
        return []


_MEMORY_EXTRACTION_PROMPT = """Tu es un extracteur de mémoires pédagogiques. Analyse la réponse WinAI ci-dessous et extrait les informations mémorisables sur l'étudiant.

Retourne un tableau JSON (peut être vide []) avec des objets :
{"type": "<type>", "content": "<contenu court>"}

Types autorisés :
- struggling_topics : notion que l'étudiant a du mal à comprendre
- understood_topics : notion que l'étudiant maîtrise bien
- exam_context : examen ou objectif mentionné (ex: "Prépare le BAC C 2027")
- learning_preference : préférence d'apprentissage détectée
- motivation_style : style de motivation observé

Règles :
- Extrait uniquement ce qui est FACTUEL et DURABLE (pas les salutations, questions génériques)
- Maximum 3 mémoires par réponse
- Contenu concis (max 80 caractères)
- Si rien de mémorisable, retourne []

Réponse WinAI à analyser :
"""


def _extract_and_save_memories(user_id: int, assistant_content: str, session) -> None:
    """Extrait les mémoires depuis la réponse WinAI et les persiste en DB."""
    if not assistant_content or len(assistant_content) < 100:
        return
    try:
        client = get_deepseek_client()
        result = client.chat(
            messages=[{"role": "user", "content": assistant_content[:2000]}],
            system_prompt=_MEMORY_EXTRACTION_PROMPT,
            max_tokens=300,
            temperature=0.2,
        )
        if not result.get("success"):
            return
        raw = result.get("content", "").strip()
        # Extraire le JSON même si entouré de markdown
        if "```" in raw:
            raw = raw.split("```")[1]
            if raw.startswith("json"):
                raw = raw[4:]
        memories = json.loads(raw)
        if not isinstance(memories, list):
            return
        valid_types = {"struggling_topics", "understood_topics", "exam_context",
                       "learning_preference", "motivation_style"}
        now = __import__("datetime").datetime.utcnow()
        for m in memories[:3]:
            if not isinstance(m, dict):
                continue
            mtype = m.get("type", "")
            content = (m.get("content") or "").strip()
            if mtype not in valid_types or not content:
                continue
            # Upsert : met à jour si même type + contenu similaire existe déjà
            existing = session.query(UserAIMemory).filter(
                UserAIMemory.UserId == user_id,
                UserAIMemory.MemoryType == mtype,
                UserAIMemory.Content == content,
            ).first()
            if existing:
                existing.UpdatedAt = now
            else:
                session.add(UserAIMemory(
                    UserId=user_id,
                    MemoryType=mtype,
                    Content=content,
                    CreatedAt=now,
                    UpdatedAt=now,
                ))
        session.commit()
        logger.info(f"Saved {len(memories)} memories for user {user_id}")
    except Exception as e:
        logger.warning(f"Memory extraction failed for user {user_id}: {e}")
        try:
            session.rollback()
        except Exception:
            pass


def _build_prompt_from_request(
    user_context: Optional[ChatbotContextRequest],
    token_data: UserTokenData,
) -> tuple[str, str]:
    """
    Converts request context + JWT data into a WinAI system prompt.
    Returns (system_prompt, winai_role) for logging.
    """
    role = getattr(user_context, "role", None) or token_data.role or "student"
    memories = _load_user_memories(token_data.user_id) if token_data.user_id else []
    quiz_mistakes = _load_quiz_mistakes(token_data.user_id) if token_data.user_id and role == "student" else []
    # performance_history : priorité au front (déjà calculé), sinon on calcule depuis la DB
    perf_from_front = dict(getattr(user_context, "performance_history", None) or {})
    performance_history = perf_from_front if perf_from_front else (
        _load_performance_history(token_data.user_id) if token_data.user_id else {}
    )

    # For parents, inject children context if child_ids are provided
    children_data: list = []
    if role == "parent":
        raw_child_ids = getattr(user_context, "child_ids", None) or []
        if raw_child_ids:
            children_data = _load_parent_children_data(list(raw_child_ids))

    # Résoudre la langue : préférence explicite > auto-détection depuis le dernier message
    force_lang = getattr(user_context, "force_language", None) if user_context else None

    ctx = UserContext(
        role=role,
        first_name=getattr(user_context, "first_name", None),
        education_level=getattr(user_context, "education_level", None),
        grade=getattr(user_context, "grade", None),
        enrolled_subjects=[
            s.title for s in (user_context.enrolled_subjects or []) if s.title
        ] if user_context and user_context.enrolled_subjects else [],
        objectives=list(user_context.objectives or []) if user_context else [],
        learning_style=getattr(user_context, "learning_style", None),
        performance_history=performance_history,
        ai_memories=memories,
        children_data=children_data,
        language=force_lang,
        quiz_mistakes=quiz_mistakes,
        recent_activity=list(getattr(user_context, "recent_activity", None) or []),
        navigation_history=list(getattr(user_context, "navigation_history", None) or []),
    )
    return build_system_prompt(ctx), ctx.role


def format_messages_for_deepseek(messages: List[ChatMessage]) -> List[Dict[str, str]]:
    """
    Formate les messages pour l'API DeepSeek
    
    Args:
        messages: Liste de messages avec role, content, attachments
        
    Returns:
        Messages formatés pour DeepSeek
    """
    formatted = []
    
    for msg in messages:
        content = msg.content
        attachments = msg.attachments or []
        
        # Ajouter les descriptions des attachments au contenu
        if attachments:
            attachment_descriptions = []
            for att in attachments:
                if att.type == 'image':
                    attachment_descriptions.append("[Image attachée]")
                elif att.type == 'equation':
                    attachment_descriptions.append(f"[Équation: {att.data}]")
                else:
                    attachment_descriptions.append(f"[Fichier: {att.file_name or 'fichier'}]")
            
            if attachment_descriptions:
                content = f"{content}\n\n{' '.join(attachment_descriptions)}"
        
        formatted.append({
            "role": msg.role,
            "content": content
        })
    
    return formatted


@chatbot_router.get('/health', response_model=ChatbotHealthResponse, tags=["chatbot"])
async def health():
    """Health check pour le service chatbot"""
    try:
        deepseek_client = get_deepseek_client()
        deepseek_health = deepseek_client.health_check()
        
        return {
            "status": "healthy",
            "service": "chatbot-fastapi",
            "ai_service": deepseek_health
        }
    except Exception as e:
        logger.error(f"Chatbot health check failed: {e}")
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=str(e)
        )


@chatbot_router.post('/chat', response_model=ChatResponse, tags=["chatbot"])
async def chat(
    chat_request: ChatRequest,
    current_user: UserTokenData = Depends(verify_token)
):
    """
    Endpoint principal pour le chat
    
    Requête:
    {
        "messages": [{"role": "user", "content": "...", "attachments": [...]}],
        "systemPrompt": "...",
        "userContext": {...},
        "maxTokens": 2000,
        "temperature": 0.7
    }
    
    Réponse:
    {
        "content": "...",
        "tokensUsed": 123,
        "generationTimeMs": 456,
        "model": "WinAI-chat",
        "success": true
    }
    """
    try:
        if not chat_request.messages:
            raise HTTPException(
                status_code=status.HTTP_400_BAD_REQUEST,
                detail="Messages are required"
            )
        
        # Construire le prompt système différencié par rôle
        if chat_request.system_prompt:
            system_prompt = chat_request.system_prompt
            winai_role = getattr(chat_request.user_context, "role", None) or current_user.role or "student"
        else:
            system_prompt, winai_role = _build_prompt_from_request(
                chat_request.user_context, current_user
            )

        # Formater les messages
        formatted_messages = format_messages_for_deepseek(chat_request.messages)

        logger.info(
            f"Processing chat request from user {current_user.user_id} "
            f"winai_role={winai_role} messages={len(chat_request.messages)}"
        )
        
        # Appeler DeepSeek
        deepseek_client = get_deepseek_client()
        result = deepseek_client.chat(
            messages=formatted_messages,
            system_prompt=system_prompt,
            max_tokens=chat_request.max_tokens,
            temperature=chat_request.temperature
        )
        
        logger.info(f"Chat response: success={result.get('success')}, tokens={result.get('tokens_used')}")
        
        # Retourner la réponse au format attendu
        return {
            "content": result.get('content', ''),
            "tokens_used": result.get('tokens_used', 0),
            "generation_time_ms": result.get('generation_time_ms', 0),
            "success": result.get('success', False),
            "error": result.get('error')
        }

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error in chat endpoint: {e}")
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=str(e)
        )


class StreamChatBody(BaseModel):
    messages: List[Dict[str, Any]]
    conversation_id: Optional[int] = None
    system_prompt: Optional[str] = None
    user_context: Optional[ChatbotContextRequest] = None
    max_tokens: Optional[int] = 2000
    temperature: Optional[float] = 0.7


@chatbot_router.post('/stream', tags=["chatbot"])
async def stream_chat(
    body: StreamChatBody,
    current_user: UserTokenData = Depends(verify_token)
):
    """
    Stream SSE depuis DeepSeek, persiste le message assistant en fin de stream.
    Format chunk : data: {"delta": "...", "tokens_used": N}\\n\\n
    Dernier chunk : data: [DONE]\\n\\n
    """
    conv_id = body.conversation_id
    messages = body.messages

    if body.system_prompt:
        system_prompt = body.system_prompt
        winai_role = getattr(body.user_context, "role", None) or current_user.role or "student"
    else:
        # Auto-détection de langue si non forcée : lit le dernier message utilisateur
        if body.user_context and not getattr(body.user_context, "force_language", None):
            last_user_msgs = [m for m in (body.messages or []) if isinstance(m, dict) and m.get("role") == "user"]
            if last_user_msgs:
                detected = detect_language(last_user_msgs[-1].get("content", ""))
                body.user_context.force_language = detected  # type: ignore[assignment]
        system_prompt, winai_role = _build_prompt_from_request(body.user_context, current_user)

    logger.info(f"Stream request from user {current_user.user_id}, winai_role={winai_role}, conv_id={conv_id}")

    def generate():
        db = Database()
        session = db.SessionLocal()
        full_content = ""
        tokens_used = 0
        start_time = time.time()

        try:
            client = get_deepseek_client()
            for chunk in client.chat_stream(
                messages=messages,
                system_prompt=system_prompt,
                max_tokens=body.max_tokens,
                temperature=body.temperature
            ):
                if chunk.startswith("data: ") and chunk.strip() != "data: [DONE]":
                    try:
                        data = json.loads(chunk[6:].strip())
                        full_content += data.get("delta", "")
                        tokens_used = max(tokens_used, data.get("tokens_used", 0))
                    except Exception:
                        pass
                yield chunk
        except GeneratorExit:
            logger.info(f"Client disconnected for conv {conv_id}")
        except Exception as e:
            logger.error(f"Stream error for conv {conv_id}: {e}")
            yield f'data: {json.dumps({"error": str(e)})}\n\n'
            yield "data: [DONE]\n\n"
        finally:
            if full_content and conv_id:
                try:
                    generation_time = int((time.time() - start_time) * 1000)
                    msg = ChatMessageDB(
                        ConversationId=conv_id,
                        Role="assistant",
                        Content=full_content,
                        TokensUsed=tokens_used,
                        GenerationTimeMs=generation_time
                    )
                    session.add(msg)
                    session.commit()
                    logger.info(f"Saved assistant message ({len(full_content)} chars) for conv {conv_id}")
                except Exception as e:
                    logger.error(f"Failed to save assistant message: {e}")
                    session.rollback()
            # Extraction asynchrone des mémoires (rôle étudiant seulement)
            if full_content and current_user.user_id and winai_role == "student":
                _extract_and_save_memories(current_user.user_id, full_content, session)
            session.close()

    return StreamingResponse(
        generate(),
        media_type="text/event-stream",
        headers={"Cache-Control": "no-cache", "X-Accel-Buffering": "no"}
    )


@chatbot_router.post('/complete', response_model=ChatResponse, tags=["chatbot"])
async def complete(
    request_data: Dict[str, Any],
    current_user: UserTokenData = Depends(verify_token)
):
    """
    Endpoint pour complétion simple (sans historique)
    
    Requête:
    {
        "prompt": "...",
        "maxTokens": 2000,
        "temperature": 0.7
    }
    """
    try:
        prompt = request_data.get('prompt', '')
        if not prompt:
            raise HTTPException(
                status_code=status.HTTP_400_BAD_REQUEST,
                detail="Prompt is required"
            )
        
        max_tokens = request_data.get('maxTokens', 2000)
        temperature = request_data.get('temperature', 0.7)
        
        messages = [{"role": "user", "content": prompt}]
        
        logger.info(f"Processing completion request from user {current_user.user_id}")
        
        deepseek_client = get_deepseek_client()
        result = deepseek_client.chat(
            messages=messages,
            max_tokens=max_tokens,
            temperature=temperature
        )
        
        return {
            "content": result.get('content', ''),
            "tokens_used": result.get('tokens_used', 0),
            "generation_time_ms": result.get('generation_time_ms', 0),
            "success": result.get('success', False),
            "error": result.get('error')
        }

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error in complete endpoint: {e}")
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=str(e)
        )

