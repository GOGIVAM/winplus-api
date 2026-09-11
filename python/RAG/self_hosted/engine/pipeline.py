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
from RAG.self_hosted.engine import graphrag
from RAG.self_hosted.engine.complexity_router import classify_complexity
from RAG.self_hosted.engine.generator import generate_answer, reformulate_query
from RAG.self_hosted.engine.hyde import generate_hypothetical_document
from RAG.self_hosted.engine.reranker import rerank
from RAG.self_hosted.indexing.embedding import embed_query
from RAG.self_hosted.validation.faithfulness import validate_answer
from RAG.shared.bm25_index import BM25Registry
from RAG.shared.contracts import Citation, RAGAnswer, RAGQueryRequest, RetrievedContext
from RAG.shared.graph_registry import GraphRegistry
from RAG.shared.rrf import reciprocal_rank_fusion
from RAG.shared.vector_store import get_by_ids, scroll_by_filter, search_dense

# Approximation d'entités normatives dans la requête — même heuristique que
# le routeur de complexité (suites de mots capitalisés ou sigles).
import re as _re

_ENTITY_RE = _re.compile(r"\b([A-ZÀ-Ý][a-zà-ÿ]+(?:\s+[A-ZÀ-Ý][a-zà-ÿ]+)*|[A-Z]{2,})\b")

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


def _graphrag_augment(question: str, filters: Dict, max_docs: int = 3) -> List[dict]:
    """Complément de contexte structuré pour les requêtes COMPLEXE (Phase 3,
    §3.7) — parcours BFS borné à 2 sauts depuis les entités de la requête,
    puis récupération des chunks des documents connectés."""
    graph = GraphRegistry.get(config.QDRANT_COLLECTION)
    entities = list(set(_ENTITY_RE.findall(question)))
    if not entities or graph.number_of_nodes() == 0:
        return []

    related = graphrag.bfs_related_docs(graph, entities, max_hops=2)
    doc_ids = list({doc_id for _, doc_id in related})[:max_docs]
    if not doc_ids:
        return []

    graph_filters = {**filters, "doc_id": doc_ids}
    records = scroll_by_filter(config.QDRANT_COLLECTION, graph_filters, limit=max_docs * 3)
    return [
        {"id": str(r.id), "payload": r.payload}
        for r in records
        if r.payload.get("status", "active") == "active"
    ]


def _retrieve_and_rerank(request: RAGQueryRequest) -> tuple[List[dict], List[tuple], str]:
    """Routage de complexité + HyDE + GraphRAG + recherche hybride + boucle
    Self-RAG, sans génération ni validation — factorisé pour être partagé
    entre run_query() (réponse autonome de ce module) et retrieve_passages()
    (utilisé par l'intégration chatbot WinAI)."""
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

    if complexity == "complex":
        candidates = candidates + _graphrag_augment(request.question, request.filters)

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

    return candidates, reranked, complexity


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
    candidates, reranked, complexity = _retrieve_and_rerank(request)
    if not reranked:
        return RetrievedContext(refused=True, complexity=complexity, latency_ms=int((time.time() - start) * 1000))
    _, passages, citations = _to_citations(candidates, reranked)
    return RetrievedContext(
        passages=passages,
        citations=citations,
        refused=False,
        complexity=complexity,
        latency_ms=int((time.time() - start) * 1000),
    )


def run_query(request: RAGQueryRequest) -> RAGAnswer:
    start = time.time()

    candidates, reranked, complexity = _retrieve_and_rerank(request)

    if not reranked:
        return RAGAnswer(
            answer=config.REFUSAL_MESSAGE,
            refused=True,
            backend="self_hosted",
            complexity=complexity,
            latency_ms=int((time.time() - start) * 1000),
        )

    selected, passages, citations = _to_citations(candidates, reranked)

    answer_text = generate_answer(request.question, passages, complexity)

    validation = validate_answer(request.question, answer_text, passages, critical=(complexity == "complex"))

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
