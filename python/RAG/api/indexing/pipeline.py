"""Vectorisation et indexation côté api — même Qdrant que self_hosted,
collection distincte (dimensions Cohere embed-v4 ≠ Qwen3-Embedding-8B)."""

from __future__ import annotations

import logging
from typing import List

from RAG.api import config
from RAG.api.indexing.embedding_client import embed_texts
from RAG.shared.bm25_index import BM25Registry
from RAG.shared.contracts import Chunk
from RAG.shared.relevance_scoring import compute_relevance_score, resolve_topic_label
from RAG.shared.vector_store import ensure_collection, mark_superseded, upsert_chunks

logger = logging.getLogger(__name__)


def _scope_filters(metadata) -> dict:
    filters: dict = {"status": "active"}
    if metadata.subject_id is not None:
        filters["subject_id"] = metadata.subject_id
    if metadata.course_id is not None:
        filters["course_id"] = metadata.course_id
    if metadata.owner_user_id is not None:
        filters["owner_user_id"] = metadata.owner_user_id
    return filters


def index_chunks(chunks: List[Chunk], previous_doc_id: str | None = None) -> List[str]:
    """Retourne d'éventuels avertissements de scoring (ex: adéquation au
    sujet faible) — à fusionner par l'appelant avec les warnings
    d'ingestion (voir RAG/router.py::_run_ingestion_job)."""
    if not chunks:
        return []

    ensure_collection(config.QDRANT_COLLECTION, config.EMBEDDING_DIM)

    if previous_doc_id:
        mark_superseded(config.QDRANT_COLLECTION, previous_doc_id, chunks[0].metadata.doc_id)

    # Score de pertinence composite (topo validé) — calculé une fois par
    # document (échantillon des premiers chunks), appliqué à tous ses
    # chunks. Voir RAG/shared/relevance_scoring.py pour le détail.
    first_meta = chunks[0].metadata
    topic_label = resolve_topic_label(first_meta.subject_id, first_meta.category)
    score, detail = compute_relevance_score(
        chunks_text=[c.text for c in chunks],
        embed_fn=embed_texts,
        collection=config.QDRANT_COLLECTION,
        filters=_scope_filters(first_meta),
        topic_label=topic_label,
    )
    for c in chunks:
        c.metadata.relevance_score = score
    scoring_warnings: List[str] = []
    if detail.get("topic_fit") is not None and detail["topic_fit"] < 0.3:
        msg = (
            f"Adéquation au sujet faible ({detail['topic_fit']:.2f}) — "
            f"possible erreur de classement (sujet déclaré : {topic_label!r})"
        )
        scoring_warnings.append(msg)
        logger.warning(f"[RAG/api] doc_id={first_meta.doc_id} : {msg}")

    vectors = embed_texts([c.text for c in chunks])
    upsert_chunks(config.QDRANT_COLLECTION, chunks, vectors)

    bm25 = BM25Registry.get(config.QDRANT_COLLECTION)
    bm25.add_many([(c.chunk_id, c.text) for c in chunks])
    BM25Registry.persist(config.QDRANT_COLLECTION)

    logger.info(f"[RAG/api] {len(chunks)} chunks indexés dans '{config.QDRANT_COLLECTION}' (relevance_score={score}).")
    return scoring_warnings
