"""
Points d'entrée FastAPI du moteur api — router de test direct, synchrone
(pratique pour observer l'IngestResult complet immédiatement sur un petit
fichier). Le point d'entrée réel, monté dans app.py et utilisé par
l'appelant, est RAG/router.py — lui traite l'ingestion en arrière-plan pour
ne jamais bloquer sur un gros fichier (voir RAG/router.py, RAG/README.md).
"""

from __future__ import annotations

import logging

from fastapi import APIRouter, Depends, HTTPException

from auth import UserTokenData, verify_token
from RAG.api.engine.pipeline import run_query
from RAG.api.indexing.pipeline import index_chunks
from RAG.api.ingestion.pipeline import process_document
from RAG.shared.contracts import IngestRequest, IngestResult, RAGAnswer, RAGQueryRequest

logger = logging.getLogger(__name__)

api_router = APIRouter(prefix="/rag/api", tags=["RAG - api"])


@api_router.post("/ingest", response_model=IngestResult)
async def ingest(request: IngestRequest, current_user: UserTokenData = Depends(verify_token)):
    from RAG.shared.file_resolver import resolve_local_path

    try:
        with resolve_local_path(request) as local_path:
            resolved_request = request.model_copy(update={"file_path": local_path})
            chunks, source_type, warnings = process_document(resolved_request)
        warnings = warnings + index_chunks(chunks)
        return IngestResult(doc_id=request.doc_id, chunks_indexed=len(chunks), source_type=source_type, warnings=warnings)
    except Exception as e:
        logger.exception("[RAG/api] Échec d'ingestion")
        raise HTTPException(status_code=500, detail=str(e))


@api_router.post("/query", response_model=RAGAnswer)
async def query(request: RAGQueryRequest, current_user: UserTokenData = Depends(verify_token)):
    try:
        return run_query(request)
    except Exception as e:
        logger.exception("[RAG/api] Échec de requête")
        raise HTTPException(status_code=500, detail=str(e))


@api_router.get("/health")
async def health():
    return {"status": "ok"}
