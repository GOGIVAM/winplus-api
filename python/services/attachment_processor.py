"""
Traitement des pièces jointes envoyées directement dans un message de chat
(à distinguer d'un upload catalogue via AdminExamsController/
AdminLibraryController/TeacherCourseController, qui reste public). Décision
utilisateur (topo validé) : "tout document uploadé doit servir dans la
base de connaissance" — une pièce jointe de chat, souvent personnelle
(devoir, brouillon), est donc toujours indexée dans la base PERSONNELLE de
l'utilisateur (owner_user_id, voir RAG/router.py), jamais dans le corpus
public partagé entre utilisateurs.

Deux effets, en parallèle, à chaque pièce jointe binaire (PDF, docx...) :
1. Extraction immédiate d'un aperçu texte (PDF natif, texte brut) injecté
   dans le prompt WinAI pour répondre à CE message sans attendre — c'est
   ce qui manquait avant (le fichier n'était jamais lu, juste nommé).
2. Ingestion RAG complète en tâche de fond (OCR si le PDF est scanné,
   embeddings, indexation) pour que le contenu redevienne cherchable sur
   les messages FUTURS de cet utilisateur, via services/rag_chat_bridge.py
   (scope personnel, toujours interrogé).
"""

from __future__ import annotations

import asyncio
import base64
import hashlib
import logging
import os
import re
from typing import Optional

logger = logging.getLogger(__name__)

_MIME_EXTENSIONS = {
    "application/pdf": ".pdf",
    "text/plain": ".txt",
    "text/csv": ".csv",
    "application/json": ".json",
    "text/markdown": ".md",
}
_MAX_PREVIEW_CHARS = 8000
_DATA_URL_RE = re.compile(r"^data:([^;,]+)?(;base64)?,")


def _parse_data_url(data_url_or_base64: str) -> tuple[str, Optional[str]]:
    """Renvoie (payload_base64, mime_type_ou_None)."""
    match = _DATA_URL_RE.match(data_url_or_base64)
    if not match:
        return data_url_or_base64, None
    return data_url_or_base64[match.end():], match.group(1)


def _guess_extension(mime_type: Optional[str], filename: Optional[str]) -> str:
    if mime_type and mime_type in _MIME_EXTENSIONS:
        return _MIME_EXTENSIONS[mime_type]
    ext = os.path.splitext(filename or "")[1]
    return ext if ext else ".bin"


def _extract_quick_preview(raw_bytes: bytes, ext: str) -> Optional[str]:
    """Extraction synchrone légère pour répondre à CE message sans attendre
    l'ingestion complète — PDF natif et texte brut seulement (pas d'OCR
    ici : trop lent pour rester synchrone, voir l'ingestion de fond)."""
    if ext == ".pdf":
        try:
            import fitz

            doc = fitz.open(stream=raw_bytes, filetype="pdf")
            text = "\n".join(page.get_text() for page in doc).strip()
            return text[:_MAX_PREVIEW_CHARS] if text else None
        except Exception as e:
            logger.debug(f"[Attachment] Extraction PDF native échouée (probablement scanné) : {e}")
            return None
    if ext in (".txt", ".csv", ".json", ".md"):
        try:
            return raw_bytes.decode("utf-8", errors="replace")[:_MAX_PREVIEW_CHARS]
        except Exception:
            return None
    return None


def _prepare(data_url_or_base64: str, filename: Optional[str], user_id: int) -> tuple[str, Optional[object]]:
    """Partie 100% synchrone (décodage + extraction native, potentiellement
    coûteuse en CPU) — SANS aucun appel asyncio, pour pouvoir tourner en
    toute sécurité dans un threadpool (voir process_chat_attachment_async).
    Renvoie (texte_pour_le_prompt, IngestRequest_ou_None à ingérer)."""
    from RAG.shared.contracts import IngestRequest

    name = filename or "document"
    try:
        payload, mime_type = _parse_data_url(data_url_or_base64)
        raw_bytes = base64.b64decode(payload)

        ext = _guess_extension(mime_type, filename)
        # Hash du contenu (pas un uuid) : si le même fichier est renvoyé
        # deux fois, l'ingestion re-déclenchée réutilise le même doc_id —
        # la supersession (RAG/router.py) évite un doublon dans le corpus
        # personnel plutôt que de l'indexer indéfiniment à chaque envoi.
        content_hash = hashlib.sha256(raw_bytes).hexdigest()[:16]
        doc_id = f"chatattach_{user_id}_{content_hash}"

        preview = _extract_quick_preview(raw_bytes, ext)
        ingest_request = IngestRequest(
            doc_id=doc_id,
            title=name,
            inline_content_base64=payload,
            file_extension_hint=ext,
            owner_user_id=user_id,
        )

        if preview:
            text = f"[Contenu du fichier joint « {name} »]\n{preview}"
        else:
            text = (
                f"[Pièce jointe « {name} » reçue — traitement en cours (une lecture optique peut être "
                "nécessaire pour un document scanné). Réponds à l'utilisateur avec ce que tu sais déjà, "
                "et indique-lui qu'il peut reposer sa question dans un instant une fois le document "
                "analysé, ou recopier directement le passage qui l'intéresse pour une réponse immédiate.]"
            )
        return text, ingest_request
    except Exception as e:
        logger.warning(f"[Attachment] Traitement de la pièce jointe échoué : {e}")
        return f"[Pièce jointe : {name} — contenu illisible.]", None


async def process_chat_attachment_async(data_url_or_base64: str, filename: Optional[str], user_id: int) -> str:
    """Point d'entrée async : décode/extrait dans un threadpool (ne bloque
    pas l'event loop sur un gros PDF), puis planifie l'ingestion RAG
    complète en tâche de fond (fire-and-forget, ne retarde jamais la
    réponse au message en cours) depuis le thread de la boucle d'événements
    — `asyncio.create_task` exige d'y être appelé, d'où la séparation
    stricte avec `_prepare` (qui, lui, ne doit JAMAIS appeler asyncio)."""
    from starlette.concurrency import run_in_threadpool

    from RAG.router import _run_ingestion_job

    text, ingest_request = await run_in_threadpool(_prepare, data_url_or_base64, filename, user_id)

    if ingest_request is not None:
        async def _runner() -> None:
            try:
                await run_in_threadpool(_run_ingestion_job, ingest_request)
            except Exception as e:
                logger.warning(f"[Attachment] Ingestion de fond échouée pour {ingest_request.doc_id} : {e}")

        asyncio.create_task(_runner())

    return text
