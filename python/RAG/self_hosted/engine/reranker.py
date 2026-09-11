"""
Reranking par Qwen3-Reranker (Phase 3, §3.6) — cross-encoder qui évalue la
pertinence croisée (question, passage) par attention bidirectionnelle,
contrairement au bi-encoder utilisé pour la recherche vectorielle initiale.

Gabarit exact de la fiche modèle officielle (Qwen/Qwen3-Reranker-8B) : préfixe
système forçant le format yes/no, suffixe `<think>\n\n</think>\n\n` qui
désactive le mode raisonnement pour cette tâche de classification — un
`apply_chat_template` générique produit un prompt légèrement différent de
celui sur lequel le modèle a été entraîné et dégraderait la qualité du score.
"""

from __future__ import annotations

from typing import List, Tuple

import torch

from RAG.self_hosted import config
from RAG.self_hosted.models.loader import get_reranker_model

_DEFAULT_INSTRUCTION = "Given a question, determine whether the passage is relevant to answering it."

_PREFIX = (
    "<|im_start|>system\nJudge whether the Document meets the requirements "
    'based on the Query and the Instruct provided. Note that the answer can '
    'only be "yes" or "no".<|im_end|>\n<|im_start|>user\n'
)
_SUFFIX = "<|im_end|>\n<|im_start|>assistant\n<think>\n\n</think>\n\n"
_TEMPLATE = "<Instruct>: {instruction}\n<Query>: {query}\n<Document>: {document}"


def rerank(query: str, passages: List[str], top_k: int = 5, instruction: str = _DEFAULT_INSTRUCTION) -> List[Tuple[int, float]]:
    """Retourne [(index_dans_passages, score)] trié décroissant, limité à top_k."""
    tokenizer, model = get_reranker_model()

    token_true_id = tokenizer.convert_tokens_to_ids("yes")
    token_false_id = tokenizer.convert_tokens_to_ids("no")

    scores: List[float] = []
    for doc in passages:
        body = _TEMPLATE.format(instruction=instruction, query=query, document=doc)
        full_prompt = _PREFIX + body + _SUFFIX
        inputs = tokenizer(full_prompt, return_tensors="pt", truncation=True, max_length=4096).to(config.DEVICE)

        with torch.no_grad():
            logits = model(**inputs).logits[0, -1, :]

        pair_logits = torch.stack([logits[token_false_id], logits[token_true_id]])
        probs = torch.softmax(pair_logits, dim=0)
        scores.append(float(probs[1]))

    ranked = sorted(enumerate(scores), key=lambda x: x[1], reverse=True)
    return ranked[:top_k]
