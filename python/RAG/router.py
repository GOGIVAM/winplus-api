"""
Point d'entrée UNIQUE des deux moteurs RAG (voir topo validé) — l'appelant
(ASP.NET Core via FastApiClient.cs, ou tout autre client futur) tape
toujours sur /rag/query et /rag/ingest, quel que soit le moteur actif. Le
choix se fait via la variable d'environnement RAG_BACKEND ("self_hosted" |
"api"), sans changement de code côté appelant.

NON monté dans app.py pour l'instant (voir RAG/README.md, §Statut).
"""

from __future__ import annotations

import logging

from fastapi import APIRouter, Depends, HTTPException

from auth import UserTokenData, verify_token
from RAG.shared.config import RAG_BACKEND
from RAG.shared.contracts import IngestRequest, IngestResult, RAGAnswer, RAGQueryRequest

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


@rag_router.post("/ingest", response_model=IngestResult)
async def ingest(request: IngestRequest, current_user: UserTokenData = Depends(verify_token)):
    process_document, index_chunks, _ = _active_backend()
    try:
        chunks, source_type, warnings = process_document(request)
        index_chunks(chunks)
        return IngestResult(doc_id=request.doc_id, chunks_indexed=len(chunks), source_type=source_type, warnings=warnings)
    except Exception as e:
        logger.exception(f"[RAG] Échec d'ingestion (backend={RAG_BACKEND})")
        raise HTTPException(status_code=500, detail=str(e))


@rag_router.post("/query", response_model=RAGAnswer)
async def query(request: RAGQueryRequest, current_user: UserTokenData = Depends(verify_token)):
    _, _, run_query = _active_backend()
    try:
        return run_query(request)
    except Exception as e:
        logger.exception(f"[RAG] Échec de requête (backend={RAG_BACKEND})")
        raise HTTPException(status_code=500, detail=str(e))


@rag_router.get("/health")
async def health():
    return {"status": "ok", "active_backend": RAG_BACKEND}
