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
from services.prompt_builder import build_system_prompt, UserContext
from auth import verify_token, UserTokenData
from schemas import ChatRequest, ChatResponse, ChatbotHealthResponse, ChatMessage, ChatbotContextRequest
from database import Database, Conversation, ChatMessage as ChatMessageDB

logger = logging.getLogger(__name__)

chatbot_router = APIRouter(tags=["chatbot"])


def _build_prompt_from_request(
    user_context: Optional[ChatbotContextRequest],
    token_data: UserTokenData,
) -> tuple[str, str]:
    """
    Converts request context + JWT data into a WinAI system prompt.
    Returns (system_prompt, winai_role) for logging.
    """
    ctx = UserContext(
        role=getattr(user_context, "role", None) or token_data.role or "student",
        first_name=getattr(user_context, "first_name", None),
        education_level=getattr(user_context, "education_level", None),
        grade=getattr(user_context, "grade", None),
        enrolled_subjects=[
            s.title for s in (user_context.enrolled_subjects or []) if s.title
        ] if user_context and user_context.enrolled_subjects else [],
        objectives=list(user_context.objectives or []) if user_context else [],
        learning_style=getattr(user_context, "learning_style", None),
        performance_history=dict(user_context.performance_history or {}) if user_context else {},
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
            "deepseek": deepseek_health
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
        "model": "deepseek-chat",
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
            "model": result.get('model', 'deepseek-chat'),
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
            "model": result.get('model', 'deepseek-chat'),
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

