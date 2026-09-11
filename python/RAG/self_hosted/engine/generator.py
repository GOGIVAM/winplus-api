"""
Génération de la réponse (Phase 3, §3.8) — ancrage strict au corpus fourni,
routage dual-modèle SIMPLE/COMPLEXE (Phase 3, §3.2).
"""

from __future__ import annotations

from typing import List

from RAG.self_hosted.engine.llm_utils import generate
from RAG.self_hosted.models.loader import get_llm_complex, get_llm_simple

# Prompt système non modifiable par l'appelant — équivalent du prompt
# d'ancrage strict du référentiel (Phase 3, §3.8), généralisé au contexte
# WinPlus (pas de RBAC "habilitation", remplacé par un message neutre).
SYSTEM_PROMPT_TEMPLATE = (
    "Tu es l'assistant documentaire WinPlus. Tu réponds exclusivement à "
    "partir des passages fournis ci-dessous. Chaque affirmation de ta "
    "réponse doit être ancrée dans un passage source identifié, cité par "
    "son numéro [1], [2], etc. Si l'information nécessaire est absente des "
    "passages fournis, réponds explicitement : \"Cette information n'est "
    "pas disponible dans le contenu accessible.\" Tu ne complètes jamais "
    "une réponse par des connaissances générales extérieures aux passages "
    "fournis.\n\nPassages disponibles :\n{context}"
)


def _format_context(passages: List[str]) -> str:
    return "\n\n".join(f"[{i + 1}] {p}" for i, p in enumerate(passages))


def generate_answer(question: str, passages: List[str], complexity: str) -> str:
    system = SYSTEM_PROMPT_TEMPLATE.format(context=_format_context(passages))

    if complexity == "complex":
        tokenizer, model = get_llm_complex()
        return generate(tokenizer, model, system, question, max_new_tokens=1000, thinking=True)

    tokenizer, model = get_llm_simple()
    return generate(tokenizer, model, system, question, max_new_tokens=600, thinking=False)


def reformulate_query(question: str, reason: str = "contexte insuffisant") -> str:
    """Reformulation de requête pour la boucle Self-RAG (Phase 3, §3.9)."""
    tokenizer, model = get_llm_simple()
    system = (
        "La recherche documentaire pour la question suivante n'a pas donné "
        f"un contexte suffisant ({reason}). Reformule la question avec des "
        "synonymes et un vocabulaire alternatif pour améliorer la "
        "recherche. Réponds uniquement avec la question reformulée."
    )
    return generate(tokenizer, model, system, question, max_new_tokens=100)
