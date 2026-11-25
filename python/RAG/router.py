"""
Point d'entrée UNIQUE des deux moteurs RAG (voir topo validé)  l'appelant
(ASP.NET Core via FastApiClient.cs, ou tout autre client futur) tape
toujours sur /rag/query et /rag/ingest, quel que soit le moteur actif. Le
choix se fait via la variable d'environnement RAG_BACKEND ("self_hosted" |
"api"), sans changement de code côté appelant.

/rag/ingest répond immédiatement (statut "queued") et lance le traitement
réel (OCR, transcription, embedding, indexation) en tâche d'arrière-plan 
un upload de vidéo ou de PDF volumineux ne doit jamais faire attendre
l'appelant. Utiliser GET /rag/ingest/{doc_id}/status pour suivre l'avancement.
"""

from __future__ import annotations

import logging

from fastapi import APIRouter, BackgroundTasks, Depends, HTTPException
from pydantic import BaseModel

from auth import UserTokenData, verify_token
from RAG.shared.config import RAG_BACKEND
from RAG.shared.contracts import (
    IngestJobStatus,
    IngestQueuedResponse,
    IngestRequest,
    RAGAnswer,
    RAGQueryRequest,
)
from RAG.shared.ingest_jobs import IngestJobRegistry

logger = logging.getLogger(__name__)

rag_router = APIRouter(prefix="/rag", tags=["RAG"])


def _active_backend():
    if RAG_BACKEND == "self_hosted":
        from RAG.self_hosted.engine.pipeline import run_query
        from RAG.self_hosted.indexing.pipeline import index_chunks
        from RAG.self_hosted.ingestion.pipeline import process_document

        return process_document, index_chunks, run_query

    from RAG.api.engine.pipeline import run_query
    from RAG.api.indexing.pipeline import index_chunks
    from RAG.api.ingestion.pipeline import process_document

    return process_document, index_chunks, run_query


def _supersede_previous_version(doc_id: str) -> None:
    """Marque `superseded` tout chunk déjà indexé sous ce doc_id, AVANT
    d'indexer la nouvelle version (les nouveaux chunks n'existent pas
    encore à cet instant, donc le filtre par doc_id ne peut matcher que
    l'ancienne version)  évite qu'un document remplacé (nouvel upload sur
    une fiche existante côté .NET, même doc_id réutilisé) laisse l'ancien
    contenu retrouvable indéfiniment aux côtés du nouveau. Best-effort :
    échoue silencieusement (log) sur une toute première ingestion, la
    collection Qdrant n'existant pas encore."""
    from RAG.shared.vector_store import mark_superseded

    if RAG_BACKEND == "self_hosted":
        from RAG.self_hosted.config import QDRANT_COLLECTION
    else:
        from RAG.api.config import QDRANT_COLLECTION

    try:
        mark_superseded(QDRANT_COLLECTION, doc_id, superseded_by=doc_id)
    except Exception as e:
        logger.debug(f"[RAG] Pas de version précédente à superseder pour doc_id={doc_id} ({e})")


def _run_ingestion_job(request: IngestRequest) -> None:
    """Exécutée en tâche d'arrière-plan par BackgroundTasks  tout ce qui
    peut prendre du temps (OCR, transcription vidéo, appels d'embedding)
    tourne ici, après que la réponse HTTP a déjà été envoyée à l'appelant."""
    from RAG.shared.file_resolver import resolve_local_path

    process_document, index_chunks, _ = _active_backend()
    IngestJobRegistry.set_processing(request.doc_id)
    try:
        _supersede_previous_version(request.doc_id)
        # resolve_local_path gère les trois cas (URL S3, chemin local,
        # contenu inline base64 d'une pièce jointe de chat)  process_document
        # (fitz/whisper) n'accepte qu'un chemin local, jamais une URL http(s).
        with resolve_local_path(request) as local_path:
            resolved_request = request.model_copy(update={"file_path": local_path})
            chunks, source_type, warnings = process_document(resolved_request)
        scoring_warnings = index_chunks(chunks)
        warnings = warnings + scoring_warnings
        from RAG.shared.contracts import IngestResult

        IngestJobRegistry.set_done(
            request.doc_id,
            IngestResult(doc_id=request.doc_id, chunks_indexed=len(chunks), source_type=source_type, warnings=warnings),
        )
        logger.info(f"[RAG] Ingestion terminée doc_id={request.doc_id} chunks={len(chunks)} backend={RAG_BACKEND}")
    except Exception as e:
        logger.exception(f"[RAG] Échec d'ingestion en arrière-plan doc_id={request.doc_id} (backend={RAG_BACKEND})")
        IngestJobRegistry.set_failed(request.doc_id, str(e))


