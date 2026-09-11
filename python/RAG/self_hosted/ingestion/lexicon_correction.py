"""
Post-correction OCR par lexique métier (Phase 1, §1.5) — corrige les
substitutions de caractères statistiquement prévisibles sur les termes
techniques absents des corpus d'entraînement des moteurs OCR/ASR (sigles
d'examens, noms de matières, vocabulaire pédagogique camerounais : BEPC,
Probatoire, ENSP, FMSB, ENAM...).

`rapidfuzz` calcule la distance de Levenshtein normalisée — c'est un
utilitaire de comparaison de chaînes, pas un framework ML, au même titre que
rank_bm25 ou Qdrant.
"""

from __future__ import annotations

import logging
import os
import re
from functools import lru_cache
from typing import List

from rapidfuzz import fuzz, process

from RAG.self_hosted import config

logger = logging.getLogger(__name__)

_WORD_RE = re.compile(r"\b[\wÀ-ÿ]+\b", re.UNICODE)

# Lexique de repli si le fichier n'existe pas encore — à enrichir par
# extraction semi-automatique sur un échantillon du corpus WinPlus (Phase 0).
_DEFAULT_LEXICON = [
    "BEPC", "Probatoire", "Baccalauréat", "BTS", "Licence", "Master",
    "ENSP", "ENSPM", "FMSB", "ENS", "ENSET", "ENAM", "IRIC", "ESSTIC", "ESSEC",
    "Mathématiques", "Physique-Chimie", "SVT", "Philosophie", "Histoire-Géographie",
]


@lru_cache(maxsize=1)
def _load_lexicon() -> List[str]:
    if os.path.exists(config.LEXICON_PATH):
        with open(config.LEXICON_PATH, "r", encoding="utf-8") as f:
            terms = [line.strip() for line in f if line.strip()]
        if terms:
            return terms
    logger.warning(f"[RAG/self_hosted] Lexique introuvable ({config.LEXICON_PATH}) — repli sur le lexique par défaut.")
    return _DEFAULT_LEXICON


def correct_text(text: str) -> str:
    lexicon = _load_lexicon()
    words = _WORD_RE.findall(text)
    corrected = text

    for word in set(words):
        if len(word) < 3:
            continue
        match = process.extractOne(word, lexicon, scorer=fuzz.ratio)
        if match is None:
            continue
        candidate, score, _ = match
        if candidate.lower() == word.lower():
            continue
        similarity_ratio = score / 100.0
        if similarity_ratio >= (1 - config.LEXICON_MAX_EDIT_DISTANCE_RATIO):
            corrected = re.sub(rf"\b{re.escape(word)}\b", candidate, corrected)

    return corrected
