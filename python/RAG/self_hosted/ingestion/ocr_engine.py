"""
Chaîne OCR conditionnelle (Phase 1, §1.3-1.4) — GLM-OCR comme moteur unique
pour le texte scanné, les zones tamponnées et les images embarquées en mode
vision-langage. Remplace le duo PaddleOCR-VL/GLM-OCR du référentiel
original : meilleur score OmniDocBench actuel, et nativement PyTorch/HF
Transformers (voir topo validé).
"""

from __future__ import annotations

import io
import logging

import torch
from PIL import Image

from RAG.self_hosted import config
from RAG.self_hosted.models.loader import get_ocr_vlm

logger = logging.getLogger(__name__)

_TEXT_PROMPT = "Transcris fidèlement tout le texte visible sur ce document, dans l'ordre de lecture naturel."
_CAPTION_PROMPT = (
    "Décris ce schéma ou diagramme technique de façon structurée et indexable : "
    "type de diagramme, éléments principaux, relations entre ces éléments, texte visible."
)


def _run(image_bytes: bytes, prompt: str, max_new_tokens: int = 1024) -> str:
    processor, model = get_ocr_vlm()
    image = Image.open(io.BytesIO(image_bytes)).convert("RGB")

    messages = [
        {
            "role": "user",
            "content": [{"type": "image", "image": image}, {"type": "text", "text": prompt}],
        }
    ]
    inputs = processor.apply_chat_template(
        messages, tokenize=True, add_generation_prompt=True, return_tensors="pt", return_dict=True
    ).to(config.DEVICE)

    with torch.no_grad():
        output_ids = model.generate(**inputs, max_new_tokens=max_new_tokens, do_sample=False)

    generated = output_ids[:, inputs["input_ids"].shape[1] :]
    text = processor.batch_decode(generated, skip_special_tokens=True)[0]
    return text.strip()


def ocr_transcribe(image_bytes: bytes) -> str:
    """OCR standard — pages scannées et zones tamponnées (Phase 1, §1.3)."""
    return _run(image_bytes, _TEXT_PROMPT)


def caption_embedded_image(image_bytes: bytes) -> str:
    """Mode vision-langage — description indexable des schémas/diagrammes
    embarqués dans le corpus (Phase 1, §1.4)."""
    return _run(image_bytes, _CAPTION_PROMPT, max_new_tokens=512)
