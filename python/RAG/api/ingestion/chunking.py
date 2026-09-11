"""Adapte le chunking partagé (RAG/shared/chunking.py) à tiktoken (agnostique
du fournisseur — Cohere/DeepSeek n'exposent pas de tokenizer local)."""

from __future__ import annotations

from typing import List

import tiktoken

from RAG.api import config as api_config
from RAG.shared.chunking import ChunkPair, fixed_window_chunk_pairs, has_structural_markers, semantic_chunk_pairs
from RAG.shared.config import CHUNK_LONG_TOKENS, CHUNK_OVERLAP_TOKENS, CHUNK_SHORT_TOKENS

_encoding = tiktoken.get_encoding("cl100k_base")


def _count_tokens(text: str) -> int:
    return len(_encoding.encode(text))


def chunk_text(text: str, page: int | None = None) -> List[ChunkPair]:
    if has_structural_markers(text):
        return semantic_chunk_pairs(text, _count_tokens, CHUNK_SHORT_TOKENS, CHUNK_LONG_TOKENS, page=page)
    return fixed_window_chunk_pairs(
        text, _count_tokens, CHUNK_SHORT_TOKENS, CHUNK_LONG_TOKENS, CHUNK_OVERLAP_TOKENS, page=page
    )
