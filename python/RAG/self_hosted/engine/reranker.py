"""
Reranking par Qwen3-Reranker (Phase 3, §3.6) — cross-encoder qui évalue la
pertinence croisée (question, passage) par attention bidirectionnelle,
contrairement au bi-encoder utilisé pour la recherche vectorielle initiale.

Suit le format d'inférence de référence Qwen3-Reranker : le modèle est
interrogé pour produire "yes"/"no", et le score de pertinence est la
probabilité du token "yes".
"""

from __future__ import annotations

from typing import List, Tuple

import torch

from RAG.self_hosted import config
from RAG.self_hosted.models.loader import get_reranker_model

_INSTRUCTION = "Given a question, determine whether the passage is relevant to answering it."
_TEMPLATE = (
    "<Instruct>: {instruction}\n<Query>: {query}\n<Document>: {document}"
)


def rerank(query: str, passages: List[str], top_k: int = 5) -> List[Tuple[int, float]]:
    """Retourne [(index_dans_passages, score)] trié décroissant, limité à top_k."""
    tokenizer, model = get_reranker_model()

    yes_id = tokenizer.convert_tokens_to_ids("yes")
    no_id = tokenizer.convert_tokens_to_ids("no")

    scores: List[float] = []
    for doc in passages:
        prompt = _TEMPLATE.format(instruction=_INSTRUCTION, query=query, document=doc)
        messages = [{"role": "user", "content": prompt}]
        text = tokenizer.apply_chat_template(messages, tokenize=False, add_generation_prompt=True)
        inputs = tokenizer(text, return_tensors="pt", truncation=True, max_length=4096).to(config.DEVICE)

        with torch.no_grad():
            logits = model(**inputs).logits[0, -1, :]

        pair_logits = torch.stack([logits[no_id], logits[yes_id]])
        probs = torch.softmax(pair_logits, dim=0)
        scores.append(float(probs[1]))

    ranked = sorted(enumerate(scores), key=lambda x: x[1], reverse=True)
    return ranked[:top_k]
