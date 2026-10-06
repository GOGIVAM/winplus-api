"""
DeepSeek Client - Client pour communiquer avec l'API DeepSeek
"""

import os
import json
import time
import logging
import requests
from typing import Dict, List, Optional, Any, Generator
from dotenv import load_dotenv

from services.token_accounting import estimate_prompt_tokens, estimate_tokens, usage_total

load_dotenv()
logger = logging.getLogger(__name__)


class DeepSeekClient:
    """Client pour l'API DeepSeek avec retry logic"""
    
    def __init__(self):
        self.base_url = os.getenv('DEEPSEEK_BASE_URL', 'http://localhost:8000')
        self.api_key = os.getenv('DEEPSEEK_API_KEY', '')
        self.model = os.getenv('DEEPSEEK_MODEL', 'WinAI-chat')
        self.timeout = int(os.getenv('DEEPSEEK_TIMEOUT', '60'))
        self.max_tokens = int(os.getenv('DEEPSEEK_MAX_TOKENS', '2000'))
        self.temperature = float(os.getenv('DEEPSEEK_TEMPERATURE', '0.7'))
        self.max_retries = 3
        
        logger.info(f"DeepSeekClient initialized with base_url: {self.base_url}")
    
    def chat(
        self,
        messages: List[Dict[str, str]],
        system_prompt: Optional[str] = None,
        max_tokens: Optional[int] = None,
        temperature: Optional[float] = None,
        json_mode: bool = False
    ) -> Dict[str, Any]:
        """
        Envoie une requête de chat à DeepSeek
        
        Args:
            messages: Liste des messages [{role: "user", content: "..."}, ...]
            system_prompt: Prompt système optionnel
            max_tokens: Nombre max de tokens (optionnel)
            temperature: Température de génération (optionnel)
            
        Returns:
            Dict avec content, tokens_used, generation_time_ms
        """
        start_time = time.time()
        
        # Préparer les messages avec le system prompt
        all_messages = []
        if system_prompt:
            all_messages.append({
                "role": "system",
                "content": system_prompt
            })
        all_messages.extend(messages)
        
        # Préparer la requête
        request_body = {
            "model": self.model,
            "messages": all_messages,
            "max_tokens": max_tokens or self.max_tokens,
            "temperature": temperature or self.temperature,
            "stream": False
        }
        if json_mode:
            # Mode JSON strict de l'API DeepSeek (garantit une syntaxe JSON
            # valide, mais ne protege pas d'une reponse coupee par max_tokens).
            request_body["response_format"] = {"type": "json_object"}

        headers = {
            "Content-Type": "application/json"
        }
        
        if self.api_key:
            headers["Authorization"] = f"Bearer {self.api_key}"
        
        # Retry logic
        last_error = None
        for attempt in range(self.max_retries):
            try:
                logger.info(f"Sending request to DeepSeek (attempt {attempt + 1})")
                
                response = requests.post(
                    f"{self.base_url}/v1/chat/completions",
                    json=request_body,
                    headers=headers,
                    timeout=self.timeout
                )
                
                response.raise_for_status()
                result = response.json()
                
                # Extraire la réponse
                generation_time = int((time.time() - start_time) * 1000)
                choice = result.get('choices', [{}])[0]
                content = choice.get('message', {}).get('content', '')
                finish_reason = choice.get('finish_reason')
                # Partie 8.1 : coût réel input+output tel que renvoyé par le
                # fournisseur (API compatible OpenAI : usage.prompt_tokens +
                # usage.completion_tokens). À défaut d'usage dans la réponse,
                # estimation ~4 caractères/token, signalée par usage_estimated.
                usage = result.get('usage') or {}
                prompt_tokens = int(usage.get('prompt_tokens') or 0)
                completion_tokens = int(usage.get('completion_tokens') or 0)
                tokens_used = usage_total(usage)
                usage_estimated = False
                if tokens_used <= 0:
                    prompt_tokens = estimate_prompt_tokens(all_messages)
                    completion_tokens = estimate_tokens(content)
                    tokens_used = prompt_tokens + completion_tokens
                    usage_estimated = True

                logger.info(f"DeepSeek response received: {tokens_used} tokens in {generation_time}ms (finish_reason={finish_reason})")
                # finish_reason == "length" : la réponse a été coupée par max_tokens
                # avant sa fin naturelle  très souvent du JSON invalide en aval
                # (chaîne non terminée, accolade fermante manquante) sans que ce
                # ne soit une erreur réseau. Le signaler explicitement permet aux
                # appelants de distinguer "DeepSeek a mal répondu" de "la réponse
                # a été tronquée", au lieu de deviner depuis un message json.loads.
                if finish_reason == "length":
                    logger.warning("DeepSeek response truncated by max_tokens (finish_reason=length)")

                return {
                    "success": True,
                    "content": content,
                    "tokens_used": tokens_used,
                    "prompt_tokens": prompt_tokens,
                    "completion_tokens": completion_tokens,
                    "usage_estimated": usage_estimated,
                    "generation_time_ms": generation_time,
                    "model": result.get('model', self.model),
                    "finish_reason": finish_reason,
                }
                
            except requests.exceptions.Timeout:
                last_error = "Request timeout"
                logger.warning(f"DeepSeek timeout on attempt {attempt + 1}")
                
            except requests.exceptions.ConnectionError as e:
                last_error = f"Connection error: {str(e)}"
                logger.warning(f"DeepSeek connection error on attempt {attempt + 1}: {e}")
                
            except requests.exceptions.HTTPError as e:
                last_error = f"HTTP error: {response.status_code}"
                logger.error(f"DeepSeek HTTP error: {response.status_code} - {response.text}")
                # Ne pas retenter pour les erreurs 4xx
                if response.status_code < 500:
                    break
                    
            except Exception as e:
                last_error = str(e)
                logger.error(f"DeepSeek unexpected error: {e}")
            
            # Attendre avant de retenter
            if attempt < self.max_retries - 1:
                time.sleep(2 ** attempt)  # Backoff exponentiel
        
        # Échec après tous les retries
        generation_time = int((time.time() - start_time) * 1000)
        return {
            "success": False,
            "content": "Je suis désolé, je ne peux pas répondre pour le moment. Veuillez réessayer.",
            "error": last_error,
            "tokens_used": 0,
            "generation_time_ms": generation_time,
            "model": self.model
        }
    
    def chat_stream(
        self,
        messages: List[Dict[str, str]],
        system_prompt: Optional[str] = None,
        max_tokens: Optional[int] = None,
        temperature: Optional[float] = None,
        emit_usage: bool = False,
    ) -> Generator[str, None, None]:
        """
        Envoie une requête de chat streamée à DeepSeek.
        Yields des chunks SSE au format 'data: {"delta": "..."}\\n\\n'.
        Dernier chunk : 'data: [DONE]\\n\\n'.

        Partie 8.1 : l'usage réel est demandé au fournisseur via
        `stream_options: {"include_usage": true}` (API compatible OpenAI :
        le DERNIER chunk porte `usage` avec une liste `choices` vide). Si
        `emit_usage` est vrai, un événement
        'data: {"usage_final": true, "tokens_used": N, "prompt_tokens": P,
        "completion_tokens": C, "usage_estimated": bool}' est émis juste avant
        [DONE] destiné au décompte (.NET le consomme sans le relayer au
        client, décision 8.3). Sans usage renvoyé : estimation ~4 car./token.

        Le nombre de tokens n'est plus inclus dans chaque delta : aucun
        client ne l'utilisait, et la décision 8.3 exclut tout compteur brut.
        """
        all_messages = []
        if system_prompt:
            all_messages.append({"role": "system", "content": system_prompt})
        all_messages.extend(messages)

        request_body = {
            "model": self.model,
            "messages": all_messages,
            "max_tokens": max_tokens or self.max_tokens,
            "temperature": temperature or self.temperature,
            "stream": True,
            "stream_options": {"include_usage": True},
        }

        headers = {"Content-Type": "application/json"}
        if self.api_key:
            headers["Authorization"] = f"Bearer {self.api_key}"

        usage: Dict[str, Any] = {}
        full_content = ""
        try:
            response = requests.post(
                f"{self.base_url}/v1/chat/completions",
                json=request_body,
                headers=headers,
                timeout=self.timeout,
                stream=True
            )
            # Serveur compatible OpenAI plus ancien qui refuse le paramètre
            # stream_options : on retente sans (l'usage sera alors estimé).
            if response.status_code == 400:
                logger.warning("DeepSeek a refusé stream_options (400) : nouvel essai sans, usage estimé")
                request_body.pop("stream_options", None)
                response = requests.post(
                    f"{self.base_url}/v1/chat/completions",
                    json=request_body,
                    headers=headers,
                    timeout=self.timeout,
                    stream=True
                )
            response.raise_for_status()

            for raw_line in response.iter_lines():
                if not raw_line:
                    continue
                line = raw_line.decode("utf-8") if isinstance(raw_line, bytes) else raw_line
                if not line.startswith("data: "):
                    continue
                data_str = line[6:]
                if data_str == "[DONE]":
                    break
                try:
                    data = json.loads(data_str)
                    # L'usage est lu AVANT les choices : avec include_usage, le
                    # dernier chunk porte `usage` et une liste `choices` VIDE.
                    # L'ancien code indexait [0] dessus, levait IndexError et
                    # perdait silencieusement l'usage.
                    if data.get("usage"):
                        usage = data["usage"]
                    choices = data.get("choices") or []
                    delta = (choices[0].get("delta", {}).get("content") or "") if choices else ""
                    if delta:
                        full_content += delta
                        chunk_data = json.dumps({"delta": delta})
                        yield f"data: {chunk_data}\n\n"
                except (json.JSONDecodeError, KeyError, IndexError, AttributeError):
                    pass

        except GeneratorExit:
            logger.info("Client disconnected during DeepSeek stream")
            return
        except Exception as e:
            logger.error(f"DeepSeek stream error: {e}")
            yield f'data: {json.dumps({"error": "Stream error"})}\n\n'

        if emit_usage:
            tokens_used = usage_total(usage)
            estimated = tokens_used <= 0
            if estimated:
                prompt_tokens = estimate_prompt_tokens(all_messages)
                completion_tokens = estimate_tokens(full_content)
                tokens_used = prompt_tokens + completion_tokens
            else:
                prompt_tokens = int(usage.get("prompt_tokens") or 0)
                completion_tokens = int(usage.get("completion_tokens") or 0)
            # Rien de généré ni d'usage renvoyé (erreur avant le premier
            # token) : pas d'événement, la réserve côté .NET décide.
            if full_content or not estimated:
                yield "data: " + json.dumps({
                    "usage_final": True,
                    "tokens_used": tokens_used,
                    "prompt_tokens": prompt_tokens,
                    "completion_tokens": completion_tokens,
                    "usage_estimated": estimated,
                }) + "\n\n"

        yield "data: [DONE]\n\n"

    def health_check(self) -> Dict[str, Any]:
        """Vérifie l'état du service DeepSeek"""
        try:
            response = requests.get(
                f"{self.base_url}/health",
                timeout=5
            )
            
            if response.status_code == 200:
                return {
                    "status": "healthy",
                    "service": "deepseek",
                    "base_url": self.base_url
                }
            else:
                return {
                    "status": "unhealthy",
                    "service": "deepseek",
                    "error": f"Status code: {response.status_code}"
                }
                
        except Exception as e:
            return {
                "status": "unreachable",
                "service": "deepseek",
                "error": str(e)
            }


# Singleton instance
_client = None

def get_deepseek_client() -> DeepSeekClient:
    """Récupère l'instance singleton du client DeepSeek"""
    global _client
    if _client is None:
        _client = DeepSeekClient()
    return _client
