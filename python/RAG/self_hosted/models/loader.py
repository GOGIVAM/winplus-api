"""
Chargement centralisé des modèles — PyTorch/HuggingFace Transformers
exclusivement, aucun moteur d'inférence tiers (pas d'Ollama, pas de vLLM,
pas de llama.cpp/GGUF). La quantization passe par `bitsandbytes`, qui
s'intègre nativement dans `transformers` plutôt que de remplacer PyTorch.

Chaque modèle est chargé une seule fois (singleton module-level) : le coût
de chargement (plusieurs dizaines de secondes à quelques minutes selon le
modèle et le device) ne doit être payé qu'au démarrage du service, pas à
chaque requête.
"""

from __future__ import annotations

import logging
import threading
from typing import Optional

import torch
from transformers import (
    AutoModel,
    AutoModelForCausalLM,
    AutoModelForImageTextToText,
    AutoProcessor,
    AutoTokenizer,
    BitsAndBytesConfig,
)

from RAG.self_hosted import config

logger = logging.getLogger(__name__)

_lock = threading.Lock()
_registry: dict = {}


def _quant_config() -> Optional[BitsAndBytesConfig]:
    if not config.QUANTIZE_4BIT:
        return None
    return BitsAndBytesConfig(
        load_in_4bit=True,
        bnb_4bit_quant_type="nf4",
        bnb_4bit_compute_dtype=torch.bfloat16,
        bnb_4bit_use_double_quant=True,
    )


def _load_causal_lm(model_id: str):
    logger.info(f"[RAG/self_hosted] Chargement LLM {model_id} sur {config.DEVICE}...")
    tokenizer = AutoTokenizer.from_pretrained(model_id)
    kwargs = {"torch_dtype": torch.bfloat16 if config.IS_GPU else torch.float32}
    quant = _quant_config()
    if quant is not None:
        kwargs["quantization_config"] = quant
        kwargs["device_map"] = "auto"
    model = AutoModelForCausalLM.from_pretrained(model_id, **kwargs)
    if quant is None:
        model = model.to(config.DEVICE)
    model.eval()
    return tokenizer, model


def get_llm_simple():
    """Qwen3-14B — modèle nominal, mode non-thinking (Phase 3, §3.1)."""
    with _lock:
        if "llm_simple" not in _registry:
            _registry["llm_simple"] = _load_causal_lm(config.LLM_SIMPLE_MODEL_ID)
        return _registry["llm_simple"]


def get_llm_complex():
    """Qwen3-30B-A3B — mode thinking pour requêtes multi-hop (Phase 3, §3.1).
    Bascule sur le modèle simple si le GPU ne peut pas l'accueillir
    (contrainte C3, Phase 3 §3.2)."""
    if config.IS_GPU:
        free_gb = torch.cuda.mem_get_info()[0] / (1024**3)
        if free_gb < config.MAX_VRAM_GB_FOR_COMPLEX_MODEL * 0.7:
            logger.warning(
                "[RAG/self_hosted] VRAM insuffisante pour Qwen3-30B-A3B "
                f"({free_gb:.1f} Go libres) — bascule sur le modèle simple."
            )
            return get_llm_simple()
    with _lock:
        if "llm_complex" not in _registry:
            try:
                _registry["llm_complex"] = _load_causal_lm(config.LLM_COMPLEX_MODEL_ID)
            except Exception as e:
                logger.warning(f"[RAG/self_hosted] Échec chargement modèle complexe ({e}) — repli simple.")
                return get_llm_simple()
        return _registry["llm_complex"]


def get_verifier_llm():
    """DeepSeek-R1-Distill-Qwen-14B — vérificateur anti-hallucination sur
    requêtes critiques, raisonnement auditable via tokens <think> (Phase 4)."""
    with _lock:
        if "verifier" not in _registry:
            _registry["verifier"] = _load_causal_lm(config.VERIFIER_MODEL_ID)
        return _registry["verifier"]


