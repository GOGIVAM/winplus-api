"""Reranking via Cohere Rerank v3.5 (même fournisseur que l'embedding)."""

from __future__ import annotations

from typing import List, Tuple

import cohere

from RAG.api import config

_client: cohere.ClientV2 | None = None


def _get_client() -> cohere.ClientV2:
    global _client
    if _client is None:
        _client = cohere.ClientV2(api_key=config.COHERE_API_KEY)
    return _client


def rerank(query: str, passages: List[str], top_k: int = 5) -> List[Tuple[int, float]]:
    if not passages:
        return []
    client = _get_client()
    response = client.rerank(model=config.COHERE_RERANK_MODEL, query=query, documents=passages, top_n=top_k)
    return [(r.index, r.relevance_score) for r in response.results]
