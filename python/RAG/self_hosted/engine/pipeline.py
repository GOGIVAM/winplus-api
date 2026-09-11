"""
Orchestration complète de la Phase 3 — du texte de la requête à la réponse
validée. Assemble routage de complexité, HyDE, recherche hybride (dense +
BM25 + RRF), GraphRAG, reranking, boucle Self-RAG et génération.
"""

from __future__ import annotations

import logging
import time
from typing import Dict, List

from RAG.self_hosted import config
from RAG.self_hosted.engine.complexity_router import classify_complexity
from RAG.self_hosted.engine.generator import generate_answer, reformulate_query
from RAG.self_hosted.engine.hyde import generate_hypothetical_document
from RAG.self_hosted.engine.reranker import rerank
from RAG.self_hosted.indexing.embedding import embed_query
from RAG.self_hosted.validation.faithfulness import validate_answer
from RAG.shared.bm25_index import BM25Registry
from RAG.shared.contracts import Citation, RAGAnswer, RAGQueryRequest
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


def run_query(request: RAGQueryRequest) -> RAGAnswer:
    start = time.time()
    complexity = classify_complexity(request.question)

    query_vector = embed_query(request.question)
    candidates = _hybrid_retrieve(query_vector, request.question, request.filters, request.top_k)

    # HyDE si le meilleur score dense initial suggère un écart sémantique
    # (Phase 3, §3.4) — appliqué uniquement sur les requêtes SIMPLE, comme
    # décrit dans le référentiel.
    if complexity == "simple" and candidates:
        dense_check = search_dense(config.QDRANT_COLLECTION, query_vector, top_k=1, filters=request.filters)
        best_score = dense_check[0].score if dense_check else 0.0
        if best_score < config.HYDE_TRIGGER_COSINE:
            hypothetical = generate_hypothetical_document(request.question)
            hyde_vector = embed_query(hypothetical)
            candidates = _hybrid_retrieve(hyde_vector, request.question, request.filters, request.top_k)

    iterations = 0
    reranked: List[tuple] = []
    question_for_retrieval = request.question

    while iterations < config.SELF_RAG_MAX_ITERATIONS:
        texts = [c["payload"]["text"] for c in candidates]
        if not texts:
            break

        reranked = rerank(request.question, texts, top_k=request.top_k)
        avg_score = sum(s for _, s in reranked) / len(reranked) if reranked else 0.0

        if avg_score >= config.RERANK_CONFIDENCE_THRESHOLD or iterations == config.SELF_RAG_MAX_ITERATIONS - 1:
            break

        # Boucle Self-RAG (Phase 3, §3.9) : le contexte est jugé insuffisant,
        # on reformule la requête et on relance la récupération.
        question_for_retrieval = reformulate_query(request.question)
        new_vector = embed_query(question_for_retrieval)
        candidates = _hybrid_retrieve(new_vector, question_for_retrieval, request.filters, request.top_k)
        iterations += 1

    if not reranked:
        return RAGAnswer(
            answer=config.REFUSAL_MESSAGE,
            refused=True,
            backend="self_hosted",
            complexity=complexity,
            latency_ms=int((time.time() - start) * 1000),
        )

    selected = [candidates[i] for i, _ in reranked]
    passages = [c["payload"]["text"] for c in selected]

    answer_text = generate_answer(request.question, passages, complexity)

    validation = validate_answer(request.question, answer_text, passages, critical=(complexity == "complex"))

    citations = [
        Citation(
            doc_id=c["payload"]["doc_id"],
            title=c["payload"]["title"],
            page=c["payload"].get("page"),
            section=c["payload"].get("section"),
            chunk_id=c["id"],
            score=score,
        )
        for c, (_, score) in zip(selected, reranked)
    ]

    if not validation.passed:
        return RAGAnswer(
            answer=config.REFUSAL_MESSAGE,
            citations=citations,
            refused=True,
            faithfulness=validation.faithfulness,
            answer_relevancy=validation.answer_relevancy,
            backend="self_hosted",
            complexity=complexity,
            latency_ms=int((time.time() - start) * 1000),
        )

    return RAGAnswer(
        answer=answer_text,
        citations=citations,
        refused=False,
        faithfulness=validation.faithfulness,
        answer_relevancy=validation.answer_relevancy,
        backend="self_hosted",
        complexity=complexity,
        latency_ms=int((time.time() - start) * 1000),
    )
