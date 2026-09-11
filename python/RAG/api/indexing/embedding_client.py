"""Vectorisation via Cohere embed-v4 (multilingue, fort en français)."""

from __future__ import annotations

from typing import List

import cohere

from RAG.api import config

_client: cohere.ClientV2 | None = None


def _get_client() -> cohere.ClientV2:
    global _client
    if _client is None:
        _client = cohere.ClientV2(api_key=config.COHERE_API_KEY)
    return _client


def embed_texts(texts: List[str], input_type: str = "search_document") -> List[List[float]]:
    client = _get_client()
    response = client.embed(
        texts=texts,
        model=config.COHERE_EMBED_MODEL,
        input_type=input_type,
        embedding_types=["float"],
    )
    return response.embeddings.float


def embed_query(query: str) -> List[float]:
    return embed_texts([query], input_type="search_query")[0]