@rag_router.post("/ingest", response_model=IngestQueuedResponse, status_code=202)
async def ingest(
    request: IngestRequest,
    background_tasks: BackgroundTasks,
    current_user: UserTokenData = Depends(verify_token),
):
    # Un document sans subject_id NI course_id est automatiquement traité
    # comme personnel (topo validé) : posé depuis le JWT, jamais accepté
    # tel quel du corps de la requête  un utilisateur ne doit pas pouvoir
    # usurper owner_user_id pour un autre compte.
    if request.subject_id is None and request.course_id is None:
        request.owner_user_id = current_user.user_id
    else:
        request.owner_user_id = None

    IngestJobRegistry.set_queued(request.doc_id)
    background_tasks.add_task(_run_ingestion_job, request)
    return IngestQueuedResponse(doc_id=request.doc_id, status=IngestJobStatus.QUEUED)


@rag_router.get("/ingest/{doc_id}/status")
async def ingest_status(doc_id: str, current_user: UserTokenData = Depends(verify_token)):
    job = IngestJobRegistry.get(doc_id)
    if job is None:
        raise HTTPException(status_code=404, detail="Aucune ingestion connue pour ce doc_id")
    return job


def _active_collection() -> str:
    if RAG_BACKEND == "self_hosted":
        from RAG.self_hosted import config as backend_config
    else:
        from RAG.api import config as backend_config
    return backend_config.QDRANT_COLLECTION


# ── Atelier WinAI : documents personnels (sources) ────────────────────────────
def _owner_id(user: UserTokenData) -> int:
    """Le jeton .NET porte user_id en chaîne (« 30 ») ; les extraits stockent owner_user_id en entier.
    Qdrant compare en tenant compte du type : sans conversion, rien ne correspond."""
    return int(user.user_id)


# État et suppression d'un document personnel, toujours restreints au
# propriétaire tiré du JWT : un utilisateur ne voit ni ne supprime jamais les
# extraits d'un autre compte, même s'il connaît leur doc_id.

@rag_router.get("/documents/{doc_id}/state")
async def document_state(doc_id: str, current_user: UserTokenData = Depends(verify_token)):
    """Nombre d'extraits actifs indexés pour ce document personnel, et état du
    travail d'ingestion s'il est encore connu (registre en mémoire, perdu au
    redémarrage : le nombre d'extraits fait foi)."""
    from RAG.shared.vector_store import count_by_filter

    chunks = count_by_filter(_active_collection(),
                             {"doc_id": doc_id, "owner_user_id": _owner_id(current_user), "status": "active"})
    job = IngestJobRegistry.get(doc_id)
    return {
        "doc_id": doc_id,
        "chunks": chunks,
        "job_status": getattr(getattr(job, "status", None), "value", getattr(job, "status", None)) if job is not None else None,
        "job_error": getattr(job, "error", None) if job is not None else None,
    }


@rag_router.delete("/documents/{doc_id}")
async def delete_document(doc_id: str, current_user: UserTokenData = Depends(verify_token)):
    """Supprime tous les extraits (toutes versions) de ce document personnel."""
    from RAG.shared.vector_store import delete_by_filter

    try:
        delete_by_filter(_active_collection(), {"doc_id": doc_id, "owner_user_id": _owner_id(current_user)})
    except Exception as e:
        logger.warning(f"[RAG] Suppression du document {doc_id} : {e}")
        raise HTTPException(status_code=503, detail="Suppression impossible pour l'instant")
    return {"doc_id": doc_id, "deleted": True}


@rag_router.post("/query", response_model=RAGAnswer)
async def query(request: RAGQueryRequest, current_user: UserTokenData = Depends(verify_token)):
    from RAG.query_service import query_rag

    try:
        return await query_rag(request)
    except Exception as e:
        logger.exception(f"[RAG] Échec de requête (backend={RAG_BACKEND})")
        raise HTTPException(status_code=500, detail=str(e))


class ChatAttachmentRequest(BaseModel):
    data_url_or_base64: str
    file_name: str | None = None


class ChatAttachmentResponse(BaseModel):
    text_for_prompt: str


@rag_router.post("/chat-attachment", response_model=ChatAttachmentResponse)
async def chat_attachment(request: ChatAttachmentRequest, current_user: UserTokenData = Depends(verify_token)):
    """Point d'entrée HTTP pour les pièces jointes de chat côté .NET
    (ChatbotController.cs::DescribeDocument, chemin /stream)  .NET n'a pas
    d'équivalent PyMuPDF pour extraire le texte lui-même. Même logique que
    services/attachment_processor.py, utilisée directement en interne côté
    Python par routes/chatbot_routes.py (chemin /chat, pas de HTTP self-call
    nécessaire dans ce cas). Toujours personnel (owner_user_id=current_user),
    jamais public : voir topo validé, "tout document uploadé doit servir
    dans la base de connaissance"."""
    from services.attachment_processor import process_chat_attachment_async

    text = await process_chat_attachment_async(request.data_url_or_base64, request.file_name, current_user.user_id)
    return ChatAttachmentResponse(text_for_prompt=text)


@rag_router.get("/health")
async def health():
    return {"status": "ok", "active_backend": RAG_BACKEND}
