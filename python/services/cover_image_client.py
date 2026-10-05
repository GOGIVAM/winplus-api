"""
Génération de pochette (vignette) de document par un modèle Gemini de génération d'image.

Brique dédiée, distincte de RAG/api/ingestion/vision_client.py (qui décrit une
image en texte, image -> texte). Ici : texte du document -> prompt -> image PNG.

Le stockage S3 n'est PAS fait ici : le backend .NET reçoit les octets et les
envoie via IStorageService.PutAsync (la brique de stockage existante).

Aucune clé n'est lue ni écrite autrement que par la variable d'environnement
GEMINI_API_KEY (déjà utilisée par la vision). Le modèle est surchargeable par
COVER_IMAGE_MODEL ; par défaut gemini-2.5-flash-image.

Vérification : la disponibilité réelle du modèle de génération d'image dépend
du compte Gemini. Si le modèle n'est pas accessible, generate_cover_png lève
CoverImageUnavailable : aucun résultat n'est simulé.
"""

from __future__ import annotations

import logging
import os
from dataclasses import dataclass

logger = logging.getLogger(__name__)

COVER_IMAGE_MODEL = os.getenv("COVER_IMAGE_MODEL", "gemini-2.5-flash-image")
GEMINI_API_KEY = os.getenv("GEMINI_API_KEY", "")


class CoverImageUnavailable(RuntimeError):
    """Génération impossible : clé absente, modèle indisponible ou réponse sans image."""


@dataclass(frozen=True)
class CoverImage:
    png_bytes: bytes
    mime_type: str
    model: str


def build_cover_prompt(title: str, description: str | None, kind: str) -> str:
    """Prompt de pochette : illustration sans texte lisible (le titre est affiché par l'interface)."""
    subject_line = (description or "").strip()[:600]
    return (
        "Crée une pochette de document pédagogique, format carré, illustration "
        "sobre et moderne, fond uni, sans aucun texte, lettre ni logo. "
        f"Type de contenu : {kind}. "
        f"Sujet : {title.strip()[:200]}. "
        + (f"Contexte : {subject_line}. " if subject_line else "")
    )


def generate_cover_png(title: str, description: str | None, kind: str) -> CoverImage:
    if not GEMINI_API_KEY:
        raise CoverImageUnavailable("GEMINI_API_KEY non configurée.")

    # Import tardif : le module reste importable sans le SDK en environnement de test.
    from google import genai
    from google.genai import types

    client = genai.Client(api_key=GEMINI_API_KEY)
    prompt = build_cover_prompt(title, description, kind)
    try:
        response = client.models.generate_content(
            model=COVER_IMAGE_MODEL,
            contents=prompt,
            config=types.GenerateContentConfig(response_modalities=["IMAGE"]),
        )
    except Exception as exc:  # modèle absent, quota, refus de sécurité côté API
        logger.warning("Génération de pochette refusée par %s : %s", COVER_IMAGE_MODEL, exc)
        raise CoverImageUnavailable(f"Modèle de génération d'image indisponible ({COVER_IMAGE_MODEL}).") from exc

    candidates = response.candidates or []
    for candidate in candidates:
        content = getattr(candidate, "content", None)
        for part in (getattr(content, "parts", None) or []):
            inline = getattr(part, "inline_data", None)
            if inline and inline.data:
                mime = inline.mime_type or "image/png"
                return CoverImage(png_bytes=bytes(inline.data), mime_type=mime, model=COVER_IMAGE_MODEL)

    raise CoverImageUnavailable("Le modèle n'a renvoyé aucune image.")
