"""
HyDE — Hypothetical Document Embeddings (Phase 3, §3.4). Comble l'écart
sémantique entre une question en langage courant et le registre du corpus :
on génère un court passage hypothétique dans le style du corpus, et c'est
son vecteur — pas celui de la question brute — qui sert de requête.
"""

from __future__ import annotations

from RAG.self_hosted.engine.llm_utils import generate
from RAG.self_hosted.models.loader import get_llm_simple

_SYSTEM = (
    "Tu rédiges un court paragraphe hypothétique (3-4 phrases) qui pourrait "
    "figurer dans un document pédagogique répondant à la question posée. "
    "Emploie un registre factuel et le vocabulaire technique probable du "
    "domaine. Le contenu peut être approximatif : seul le style et le "
    "vocabulaire comptent, pas l'exactitude."
)


def generate_hypothetical_document(question: str) -> str:
    tokenizer, model = get_llm_simple()
    return generate(tokenizer, model, _SYSTEM, question, max_new_tokens=200)
