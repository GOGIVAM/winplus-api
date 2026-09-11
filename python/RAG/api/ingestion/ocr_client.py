"""Mistral OCR API — documents scannés (Phase 1 équivalent, voie API)."""

from __future__ import annotations

import base64
import logging

from mistralai.client import Mistral

from RAG.api import config

logger = logging.getLogger(__name__)

_client: Mistral | None = None


def _get_client() -> Mistral:
    global _client
    if _client is None:
        _client = Mistral(api_key=config.MISTRAL_API_KEY)
    return _client


def ocr_pdf(pdf_path: str) -> str:
    """Transcrit un PDF scanné complet via l'API Mistral OCR — retourne le
    texte concaténé de toutes les pages avec numérotation."""
    client = _get_client()
    with open(pdf_path, "rb") as f:
        encoded = base64.b64encode(f.read()).decode("utf-8")

    response = client.ocr.process(
        model=config.MISTRAL_OCR_MODEL,
        document={"type": "document_url", "document_url": f"data:application/pdf;base64,{encoded}"},
    )

    pages_text = []
    for i, page in enumerate(response.pages, start=1):
        pages_text.append(f"--- page {i} ---\n{page.markdown}")
    return "\n\n".join(pages_text)


def ocr_image(image_bytes: bytes) -> str:
    encoded = base64.b64encode(image_bytes).decode("utf-8")
    client = _get_client()
    response = client.ocr.process(
        model=config.MISTRAL_OCR_MODEL,
        document={"type": "image_url", "image_url": f"data:image/png;base64,{encoded}"},
    )
    return "\n".join(p.markdown for p in response.pages)
