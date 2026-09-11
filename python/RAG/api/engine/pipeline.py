"""Orchestration complète d'une requête côté api — recherche hybride
(Cohere embed + BM25 + RRF), reranking Cohere, génération DeepSeek,
validation anti-hallucination."""

from __future__ import annotations

import logging
import time
from typing import Dict, List

from RAG.api import config
from RAG.api.engine.generator_client import generate_answer, reformulate_query
from RAG.api.engine.reranker_client import rerank
from RAG.api.indexing.embedding_client import embed_query
from RAG.api.validation.faithfulness import validate_answer
from RAG.shared.bm25_index import BM25Registry
from RAG.shared.config import REFUSAL_MESSAGE, RERANK_CONFIDENCE_THRESHOLD, SELF_RAG_MAX_ITERATIONS
from RAG.shared.contracts import Citation, RAGAnswer, RAGQueryRequest, RetrievedContext
from RAG.shared.rrf import reciprocal_rank_fusion
from RAG.shared.vector_store import get_by_ids, search_dense

logger = logging.getLogger(__name__)


def _hybrid_retrieve(query_vector: List[float], query_text: str, filters: Dict, top_k: int) -> List[dict]:
    dense_hits = search_dense(config.QDRANT_COLLECTION, query_vector, top_k=top_k * 2, filters=filters)
    dense_ids = [str(h.id) for h in dense_hits]

    bm25 = BM25Registry.get(config.QDRANT_COLLECTION)
    sparse_ids = bm25.search(query_text, top_k=top_k * 2)

    fused = reciprocal_rank_fusion([dense_ids, sparse_ids])
    top_ids = [chunk_id for chunk_id, _ in fused[: top_k * 2]]

    records = get_by_ids(config.QDRANT_COLLECTION, top_ids)
    by_id = {str(r.id): r for r in records}
    return [
        {"id": cid, "payload": by_id[cid].payload}
        for cid in top_ids
        if cid in by_id and by_id[cid].payload.get("status", "active") == "active"
    ]


def _retrieve_and_rerank(request: RAGQueryRequest) -> tuple[List[dict], List[tuple]]:
    """Recherche hybride + boucle Self-RAG (reformulation si le rerank est
    peu confiant), sans génération ni validation — factorisé pour être
    partagé entre run_query() (réponse autonome de ce module) et
    retrieve_passages() (utilisé par l'intégration chatbot WinAI)."""
    query_vector = embed_query(request.question)
    candidates = _hybrid_retrieve(query_vector, request.question, request.filters, request.top_k)

    iterations = 0
    reranked: List[tuple] = []

    while iterations < SELF_RAG_MAX_ITERATIONS:
        texts = [c["payload"]["text"] for c in candidates]
        if not texts:
            break

        reranked = rerank(request.question, texts, top_k=request.top_k)
        avg_score = sum(s for _, s in reranked) / len(reranked) if reranked else 0.0

        if avg_score >= RERANK_CONFIDENCE_THRESHOLD or iterations == SELF_RAG_MAX_ITERATIONS - 1:
            break

        reformulated = reformulate_query(request.question)
        new_vector = embed_query(reformulated)
        candidates = _hybrid_retrieve(new_vector, reformulated, request.filters, request.top_k)
        iterations += 1

    return candidates, reranked


def _to_citations(candidates: List[dict], reranked: List[tuple]) -> tuple[List[dict], List[str], List[Citation]]:
    selected = [candidates[i] for i, _ in reranked]
    passages = [c["payload"]["text"] for c in selected]
    citations = [
        Citation(
            doc_id=c["payload"]["doc_id"],
            title=c["payload"]["title"],
            page=c["payload"].get("page"),
            section=c["payload"].get("section"),
            chunk_id=c["id"],
            score=score,
            subject_id=c["payload"].get("subject_id"),
            course_id=c["payload"].get("course_id"),
        )
        for c, (_, score) in zip(selected, reranked)
    ]
    return selected, passages, citations


def retrieve_passages(request: RAGQueryRequest) -> RetrievedContext:
    """Point d'entrée dédié à l'intégration chatbot WinAI (voir
    services/rag_chat_bridge.py) : ne fait QUE récupérer et reranker les
    passages, sans appeler generate_answer() ni validate_answer() — la
    génération finale reste celle du prompt WinAI existant (persona,
    pédagogie, mémoire élève), RAG ne fait qu'apporter du contexte."""
    start = time.time()
    candidates, reranked = _retrieve_and_rerank(request)
    if not reranked:
        return RetrievedContext(refused=True, latency_ms=int((time.time() - start) * 1000))
    _, passages, citations = _to_citations(candidates, reranked)
    return RetrievedContext(
        passages=passages, citations=citations, refused=False, latency_ms=int((time.time() - start) * 1000)
    )


def run_query(request: RAGQueryRequest) -> RAGAnswer:
    start = time.time()

    candidates, reranked = _retrieve_and_rerank(request)

    if not reranked:
        return RAGAnswer(
            answer=REFUSAL_MESSAGE, refused=True, backend="api", latency_ms=int((time.time() - start) * 1000)
        )

    selected, passages, citations = _to_citations(candidates, reranked)

    answer_text = generate_answer(request.question, passages)
    validation = validate_answer(request.question, answer_text, passages)

    if not validation.passed:
        return RAGAnswer(
            answer=REFUSAL_MESSAGE,
            citations=citations,
            refused=True,
            faithfulness=validation.faithfulness,
            answer_relevancy=validation.answer_relevancy,
            backend="api",
            latency_ms=int((time.time() - start) * 1000),
        )

    return RAGAnswer(
        answer=answer_text,
        citations=citations,
        refused=False,
        faithfulness=validation.faithfulness,
        answer_relevancy=validation.answer_relevancy,
        backend="api",
        latency_ms=int((time.time() - start) * 1000),
    )
