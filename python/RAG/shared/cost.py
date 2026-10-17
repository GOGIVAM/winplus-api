"""
Coût d'une recherche dans la base de connaissance, en tokens équivalents
(lot 9.7b, décision 18.21 : le coût réel de la recherche, embedding et
reranking, est imputé au budget de tokens WinAI de l'utilisateur).

Conversion (étude 9.7b section 0.3) : le budget WinAI est exprimé en tokens
du modèle de chat, à environ 0,17 $ par million (80 % entrée, 20 % sortie).
  - Embedding Cohere : ~0,10 $ par million de tokens → compté à 0,6 token
    équivalent par token embarqué (~4 caractères par token).
  - Rerank Cohere : 2 $ pour 1 000 recherches, soit 0,002 $ par appel →
    0,002 / 0,17 × 1 000 000 ≈ 11 765 tokens équivalents par appel.
  - Reformulation Self-RAG : appel LLM court, compté au texte réel envoyé et
    reçu (~4 caractères par token) plus le prompt fixe de reformulation.
Les valeurs sont réglables par variables d'environnement sans changer le code.
"""

from __future__ import annotations

import os

CHARS_PER_TOKEN = 4
EMBEDDING_TOKEN_FACTOR = float(os.getenv("RAG_EMBEDDING_TOKEN_FACTOR", "0.6"))
RERANK_TOKEN_EQUIVALENT = int(os.getenv("RAG_RERANK_TOKEN_EQUIVALENT", "11765"))
REFORMULATION_PROMPT_TOKENS = int(os.getenv("RAG_REFORMULATION_PROMPT_TOKENS", "150"))

# Plafond d'itérations Self-RAG du mode « base de connaissance » du chat :
# une recherche, puis au plus une reformulation (lot 9.7b).
CHAT_MAX_ITERATIONS = 2


def _tokens(text: str) -> int:
    return max(1, len(text or "") // CHARS_PER_TOKEN)


def embedding_cost_tokens(text: str) -> int:
    return max(1, int(_tokens(text) * EMBEDDING_TOKEN_FACTOR))


def reformulation_cost_tokens(question: str, reformulated: str) -> int:
    return REFORMULATION_PROMPT_TOKENS + _tokens(question) + _tokens(reformulated)


def search_cost_estimate(question: str) -> int:
    """Estimation quand le moteur de recherche n'a pas remonté son coût
    (moteur local ou ancien) : un embedding et un appel de rerank."""
    return embedding_cost_tokens(question) + RERANK_TOKEN_EQUIVALENT
