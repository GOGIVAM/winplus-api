"""Phase 2 — vectorisation et indexation des chunks produits en Phase 1."""

from __future__ import annotations

import logging
from typing import List

from RAG.self_hosted import config
from RAG.self_hosted.indexing.embedding import embed_texts
from RAG.shared.bm25_index import BM25Registry
from RAG.shared.contracts import Chunk
from RAG.shared.vector_store import ensure_collection, mark_superseded, upsert_chunks

logger = logging.getLogger(__name__)


def index_chunks(chunks: List[Chunk], previous_doc_id: str | None = None) -> None:
    if not chunks:
        return

    ensure_collection(config.QDRANT_COLLECTION, config.EMBEDDING_DIM)

    if previous_doc_id:
        mark_superseded(config.QDRANT_COLLECTION, previous_doc_id, chunks[0].metadata.doc_id)

    vectors = embed_texts([c.text for c in chunks])
    upsert_chunks(config.QDRANT_COLLECTION, chunks, vectors)

    bm25 = BM25Registry.get(config.QDRANT_COLLECTION)
    bm25.add_many([(c.chunk_id, c.text) for c in chunks])
    BM25Registry.persist(config.QDRANT_COLLECTION)

    logger.info(f"[RAG/self_hosted] {len(chunks)} chunks indexés dans '{config.QDRANT_COLLECTION}'.")
