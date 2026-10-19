"""
Lot 10.6 (multilingue web) : langue des réponses WinAI hors chat.

Langues prises en charge : français (défaut) et anglais. La langue du profil
(User.Locale côté .NET) arrive dans l'en-tête HTTP X-User-Locale, posé par le
backend .NET sur chaque appel vers ce service (AiLocaleHeaderHandler). Le
middleware de app.py la range dans une variable de contexte pour la durée de
la requête ; les routes la lisent via resolve_language() sans changer leur
signature. Un champ explicite (corps JSON) reste prioritaire quand une route
en reçoit un.

Le chat garde sa détection automatique (prompt_builder.detect_language) : ce
module ne le concerne pas.

Règle de compatibilité : en français, les consignes produites sont
strictement identiques au texte historique (« en français »), et
language_directive() ne produit rien. Aucun prompt français ne change.
"""

from __future__ import annotations

import contextvars
from typing import Optional

SUPPORTED_LANGUAGES = ("fr", "en")
DEFAULT_LANGUAGE = "fr"
USER_LOCALE_HEADER = "X-User-Locale"

_request_language: contextvars.ContextVar[Optional[str]] = contextvars.ContextVar(
    "winai_request_language", default=None
)


def normalize_language(value: object) -> str:
    """Ramène une valeur quelconque à "fr" ou "en" ("en-US", " EN " -> "en").
    Toute valeur inconnue ou absente donne le français."""
    parsed = _parse(value)
    return parsed or DEFAULT_LANGUAGE


def _parse(value: object) -> Optional[str]:
    if not isinstance(value, str):
        return None
    primary = value.strip().lower().replace("_", "-").split("-")[0]
    return primary if primary in SUPPORTED_LANGUAGES else None


def begin_request_language(header_value: Optional[str]) -> contextvars.Token:
    """Appelé par le middleware de app.py en début de requête."""
    return _request_language.set(_parse(header_value))


def end_request_language(token: contextvars.Token) -> None:
    try:
        _request_language.reset(token)
    except ValueError:
        pass


def resolve_language(explicit: object = None) -> str:
    """Langue à utiliser : valeur explicite reconnue, sinon celle de
    l'en-tête de la requête en cours, sinon le français."""
    return _parse(explicit) or _request_language.get() or DEFAULT_LANGUAGE


def is_english(lang: object = None) -> bool:
    return resolve_language(lang) == "en"


def in_language(lang: object = None) -> str:
    """Complément de langue à insérer dans un prompt rédigé en français :
    « en français » (texte historique) ou « en anglais »."""
    return "en anglais" if is_english(lang) else "en français"


def language_directive(lang: object = None) -> str:
    """Consigne à ajouter aux prompts qui n'imposent aucune langue (ils
    répondent alors en français, langue du prompt). Vide en français pour ne
    rien changer au prompt historique."""
    if is_english(lang):
        return (
            "\nRédige tous les textes destinés à l'utilisateur en anglais (in English), "
            "y compris ceux contenus dans le JSON ; garde les clés JSON et les valeurs "
            "techniques imposées inchangées.\n"
        )
    return ""
