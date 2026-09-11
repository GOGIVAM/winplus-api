"""Description textuelle indexable des images/schémas embarqués (GPT-4o-mini vision)."""

from __future__ import annotations

import base64

from openai import OpenAI

from RAG.api import config

_client: OpenAI | None = None

_PROMPT = (
    "Décris ce schéma ou diagramme technique de façon structurée et "
    "indexable : type de diagramme, éléments principaux, relations entre "
    "ces éléments, texte visible."
)


def _get_client() -> OpenAI:
    global _client
    if _client is None:
        _client = OpenAI(api_key=config.OPENAI_API_KEY)
    return _client


def caption_image(image_bytes: bytes) -> str:
    encoded = base64.b64encode(image_bytes).decode("utf-8")
    client = _get_client()
    response = client.chat.completions.create(
        model=config.VISION_MODEL,
        messages=[
            {
                "role": "user",
                "content": [
                    {"type": "text", "text": _PROMPT},
                    {"type": "image_url", "image_url": {"url": f"data:image/png;base64,{encoded}"}},
                ],
            }
        ],
        max_tokens=400,
    )
    return response.choices[0].message.content.strip()
