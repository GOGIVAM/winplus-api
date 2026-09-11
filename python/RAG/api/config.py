"""Configuration du moteur api — clés et modèles des fournisseurs tiers,
choisis pour le rapport coût/performance vérifié (voir RAG/README.md)."""

import os

# ── Cohere (embedding + rerank, même fournisseur pour la cohérence) ────────
COHERE_API_KEY = os.getenv("COHERE_API_KEY", "")
COHERE_EMBED_MODEL = os.getenv("RAG_API_EMBED_MODEL", "embed-v4.0")
COHERE_RERANK_MODEL = os.getenv("RAG_API_RERANK_MODEL", "rerank-v3.5")
EMBEDDING_DIM = int(os.getenv("RAG_API_EMBEDDING_DIM", "1536"))

# ── Mistral OCR (documents scannés, tableaux) ───────────────────────────────
MISTRAL_API_KEY = os.getenv("MISTRAL_API_KEY", "")
MISTRAL_OCR_MODEL = os.getenv("RAG_API_OCR_MODEL", "mistral-ocr-latest")

# ── Groq Whisper (transcription vidéo) ──────────────────────────────────────
GROQ_API_KEY = os.getenv("GROQ_API_KEY", "")
GROQ_WHISPER_MODEL = os.getenv("RAG_API_WHISPER_MODEL", "whisper-large-v3-turbo")

# ── Vision (description d'images/schémas embarqués) ─────────────────────────
# Gemini retenu plutôt que GPT-4o-mini : ~3-4x moins cher par image (moins
# de tokens consommés par image à tarif par token comparable), vérifié en
# ligne (voir RAG/README.md).
GEMINI_API_KEY = os.getenv("GEMINI_API_KEY", "")
VISION_MODEL = os.getenv("RAG_API_VISION_MODEL", "gemini-2.5-flash")

# ── DeepSeek (génération — réutilise services/deepseek_client.py) ──────────
# Pas de config séparée : le client existant est réutilisé tel quel.

QDRANT_COLLECTION = os.getenv("RAG_API_COLLECTION", "winplus_api")
