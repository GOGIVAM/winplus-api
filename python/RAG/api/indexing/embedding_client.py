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


# Cohere refuse plus de 96 textes par appel (« total number of texts must be at
# most 96 ») : un livre de plusieurs centaines de pages échouait entièrement.
MAX_TEXTS_PER_CALL = 96


def embed_texts(texts: List[str], input_type: str = "search_document") -> List[List[float]]:
    client = _get_client()
    vectors: List[List[float]] = []
    for start in range(0, len(texts), MAX_TEXTS_PER_CALL):
        response = client.embed(
            texts=texts[start:start + MAX_TEXTS_PER_CALL],
            model=config.COHERE_EMBED_MODEL,
            input_type=input_type,
            embedding_types=["float"],
        )
        vectors.extend(response.embeddings.float)
    return vectors


def embed_query(query: str) -> List[float]:
    return embed_texts([query], input_type="search_query")[0]