def get_embedding_model():
    with _lock:
        if "embedding" not in _registry:
            logger.info(f"[RAG/self_hosted] Chargement embedding {config.EMBEDDING_MODEL_ID}...")
            tokenizer = AutoTokenizer.from_pretrained(config.EMBEDDING_MODEL_ID)
            model = AutoModel.from_pretrained(
                config.EMBEDDING_MODEL_ID,
                torch_dtype=torch.bfloat16 if config.IS_GPU else torch.float32,
            ).to(config.DEVICE)
            model.eval()
            _registry["embedding"] = (tokenizer, model)
        return _registry["embedding"]


def get_reranker_model():
    with _lock:
        if "reranker" not in _registry:
            logger.info(f"[RAG/self_hosted] Chargement reranker {config.RERANKER_MODEL_ID}...")
            tokenizer = AutoTokenizer.from_pretrained(config.RERANKER_MODEL_ID)
            model = AutoModelForCausalLM.from_pretrained(
                config.RERANKER_MODEL_ID,
                torch_dtype=torch.bfloat16 if config.IS_GPU else torch.float32,
            ).to(config.DEVICE)
            model.eval()
            _registry["reranker"] = (tokenizer, model)
        return _registry["reranker"]


def get_ocr_vlm():
    """GLM-OCR — moteur unique pour scans, tampons et images embarquées
    (remplace PaddleOCR-VL + GLM-OCR du référentiel original : premier sur
    OmniDocBench et nativement PyTorch/Transformers, cf. topo validé)."""
    with _lock:
        if "ocr" not in _registry:
            logger.info(f"[RAG/self_hosted] Chargement OCR-VLM {config.OCR_VLM_MODEL_ID}...")
            processor = AutoProcessor.from_pretrained(config.OCR_VLM_MODEL_ID)
            model = AutoModelForImageTextToText.from_pretrained(
                config.OCR_VLM_MODEL_ID,
                torch_dtype=torch.bfloat16 if config.IS_GPU else torch.float32,
            ).to(config.DEVICE)
            model.eval()
            _registry["ocr"] = (processor, model)
        return _registry["ocr"]


def get_table_detection_model():
    with _lock:
        if "table_detect" not in _registry:
            from transformers import DetrImageProcessor, TableTransformerForObjectDetection

            logger.info(f"[RAG/self_hosted] Chargement TATR detection {config.TABLE_DETECTION_MODEL_ID}...")
            processor = DetrImageProcessor.from_pretrained(config.TABLE_DETECTION_MODEL_ID)
            model = TableTransformerForObjectDetection.from_pretrained(config.TABLE_DETECTION_MODEL_ID).to(
                config.DEVICE
            )
            model.eval()
            _registry["table_detect"] = (processor, model)
        return _registry["table_detect"]


def get_table_structure_model():
    with _lock:
        if "table_structure" not in _registry:
            from transformers import DetrImageProcessor, TableTransformerForObjectDetection

            logger.info(f"[RAG/self_hosted] Chargement TATR structure {config.TABLE_STRUCTURE_MODEL_ID}...")
            processor = DetrImageProcessor.from_pretrained(config.TABLE_STRUCTURE_MODEL_ID)
            model = TableTransformerForObjectDetection.from_pretrained(config.TABLE_STRUCTURE_MODEL_ID).to(
                config.DEVICE
            )
            model.eval()
            _registry["table_structure"] = (processor, model)
        return _registry["table_structure"]


def get_whisper_model():
    """openai-whisper — implémentation PyTorch native (volontairement pas
    `faster-whisper`, qui repose sur CTranslate2, hors périmètre PyTorch)."""
    with _lock:
        if "whisper" not in _registry:
            import whisper

            logger.info(f"[RAG/self_hosted] Chargement Whisper {config.WHISPER_MODEL_ID}...")
            _registry["whisper"] = whisper.load_model(config.WHISPER_MODEL_ID, device=config.DEVICE)
        return _registry["whisper"]
