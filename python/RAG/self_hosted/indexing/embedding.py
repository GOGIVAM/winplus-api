"""Vectorisation par Qwen3-Embedding-8B (Phase 2, §2.1)."""

from __future__ import annotations

from typing import List

import torch
import torch.nn.functional as F

from RAG.self_hosted import config
from RAG.self_hosted.models.loader import get_embedding_model


def _mean_pool(last_hidden_state: torch.Tensor, attention_mask: torch.Tensor) -> torch.Tensor:
    mask = attention_mask.unsqueeze(-1).expand(last_hidden_state.size()).float()
    summed = torch.sum(last_hidden_state * mask, dim=1)
    counts = torch.clamp(mask.sum(dim=1), min=1e-9)
    return summed / counts


def embed_texts(texts: List[str], batch_size: int = 16) -> List[List[float]]:
    tokenizer, model = get_embedding_model()
    vectors: List[List[float]] = []

    for i in range(0, len(texts), batch_size):
        batch = texts[i : i + batch_size]
        inputs = tokenizer(batch, padding=True, truncation=True, max_length=8192, return_tensors="pt").to(
            config.DEVICE
        )
        with torch.no_grad():
            outputs = model(**inputs)
        pooled = _mean_pool(outputs.last_hidden_state, inputs["attention_mask"])
        normalized = F.normalize(pooled, p=2, dim=1)
        vectors.extend(normalized.cpu().tolist())

    return vectors


def embed_query(query: str) -> List[float]:
    return embed_texts([query])[0]
