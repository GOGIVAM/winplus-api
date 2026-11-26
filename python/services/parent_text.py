"""
Rédaction des messages WinAI destinés aux parents (26/11/2026, tournée du mobile, parent B8).

Les messages sortaient en forme de lettre (« Bonjour, nous avons remarqué… »), trop longs, avec des tirets
longs et des apostrophes mêlées. Deux garde-fous communs :
  * PARENT_STYLE, à ajouter à la consigne système de toute génération destinée à un parent ;
  * tidy_parent_text(), appliqué au texte reçu (le modèle ne suit pas toujours la consigne).
Décision de Georges : vouvoiement pour le parent (tutoiement réservé à l'élève).
"""

from __future__ import annotations

import re

PARENT_STYLE = (
    "Règles de rédaction : vouvoie le parent. Pas de formule d'appel ni de politesse (pas de « Bonjour », "
    "« Cher parent », « Cordialement »), pas de « nous », pas de signature. Va droit au fait, en une ou deux "
    "phrases courtes (35 mots au plus). N'utilise jamais de tiret long ni de tiret moyen : une virgule, un "
    "deux-points ou un point à la place. Apostrophe droite ('). Pas de Markdown ni d'émoji."
)

_GREETING = re.compile(
    r"^\s*(?:bonjour|bonsoir|salut|hello|cher|chère|chers|chères)\b[^.!?\n]{0,60}?[,.!:]\s*",
    re.IGNORECASE,
)
_CLOSING = re.compile(
    r"\s*(?:cordialement|bien à vous|bien cordialement|à bientôt|l'équipe winplus|winai)\s*[.!]?\s*$",
    re.IGNORECASE,
)
_DASHES = re.compile(r"\s*[—–]\s*")
_SPACES = re.compile(r"[ \t]{2,}")
_SENTENCE = re.compile(r"(?<=[.!?])\s+(?=[A-ZÀ-ÖØ-Þ0-9«])")


def tidy_parent_text(text: str | None, max_sentences: int = 2) -> str:
    """Retire formule d'appel et de fin, remplace les tirets longs, unifie les apostrophes,
    réduit les espaces doubles et limite le nombre de phrases."""
    if not text:
        return ""
    t = text.strip().strip('"').strip()
    t = t.replace("’", "'").replace("‘", "'").replace("ʼ", "'")
    t = _GREETING.sub("", t)
    t = _CLOSING.sub("", t).strip()
    t = _DASHES.sub(", ", t)
    t = _SPACES.sub(" ", t)
    t = re.sub(r"\s+([,.])", r"\1", t)
    t = re.sub(r",\s*,", ",", t)
    if t:
        t = t[0].upper() + t[1:]
    if max_sentences and max_sentences > 0:
        parts = _SENTENCE.split(t)
        t = " ".join(parts[:max_sentences]).strip()
    return t
