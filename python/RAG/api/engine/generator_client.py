"""
Génération via DeepSeek — réutilise le client existant
(services/deepseek_client.py) plutôt que d'en dupliquer un, avec le même
prompt d'ancrage strict que le moteur self_hosted.
"""

from __future__ import annotations

from typing import List

from services.deepseek_client import get_deepseek_client

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


def generate_answer(question: str, passages: List[str]) -> str:
    client = get_deepseek_client()
    system = SYSTEM_PROMPT_TEMPLATE.format(context=_format_context(passages))
    result = client.chat(
        messages=[{"role": "user", "content": question}],
        system_prompt=system,
        temperature=0.2,
        max_tokens=1000,
    )
    return result.get("content", "")


def reformulate_query(question: str) -> str:
    client = get_deepseek_client()
    system = (
        "La recherche documentaire pour la question suivante n'a pas donné "
        "un contexte suffisant. Reformule la question avec des synonymes et "
        "un vocabulaire alternatif pour améliorer la recherche. Réponds "
        "uniquement avec la question reformulée."
    )
    result = client.chat(messages=[{"role": "user", "content": question}], system_prompt=system, max_tokens=100)
    return result.get("content", question)
