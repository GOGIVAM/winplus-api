"""
Évaluateur de complexité (Phase 3, §3.2) — décide si une requête est SIMPLE
(passage unique) ou COMPLEXE (multi-hop) avant de choisir le modèle de
génération. Heuristique légère, sans appel modèle : connecteurs multi-hop et
nombre d'entités distinctes dans la requête.
"""

from __future__ import annotations

import re

_MULTI_HOP_CONNECTORS = re.compile(
    r"\b(et si|à la fois|en même temps|par rapport à|comparé à|"
    r"si.*alors|à la différence de|contrairement à|ainsi que|"
    r"aussi bien que|en plus de)\b",
    re.IGNORECASE,
)

# Approximation simple d'entités : suites de mots capitalisés ou sigles.
_ENTITY_RE = re.compile(r"\b([A-ZÀ-Ý][a-zà-ÿ]+(?:\s+[A-ZÀ-Ý][a-zà-ÿ]+)*|[A-Z]{2,})\b")


def classify_complexity(question: str) -> str:
    """Retourne "simple" ou "complex"."""
    has_connector = bool(_MULTI_HOP_CONNECTORS.search(question))
    entities = set(_ENTITY_RE.findall(question))

    if has_connector or len(entities) >= 3:
        return "complex"
    return "simple"
