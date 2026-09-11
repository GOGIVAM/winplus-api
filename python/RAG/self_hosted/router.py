"""
Points d'entrée FastAPI du moteur self_hosted — router de test direct,
synchrone (pratique pour observer l'IngestResult complet immédiatement sur
un petit fichier). Le point d'entrée réel, monté dans app.py et utilisé par
l'appelant, est RAG/router.py — lui traite l'ingestion en arrière-plan pour
ne jamais bloquer sur un gros fichier (voir RAG/router.py, RAG/README.md).
"""

from __future__ import annotations

import logging

from fastapi import APIRouter, Depends, HTTPException

from auth import UserTokenData, verify_token
from RAG.self_hosted.indexing.pipeline import index_chunks
from RAG.self_hosted.ingestion.pipeline import process_document
from RAG.self_hosted import config
from RAG.self_hosted.engine.pipeline import run_query
from RAG.shared.contracts import IngestRequest, IngestResult, RAGAnswer, RAGQueryRequest

logger = logging.getLogger(__name__)

self_hosted_router = APIRouter(prefix="/rag/self-hosted", tags=["RAG - self-hosted"])


@self_hosted_router.post("/ingest", response_model=IngestResult)
async def ingest(request: IngestRequest, current_user: UserTokenData = Depends(verify_token)):
    try:
        chunks, source_type, warnings = process_document(request)
        index_chunks(chunks)
        return IngestResult(doc_id=request.doc_id, chunks_indexed=len(chunks), source_type=source_type, warnings=warnings)
    except Exception as e:
        logger.exception("[RAG/self_hosted] Échec d'ingestion")
        raise HTTPException(status_code=500, detail=str(e))


@self_hosted_router.post("/query", response_model=RAGAnswer)
async def query(request: RAGQueryRequest, current_user: UserTokenData = Depends(verify_token)):
    try:
        return run_query(request)
    except Exception as e:
        logger.exception("[RAG/self_hosted] Échec de requête")
        raise HTTPException(status_code=500, detail=str(e))


@self_hosted_router.get("/health")
async def health():
    return {
        "status": "ok",
        "device": config.DEVICE,
        "quantized_4bit": config.QUANTIZE_4BIT,
        "collection": config.QDRANT_COLLECTION,
    }
