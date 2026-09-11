"""
Validation anti-hallucination côté api — même méthode que self_hosted
(décomposition en affirmations atomiques + vérification contre les
passages), mais le juge LLM est DeepSeek plutôt qu'un modèle local.
"""

from __future__ import annotations

import json
import logging
from dataclasses import dataclass
from typing import List

from RAG.shared.config import FAITHFULNESS_GATE
from services.deepseek_client import get_deepseek_client

logger = logging.getLogger(__name__)


@dataclass
class ValidationResult:
    faithfulness: float
    answer_relevancy: float
    passed: bool


def _parse_json(raw: str) -> dict:
    raw = raw.strip().strip("`")
    if raw.lower().startswith("json"):
        raw = raw[4:]
    return json.loads(raw)


def _decompose_claims(answer: str) -> List[str]:
    client = get_deepseek_client()
    system = 'Décompose la réponse suivante en affirmations atomiques indépendantes. Réponds en JSON strict : {"claims": ["...", "..."]}.'
    result = client.chat(messages=[{"role": "user", "content": answer}], system_prompt=system, max_tokens=400)
    try:
        return _parse_json(result.get("content", "")).get("claims", [answer])
    except json.JSONDecodeError:
        return [answer]


def _claim_is_grounded(claim: str, passages: List[str]) -> bool:
    client = get_deepseek_client()
    system = (
        "Les passages sources sont donnés ci-dessous. Pour l'affirmation "
        "fournie, réponds uniquement \"true\" si elle est directement "
        f"ancrée dans au moins un des passages, \"false\" sinon.\n\nPassages :\n{chr(10).join(passages)}"
    )
    result = client.chat(messages=[{"role": "user", "content": claim}], system_prompt=system, max_tokens=10)
    return "true" in result.get("content", "").lower()


def _answer_relevancy(question: str, answer: str) -> float:
    client = get_deepseek_client()
    system = (
        "Sur une échelle de 0 à 1, évalue à quel point la réponse suivante "
        "répond effectivement à la question posée, sans hors-sujet. Réponds "
        f'uniquement en JSON : {{"score": 0.xx}}.\n\nQuestion : {question}'
    )
    result = client.chat(messages=[{"role": "user", "content": answer}], system_prompt=system, max_tokens=50)
    try:
        return float(_parse_json(result.get("content", "")).get("score", 0.0))
    except (json.JSONDecodeError, ValueError):
        return 0.0


def validate_answer(question: str, answer: str, passages: List[str]) -> ValidationResult:
    claims = _decompose_claims(answer)
    if not claims:
        return ValidationResult(0.0, 0.0, False)

    grounded = sum(1 for c in claims if _claim_is_grounded(c, passages))
    faithfulness = grounded / len(claims)
    relevancy = _answer_relevancy(question, answer)

    return ValidationResult(faithfulness=faithfulness, answer_relevancy=relevancy, passed=faithfulness >= FAITHFULNESS_GATE)
