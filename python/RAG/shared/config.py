"""Configuration commune aux deux moteurs RAG."""

import os

# "self_hosted" ou "api" — bascule le moteur derrière l'unique endpoint
# /rag/query. Les deux modules restent indépendants ; ceci ne fait que
# décider lequel répond, sans que l'appelant ait à changer de code.
RAG_BACKEND = os.getenv("RAG_BACKEND", "api")

# Seuils de calibration (équivalents "Phase 0" du référentiel KALATI-RAG).
# Valeurs de départ raisonnables ; à recalibrer sur un golden dataset WinPlus
# une fois le module en usage réel (voir DEPLOYMENT.md).
FAITHFULNESS_GATE = float(os.getenv("RAG_FAITHFULNESS_GATE", "0.90"))
ANSWER_RELEVANCY_MIN = float(os.getenv("RAG_ANSWER_RELEVANCY_MIN", "0.85"))
CONTEXT_PRECISION_MIN = float(os.getenv("RAG_CONTEXT_PRECISION_MIN", "0.80"))
CONTEXT_RECALL_MIN = float(os.getenv("RAG_CONTEXT_RECALL_MIN", "0.80"))
RERANK_CONFIDENCE_THRESHOLD = float(os.getenv("RAG_RERANK_CONFIDENCE_THRESHOLD", "0.6"))
SELF_RAG_MAX_ITERATIONS = int(os.getenv("RAG_SELF_RAG_MAX_ITERATIONS", "3"))
HYDE_TRIGGER_COSINE = float(os.getenv("RAG_HYDE_TRIGGER_COSINE", "0.35"))

# Double granularité de chunking (Phase 1, parent-child chunking).
CHUNK_SHORT_TOKENS = int(os.getenv("RAG_CHUNK_SHORT_TOKENS", "128"))
CHUNK_LONG_TOKENS = int(os.getenv("RAG_CHUNK_LONG_TOKENS", "512"))
CHUNK_OVERLAP_TOKENS = int(os.getenv("RAG_CHUNK_OVERLAP_TOKENS", "20"))

REFUSAL_MESSAGE = (
    "Je ne dispose pas d'une réponse suffisamment documentée dans le corpus "
    "accessible pour répondre à cette question avec certitude."
)
