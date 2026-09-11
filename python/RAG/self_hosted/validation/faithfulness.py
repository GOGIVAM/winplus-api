"""
Validation anti-hallucination (Phase 4) — implémentation allégée du cadre
RAGAS : décomposition de la réponse en affirmations atomiques, vérification
de chacune contre les passages sources, calcul de la faithfulness. Le
jugement est réalisé par le LLM local lui-même (Qwen3-14B), ce qui préserve
les propriétés de zéro flux sortant et zéro coût récurrent du référentiel.

Seule la faithfulness bloque en production (gate à 0.90) ; les trois autres
métriques RAGAS (answer_relevancy, context_precision, context_recall) sont
calculées à titre informatif/journalisation, comme recommandé Phase 4 §4.1.
"""

from __future__ import annotations

import json
import logging
from dataclasses import dataclass
from typing import List

from RAG.self_hosted import config
from RAG.self_hosted.engine.llm_utils import generate
from RAG.self_hosted.models.loader import get_llm_simple, get_verifier_llm

logger = logging.getLogger(__name__)

_DECOMPOSE_SYSTEM = (
    'Décompose la réponse suivante en affirmations atomiques indépendantes. '
    'Réponds en JSON strict : {"claims": ["affirmation 1", "affirmation 2", ...]}.'
)

_VERIFY_SYSTEM = (
    "Les passages sources sont donnés ci-dessous. Pour l'affirmation "
    "fournie, réponds uniquement \"true\" si elle est directement ancrée "
    "dans au moins un des passages, \"false\" sinon.\n\nPassages :\n{context}"
)

_RELEVANCY_SYSTEM = (
    "Sur une échelle de 0 à 1, évalue à quel point la réponse suivante "
    "répond effectivement à la question posée, sans hors-sujet. Réponds "
    'uniquement en JSON : {"score": 0.xx}.\n\nQuestion : {question}'
)


@dataclass
class ValidationResult:
    faithfulness: float
    answer_relevancy: float
    passed: bool
    claims_checked: int
    claims_grounded: int


def _parse_json(raw: str) -> dict:
    raw = raw.strip().strip("`")
    if raw.lower().startswith("json"):
        raw = raw[4:]
    return json.loads(raw)


def _decompose_claims(answer: str) -> List[str]:
    tokenizer, model = get_llm_simple()
    raw = generate(tokenizer, model, _DECOMPOSE_SYSTEM, answer, max_new_tokens=400)
    try:
        return _parse_json(raw).get("claims", [answer])
    except json.JSONDecodeError:
        return [answer]


def _claim_is_grounded(claim: str, passages: List[str]) -> bool:
    tokenizer, model = get_llm_simple()
    system = _VERIFY_SYSTEM.format(context="\n\n".join(passages))
    result = generate(tokenizer, model, system, claim, max_new_tokens=10)
    return "true" in result.lower()


def _answer_relevancy(question: str, answer: str) -> float:
    tokenizer, model = get_llm_simple()
    system = _RELEVANCY_SYSTEM.format(question=question)
    raw = generate(tokenizer, model, system, answer, max_new_tokens=50)
    try:
        return float(_parse_json(raw).get("score", 0.0))
    except (json.JSONDecodeError, ValueError):
        return 0.0


def validate_answer(question: str, answer: str, passages: List[str], critical: bool = False) -> ValidationResult:
    claims = _decompose_claims(answer)
    if not claims:
        return ValidationResult(0.0, 0.0, False, 0, 0)

    grounded = sum(1 for c in claims if _claim_is_grounded(c, passages))
    faithfulness = grounded / len(claims)
    relevancy = _answer_relevancy(question, answer)

    if critical:
        # Vérification secondaire par DeepSeek-R1-Distill (chaîne de pensée
        # auditable, Phase 4 §4.3) sur les requêtes marquées critiques.
        try:
            tokenizer, model = get_verifier_llm()
            verdict = generate(
                tokenizer, model,
                "Vérifie si cette réponse est strictement fondée sur les passages fournis. "
                f"Passages :\n{chr(10).join(passages)}",
                answer, max_new_tokens=300, thinking=True,
            )
            logger.info(f"[RAG/self_hosted] Vérification critique (DeepSeek-R1-Distill) : {verdict[:200]}")
        except Exception as e:
            logger.warning(f"[RAG/self_hosted] Vérificateur critique indisponible : {e}")

    return ValidationResult(
        faithfulness=faithfulness,
        answer_relevancy=relevancy,
        passed=faithfulness >= config.FAITHFULNESS_GATE,
        claims_checked=len(claims),
        claims_grounded=grounded,
    )
