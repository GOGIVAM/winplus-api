"""
Description textuelle indexable des images/schémas embarqués — Gemini 2.5
Flash, retenu plutôt que GPT-4o-mini : environ 3-4x moins cher par image
(moins de tokens consommés par image, à tarif par token comparable),
vérifié en ligne (voir RAG/README.md).
"""

from __future__ import annotations

from google import genai
from google.genai import types

from RAG.api import config

_client: genai.Client | None = None

_PROMPT = (
    "Décris ce schéma ou diagramme technique de façon structurée et "
    "indexable : type de diagramme, éléments principaux, relations entre "
    "ces éléments, texte visible."
)


def _get_client() -> genai.Client:
    global _client
    if _client is None:
        _client = genai.Client(api_key=config.GEMINI_API_KEY)
    return _client


def caption_image(image_bytes: bytes) -> str:
    client = _get_client()
    response = client.models.generate_content(
        model=config.VISION_MODEL,
        contents=[
            types.Part.from_bytes(data=image_bytes, mime_type="image/png"),
            _PROMPT,
        ],
    )
    return (response.text or "").strip()
