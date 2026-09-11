"""
Segmentation en chunks (Phase 1, §1.6) — stratégie de double granularité
(parent-child chunking) + semantic chunking pour les documents structurés.

Agnostique du tokenizer : on injecte une fonction de comptage de tokens
(tokenizer Qwen3 en self_hosted, tiktoken en api) pour que la même logique
serve aux deux moteurs sans dupliquer le code.
"""

from __future__ import annotations

import re
import uuid
from dataclasses import dataclass
from typing import Callable, List, Optional

TokenCounter = Callable[[str], int]

# Marqueurs structurels reconnus pour le semantic chunking (Phase 1, §1.6) :
# numéros d'article/section, tirets de liste, titres numérotés. Généralisé
# depuis les textes normatifs CAMRAIL vers les contenus pédagogiques WinPlus
# (chapitres, exercices numérotés, sections d'épreuve).
STRUCTURAL_MARKERS = re.compile(
    r"^\s*(article\s+\d+|chapitre\s+\d+|section\s+[\divxlc]+|exercice\s+\d+|"
    r"partie\s+\d+|\d+[.)]\s+|[-•]\s+)",
    re.IGNORECASE,
)


@dataclass
class ChunkPair:
    chunk_id: str
    short_text: str
    long_text: str
    page: Optional[int] = None
    section: Optional[str] = None


def _split_words(text: str) -> List[str]:
    return text.split()


def _words_to_text(words: List[str]) -> str:
    return " ".join(words)


def fixed_window_chunk_pairs(
    text: str,
    count_tokens: TokenCounter,
    short_tokens: int = 128,
    long_tokens: int = 512,
    overlap_tokens: int = 20,
    page: Optional[int] = None,
) -> List[ChunkPair]:
    """Découpage en fenêtres de tokens fixes avec chevauchement (méthode par
    défaut, Phase 1 §1.6). Une approximation mot/token est utilisée pour
    rester indépendante du tokenizer exact ; le comptage réel via
    `count_tokens` affine la coupe finale."""
    words = _split_words(text)
    if not words:
        return []

    pairs: List[ChunkPair] = []
    i = 0
    approx_words_per_token = 0.75  # heuristique FR (~1.3 token/mot)

    while i < len(words):
        short_span = max(1, int(short_tokens * approx_words_per_token))
        long_span = max(short_span, int(long_tokens * approx_words_per_token))

        short_words = words[i : i + short_span]
        long_words = words[i : i + long_span]

        short_text = _words_to_text(short_words)
        long_text = _words_to_text(long_words)

        # Ajustement fin sur le comptage réel de tokens pour ne pas dépasser
        # significativement les bornes cibles.
        while count_tokens(short_text) > short_tokens * 1.15 and short_words:
            short_words = short_words[:-1]
            short_text = _words_to_text(short_words)
        while count_tokens(long_text) > long_tokens * 1.15 and long_words:
            long_words = long_words[:-1]
            long_text = _words_to_text(long_words)

        if short_text.strip():
            pairs.append(
                ChunkPair(chunk_id=str(uuid.uuid4()), short_text=short_text, long_text=long_text, page=page)
            )

        step = max(1, len(short_words) - int(overlap_tokens * approx_words_per_token))
        i += step

    return pairs


def semantic_chunk_pairs(
    text: str,
    count_tokens: TokenCounter,
    short_tokens: int = 128,
    long_tokens: int = 512,
    page: Optional[int] = None,
) -> List[ChunkPair]:
    """Découpage sur les frontières structurelles du document (articles,
    chapitres, exercices...) plutôt que sur des fenêtres fixes — utilisé pour
    les documents dont la structure garantit la complétude sémantique de
    chaque section (Phase 1 §1.6)."""
    lines = text.splitlines()
    sections: List[List[str]] = []
    current: List[str] = []

    for line in lines:
        if STRUCTURAL_MARKERS.match(line) and current:
            sections.append(current)
            current = [line]
        else:
            current.append(line)
    if current:
        sections.append(current)

    pairs: List[ChunkPair] = []
    for section_lines in sections:
        section_text = "\n".join(section_lines).strip()
        if not section_text:
            continue
        section_title = section_lines[0].strip()[:120]

        if count_tokens(section_text) <= long_tokens * 1.2:
            # Section assez courte : elle sert elle-même de chunk long, et
            # son début tronqué de chunk court.
            words = _split_words(section_text)
            short_span = max(1, int(short_tokens * 0.75))
            short_text = _words_to_text(words[:short_span])
            pairs.append(
                ChunkPair(
                    chunk_id=str(uuid.uuid4()),
                    short_text=short_text or section_text,
                    long_text=section_text,
                    page=page,
                    section=section_title,
                )
            )
        else:
            # Section trop longue pour un seul chunk long : repli sur le
            # découpage par fenêtres fixes à l'intérieur de la section.
            sub_pairs = fixed_window_chunk_pairs(
                section_text, count_tokens, short_tokens, long_tokens, page=page
            )
            for p in sub_pairs:
                p.section = section_title
            pairs.extend(sub_pairs)

    return pairs


def has_structural_markers(text: str, min_hits: int = 3) -> bool:
    """Heuristique pour décider si un document est assez structuré pour
    bénéficier du semantic chunking plutôt que du découpage par fenêtres."""
    hits = sum(1 for line in text.splitlines() if STRUCTURAL_MARKERS.match(line))
    return hits >= min_hits
