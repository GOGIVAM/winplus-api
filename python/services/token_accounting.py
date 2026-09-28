"""
Comptabilité des tokens WinAI Partie 8 du suivi (quota au token réel).

Source de vérité (décision 8.1) : le compte renvoyé par le fournisseur LLM
dans le champ `usage` de sa réponse (API compatible OpenAI :
`usage.prompt_tokens` + `usage.completion_tokens`, `usage.total_tokens`).
Voir services/deepseek_client.py, qui le capture en non-streaming ET en
streaming (via `stream_options.include_usage`).

Ce module ne sert que de REPLI quand le fournisseur ne renvoie pas d'usage
(serveur auto-hébergé qui ignore `stream_options`, flux coupé avant le
dernier chunk…) et pour les étapes qui n'ont pas de compte natif (8.2) :

    ⚠ ESTIMATION, PAS MESURE : ~4 caractères par token. C'est l'approximation
    standard pour les tokenizers BPE sur du texte latin (anglais ~4, français
    un peu moins). Elle est volontairement simple et documentée comme telle.

8.2 audio et fichiers : la transcription audio, le texte OCR/vision et le
texte extrait d'un PDF sont INJECTÉS dans le prompt (attachment_processor.py,
rag_chat_bridge.py, ChatbotController.DescribeDocumentAsync côté .NET). Ils
sont donc comptés tels qu'ils sont réellement envoyés au LLM : dans
`usage.prompt_tokens` quand le fournisseur le renvoie, dans l'estimation
ci-dessous sinon. Une image envoyée en bloc `image_url` (vision native) est
comptée au forfait IMAGE_TOKEN_ESTIMATE quand l'usage réel manque.
"""

from __future__ import annotations

import math
from typing import Any, Dict, Iterable, Optional

CHARS_PER_TOKEN = 4

# Forfait d'estimation pour une image transmise au modèle en vision native,
# quand le fournisseur ne renvoie pas d'usage. Ordre de grandeur d'une image
# « haute définition » découpée en tuiles chez les fournisseurs compatibles
# OpenAI (~765 tokens). ESTIMATION documentée, jamais utilisée si l'usage réel
# est disponible.
IMAGE_TOKEN_ESTIMATE = 765

# Surcoût fixe par message (balises de rôle / séparateurs du format chat).
PER_MESSAGE_OVERHEAD = 4


def estimate_tokens(text: Optional[str]) -> int:
    """Estimation ~4 caractères/token. 0 pour un texte vide."""
    if not text:
        return 0
    return math.ceil(len(text) / CHARS_PER_TOKEN)


def _content_tokens(content: Any) -> int:
    # Contenu simple (chaîne) ou liste de blocs multimodaux
    # ({type:"text"}, {type:"image_url"}) format produit par
    # ChatbotController.StreamChat pour les pièces jointes.
    if isinstance(content, str):
        return estimate_tokens(content)
    if isinstance(content, list):
        total = 0
        for block in content:
            if not isinstance(block, dict):
                continue
            if block.get("type") == "text":
                total += estimate_tokens(block.get("text"))
            elif block.get("type") == "image_url":
                total += IMAGE_TOKEN_ESTIMATE
        return total
    return 0


def estimate_prompt_tokens(messages: Iterable[Dict[str, Any]], system_prompt: Optional[str] = None) -> int:
    """Estimation du coût d'entrée : prompt système + tous les messages, y
    compris le texte des pièces jointes/transcriptions déjà injecté (8.2)."""
    total = estimate_tokens(system_prompt)
    for msg in messages or []:
        if isinstance(msg, dict):
            total += PER_MESSAGE_OVERHEAD + _content_tokens(msg.get("content"))
    return total


def usage_total(usage: Optional[Dict[str, Any]]) -> int:
    """Total réel input+output d'un objet `usage` compatible OpenAI.

    On somme prompt_tokens + completion_tokens quand ils sont présents (ce
    sont les deux grandeurs facturées) ; `total_tokens` sert de repli."""
    if not usage:
        return 0
    prompt = usage.get("prompt_tokens")
    completion = usage.get("completion_tokens")
    if isinstance(prompt, int) or isinstance(completion, int):
        return int(prompt or 0) + int(completion or 0)
    total = usage.get("total_tokens")
    return int(total) if isinstance(total, (int, float)) else 0
