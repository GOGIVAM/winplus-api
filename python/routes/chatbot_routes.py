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
from auth import verify_token, UserTokenData
from schemas import ChatRequest, ChatResponse, ChatbotHealthResponse, ChatMessage, ChatbotContextRequest
from database import Database, Conversation, ChatMessage as ChatMessageDB

logger = logging.getLogger(__name__)

chatbot_router = APIRouter(tags=["chatbot"])


def build_system_prompt(user_context: Optional[ChatbotContextRequest] = None) -> str:
    """
    Construit le prompt système personnalisé en fonction du contexte utilisateur
    
    Args:
        user_context: Contexte de l'utilisateur (niveau, matières, etc.)
    
    Returns:
        Prompt système personnalisé
    """
    base_prompt = """Tu es un assistant pédagogique intelligent pour WinPlus, une plateforme d'apprentissage.
Tu aides les étudiants dans leurs révisions et préparation aux concours.

Directives importantes:
- Réponds toujours en français sauf si l'utilisateur parle une autre langue
- Sois pédagogue, patient et encourage l'apprentissage
- Utilise le LaTeX pour les équations mathématiques (format $...$ pour inline, $$...$$ pour les blocs)
- Adapte ton niveau de langage au niveau de l'étudiant
- Fournis des explications claires, structurées et progressives
- Si tu ne sais pas, dis-le honnêtement plutôt que d'inventer
- Propose des exemples concrets et des exercices quand c'est pertinent
- Encourage l'étudiant et valorise ses efforts"""

    if not user_context:
        return base_prompt
    
    # Enrichir avec le contexte utilisateur
    context_additions = []
    
    if user_context.education_level or user_context.grade:
        level_info = f"\n\nProfil de l'étudiant:"
        if user_context.education_level:
            level_info += f"\n- Niveau: {user_context.education_level}"
        if user_context.grade:
            level_info += f"\n- Classe: {user_context.grade}"
        context_additions.append(level_info)
    
    # Matières inscrites
    if user_context.enrolled_subjects:
        subjects_list = ", ".join([s.title for s in user_context.enrolled_subjects if s.title])
        if subjects_list:
            context_additions.append(f"\n- Matières suivies: {subjects_list}")
    
    # Objectifs
    if user_context.objectives:
        context_additions.append(f"\n- Objectifs: {', '.join(user_context.objectives)}")
    
    # Style d'apprentissage
    if user_context.learning_style:
        context_additions.append(f"\n- Style d'apprentissage préféré: {user_context.learning_style}")
    
    if context_additions:
        base_prompt += "".join(context_additions)
    
    return base_prompt


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
        
        # Construire le prompt système
        if chat_request.system_prompt:
            system_prompt = chat_request.system_prompt
        else:
            system_prompt = build_system_prompt(chat_request.user_context)
        
        # Formater les messages
        formatted_messages = format_messages_for_deepseek(chat_request.messages)
        
        logger.info(f"Processing chat request from user {current_user.user_id} with {len(chat_request.messages)} messages")
        
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
    system_prompt = body.system_prompt or build_system_prompt()

    logger.info(f"Stream request from user {current_user.user_id}, conv_id={conv_id}")

    def generate():
        db = Database()
        session = db.get_session()
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

