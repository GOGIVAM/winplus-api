"""
Reciprocal Rank Fusion (Phase 2, §2.9) — fusionne les listes ordonnées
issues de la recherche dense et de BM25. Pure fonction, aucune dépendance ML.
"""

from __future__ import annotations

from typing import Dict, List, Tuple, TypeVar

K_SMOOTHING = 60  # constante standardisée dans la littérature (Cormack 2009)

T = TypeVar("T")


def reciprocal_rank_fusion(
    ranked_lists: List[List[T]], k: int = K_SMOOTHING
) -> List[Tuple[T, float]]:
    """
    ranked_lists : plusieurs listes d'identifiants déjà triées par
    pertinence décroissante (ex: [dense_ids, bm25_ids]).
    Retourne les identifiants fusionnés triés par score RRF décroissant.
    """
    scores: Dict[T, float] = {}
    for ranked in ranked_lists:
        for rank, item in enumerate(ranked, start=1):
            scores[item] = scores.get(item, 0.0) + 1.0 / (k + rank)

    return sorted(scores.items(), key=lambda kv: kv[1], reverse=True)
