"""
DeepSeek Client - Client pour communiquer avec l'API DeepSeek
"""

import os
import time
import logging
import requests
from typing import Dict, List, Optional, Any
from dotenv import load_dotenv

load_dotenv()
logger = logging.getLogger(__name__)


class DeepSeekClient:
    """Client pour l'API DeepSeek avec retry logic"""
    
    def __init__(self):
        self.base_url = os.getenv('DEEPSEEK_BASE_URL', 'http://localhost:8000')
        self.api_key = os.getenv('DEEPSEEK_API_KEY', '')
        self.model = os.getenv('DEEPSEEK_MODEL', 'deepseek-chat')
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
        temperature: Optional[float] = None
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
                content = result.get('choices', [{}])[0].get('message', {}).get('content', '')
                usage = result.get('usage', {})
                tokens_used = usage.get('total_tokens', 0)
                
                logger.info(f"DeepSeek response received: {tokens_used} tokens in {generation_time}ms")
                
                return {
                    "success": True,
                    "content": content,
                    "tokens_used": tokens_used,
                    "generation_time_ms": generation_time,
                    "model": result.get('model', self.model)
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
