"""
Point d'entrée UNIQUE des deux moteurs RAG (voir topo validé) — l'appelant
(ASP.NET Core via FastApiClient.cs, ou tout autre client futur) tape
toujours sur /rag/query et /rag/ingest, quel que soit le moteur actif. Le
choix se fait via la variable d'environnement RAG_BACKEND ("self_hosted" |
"api"), sans changement de code côté appelant.

/rag/ingest répond immédiatement (statut "queued") et lance le traitement
réel (OCR, transcription, embedding, indexation) en tâche d'arrière-plan —
un upload de vidéo ou de PDF volumineux ne doit jamais faire attendre
l'appelant. Utiliser GET /rag/ingest/{doc_id}/status pour suivre l'avancement.
"""

from __future__ import annotations

import logging

from fastapi import APIRouter, BackgroundTasks, Depends, HTTPException

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
    l'ancienne version) — évite qu'un document remplacé (nouvel upload sur
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
    """Exécutée en tâche d'arrière-plan par BackgroundTasks — tout ce qui
    peut prendre du temps (OCR, transcription vidéo, appels d'embedding)
    tourne ici, après que la réponse HTTP a déjà été envoyée à l'appelant."""
    process_document, index_chunks, _ = _active_backend()
    IngestJobRegistry.set_processing(request.doc_id)
    try:
        _supersede_previous_version(request.doc_id)
        chunks, source_type, warnings = process_document(request)
        index_chunks(chunks)
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
    IngestJobRegistry.set_queued(request.doc_id)
    background_tasks.add_task(_run_ingestion_job, request)
    return IngestQueuedResponse(doc_id=request.doc_id, status=IngestJobStatus.QUEUED)


@rag_router.get("/ingest/{doc_id}/status")
async def ingest_status(doc_id: str, current_user: UserTokenData = Depends(verify_token)):
    job = IngestJobRegistry.get(doc_id)
    if job is None:
        raise HTTPException(status_code=404, detail="Aucune ingestion connue pour ce doc_id")
    return job


@rag_router.post("/query", response_model=RAGAnswer)
async def query(request: RAGQueryRequest, current_user: UserTokenData = Depends(verify_token)):
    from RAG.query_service import query_rag

    try:
        return await query_rag(request)
    except Exception as e:
        logger.exception(f"[RAG] Échec de requête (backend={RAG_BACKEND})")
        raise HTTPException(status_code=500, detail=str(e))


@rag_router.get("/health")
async def health():
    return {"status": "ok", "active_backend": RAG_BACKEND}
