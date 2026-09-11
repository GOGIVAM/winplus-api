"""
Configuration du moteur self_hosted — noms de modèles HuggingFace (famille
Qwen3 pour la cohérence écosystémique embedding/reranker/LLM, Phase 2 §2.2),
device et budget mémoire.

Tout est surchargeable par variable d'environnement pour permettre de faire
tourner exactement le même code sur l'EC2 CPU actuel (lent, pour valider la
logique) puis sur une instance GPU plus tard (voir DEPLOYMENT.md).
"""

import os

import torch

# ── Modèles (famille Qwen3, cohérence écosystémique) ────────────────────────
EMBEDDING_MODEL_ID = os.getenv("RAG_SH_EMBEDDING_MODEL", "Qwen/Qwen3-Embedding-8B")
RERANKER_MODEL_ID = os.getenv("RAG_SH_RERANKER_MODEL", "Qwen/Qwen3-Reranker-8B")
LLM_SIMPLE_MODEL_ID = os.getenv("RAG_SH_LLM_SIMPLE_MODEL", "Qwen/Qwen3-14B")
LLM_COMPLEX_MODEL_ID = os.getenv("RAG_SH_LLM_COMPLEX_MODEL", "Qwen/Qwen3-30B-A3B")
VERIFIER_MODEL_ID = os.getenv("RAG_SH_VERIFIER_MODEL", "deepseek-ai/DeepSeek-R1-Distill-Qwen-14B")
OCR_VLM_MODEL_ID = os.getenv("RAG_SH_OCR_MODEL", "zai-org/GLM-OCR")
TABLE_DETECTION_MODEL_ID = os.getenv("RAG_SH_TABLE_DETECTION_MODEL", "microsoft/table-transformer-detection")
TABLE_STRUCTURE_MODEL_ID = os.getenv(
    "RAG_SH_TABLE_STRUCTURE_MODEL", "microsoft/table-transformer-structure-recognition"
)
WHISPER_MODEL_ID = os.getenv("RAG_SH_WHISPER_MODEL", "large-v3")

# ── Device ───────────────────────────────────────────────────────────────
DEVICE = os.getenv("RAG_SH_DEVICE") or ("cuda" if torch.cuda.is_available() else "cpu")
IS_GPU = DEVICE.startswith("cuda")

# ── Quantization (bitsandbytes, reste dans l'écosystème transformers/torch —
# voir la validation actée avec l'équipe : ce n'est pas "une autre
# librairie" au sens d'un moteur d'inférence concurrent comme Ollama/vLLM).
QUANTIZE_4BIT = os.getenv("RAG_SH_QUANTIZE_4BIT", "true").lower() == "true" and IS_GPU

# ── VRAM (contrainte C3 du référentiel : budget cible ≤ 24 Go en Q4) ────────
# Si le GPU disponible ne peut pas tenir Qwen3-30B-A3B en plus du reste
# (embedding + reranker + OCR), le routeur de complexité bascule
# automatiquement sur Qwen3-14B (Phase 3, §3.2).
MAX_VRAM_GB_FOR_COMPLEX_MODEL = float(os.getenv("RAG_SH_MAX_VRAM_GB_COMPLEX", "24"))

# ── Collection Qdrant dédiée (dimensions Qwen3-Embedding-8B = 3072) ────────
QDRANT_COLLECTION = os.getenv("RAG_SH_COLLECTION", "winplus_self_hosted")
EMBEDDING_DIM = int(os.getenv("RAG_SH_EMBEDDING_DIM", "3072"))

# ── Lexique de correction post-OCR (Phase 1, §1.5) ─────────────────────────
LEXICON_PATH = os.getenv("RAG_SH_LEXICON_PATH", "./RAG/self_hosted/lexicon_winplus.txt")
LEXICON_MAX_EDIT_DISTANCE_RATIO = float(os.getenv("RAG_SH_LEXICON_MAX_EDIT_RATIO", "0.25"))
