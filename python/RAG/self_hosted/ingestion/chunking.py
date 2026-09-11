"""Adapte le chunking partagé (RAG/shared/chunking.py) au tokenizer Qwen3."""

from __future__ import annotations

from typing import List

from RAG.self_hosted import config
from RAG.self_hosted.models.loader import get_embedding_model
from RAG.shared.chunking import ChunkPair, fixed_window_chunk_pairs, has_structural_markers, semantic_chunk_pairs


def _count_tokens(text: str) -> int:
    tokenizer, _ = get_embedding_model()
    return len(tokenizer.encode(text, add_special_tokens=False))


def chunk_text(text: str, page: int | None = None) -> List[ChunkPair]:
    if has_structural_markers(text):
        return semantic_chunk_pairs(
            text, _count_tokens, config.CHUNK_SHORT_TOKENS, config.CHUNK_LONG_TOKENS, page=page
        )
    return fixed_window_chunk_pairs(
        text,
        _count_tokens,
        config.CHUNK_SHORT_TOKENS,
        config.CHUNK_LONG_TOKENS,
        config.CHUNK_OVERLAP_TOKENS,
        page=page,
    )
