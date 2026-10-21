"""
Environnement d'exécution de WinAI : production ou intégration.

Production : WINPLUS_ENV absent. Rien ne change (aucun plafond, aucun contrôle).

Intégration (WINPLUS_ENV=integration, service winplus-fastapi-integration) :

1. Isolation (assert_isolation, appelé au démarrage par app.py) : le service
   refuse de démarrer si une seule ressource pointe vers la production :
     - base PostgreSQL : DB_NAME obligatoire et différent de « winplus_db » ;
     - index Qdrant : RAG_API_COLLECTION et RAG_SH_COLLECTION obligatoires,
       contenant « integration » ;
     - index BM25 et graphes : RAG_BM25_STORAGE_DIR et RAG_GRAPH_STORAGE_DIR
       obligatoires, contenant « integration » ;
     - compteur du plafond : WINAI_USAGE_DIR contenant « integration ».
   Les budgets de tokens, profils, signaux et historiques de chat sont écrits
   par le Gateway .NET d'intégration dans winplus_integration (en-tête
   X-WinAI-Tokens), jamais par ce service dans une autre base.

2. Plafond quotidien de dépense (WINAI_DAILY_TOKEN_CAP, tokens par jour UTC,
   tous appels DeepSeek confondus ; WINAI_TRANSLATION_DAILY_TOKEN_CAP pour la
   traduction automatique). Compteur partagé par les workers uvicorn (fichier
   verrouillé dans WINAI_USAGE_DIR). Plafond atteint : WinAIDailyCapReached,
   réponse HTTP 429 claire et traduite (app.py), sans aucun appel au fournisseur.

3. WinAI simulé (WINAI_SIMULATED=true, intégration seulement, désactivé par
   défaut) : réponses factices marquées « [WinAI simulé] », sans appel au
   fournisseur, pour garder les écrans testables sans clé ni budget.
"""

from __future__ import annotations

import contextvars
import json
import os
import threading
from datetime import datetime, timezone
from typing import Dict, Optional

PRODUCTION_DB_NAME = "winplus_db"
_lock = threading.Lock()

# Catégorie de l'appel en cours (« translation » pour translate-batch).
_category: contextvars.ContextVar[str] = contextvars.ContextVar("winai_category", default="general")


def is_integration() -> bool:
    return (os.getenv("WINPLUS_ENV") or "").strip().lower() == "integration"


def is_simulated() -> bool:
    return is_integration() and (os.getenv("WINAI_SIMULATED") or "").strip().lower() == "true"


class IsolationError(RuntimeError):
    pass


def isolation_problems() -> list[str]:
    """Liste des ressources de production encore visées (vide = isolé)."""
    if not is_integration():
        return []
    problems: list[str] = []
    db = (os.getenv("DB_NAME") or "").strip()
    if not db:
        problems.append("DB_NAME absent (le défaut est la base de production)")
    elif db == PRODUCTION_DB_NAME:
        problems.append("DB_NAME vise la base de production winplus_db")
    for var in ("RAG_API_COLLECTION", "RAG_SH_COLLECTION", "RAG_BM25_STORAGE_DIR", "RAG_GRAPH_STORAGE_DIR", "WINAI_USAGE_DIR"):
        value = (os.getenv(var) or "").strip()
        if not value:
            problems.append(f"{var} absent (le défaut est partagé avec la production)")
        elif "integration" not in value.lower():
            problems.append(f"{var}={value} ne désigne pas une ressource d'intégration")
    return problems


def assert_isolation() -> None:
    problems = isolation_problems()
    if problems:
        raise IsolationError("WinAI intégration : démarrage refusé, " + " ; ".join(problems))


# ── Plafond quotidien ──────────────────────────────────────────────────────

class WinAIDailyCapReached(RuntimeError):
    code = "winai_daily_cap"
    messages = {
        "fr": "WinAI a atteint son plafond quotidien sur l'environnement d'intégration. Réessaie demain.",
        "en": "WinAI has reached its daily limit on the integration environment. Please try again tomorrow.",
    }

    def __init__(self, category: str):
        super().__init__(self.messages["fr"])
        self.category = category

    def message(self, accept_language: Optional[str]) -> str:
        lang = "en" if (accept_language or "").lower().startswith("en") else "fr"
        return self.messages[lang]


def register_cap_handler(app) -> None:
    """Réponse HTTP 429 claire et traduite (X-User-Locale, sinon Accept-Language)."""
    from fastapi.responses import JSONResponse

    @app.exception_handler(WinAIDailyCapReached)
    async def _winai_daily_cap(request, exc: WinAIDailyCapReached):
        lang = request.headers.get("X-User-Locale") or request.headers.get("accept-language")
        return JSONResponse(status_code=429, content={
            "error": exc.code, "code": exc.code, "message": exc.message(lang), "category": exc.category,
        })


def _cap_for(category: str) -> int:
    var = "WINAI_TRANSLATION_DAILY_TOKEN_CAP" if category == "translation" else "WINAI_DAILY_TOKEN_CAP"
    try:
        return max(0, int((os.getenv(var) or "0").strip()))
    except ValueError:
        return 0


def _usage_file() -> str:
    directory = os.getenv("WINAI_USAGE_DIR") or "./data/winai-usage-integration"
    os.makedirs(directory, exist_ok=True)
    return os.path.join(directory, f"usage-{datetime.now(timezone.utc):%Y-%m-%d}.json")


def _locked_update(update) -> Dict[str, int]:
    """Lit puis réécrit le compteur du jour sous verrou (fichier + thread)."""
    path = _usage_file()
    with _lock:
        with open(path, "a+", encoding="utf-8") as handle:
            try:
                import fcntl  # Linux (serveur) ; absent sous Windows (tests)
                fcntl.flock(handle, fcntl.LOCK_EX)
            except ImportError:
                pass
            handle.seek(0)
            raw = handle.read()
            data: Dict[str, int] = json.loads(raw) if raw.strip() else {}
            new = update(dict(data))
            if new is not None and new != data:
                handle.seek(0)
                handle.truncate()
                handle.write(json.dumps(new))
            return new if new is not None else data


def usage_today() -> Dict[str, int]:
    return _locked_update(lambda d: None)


def set_category(category: str) -> contextvars.Token:
    return _category.set(category)


def reset_category(token: contextvars.Token) -> None:
    try:
        _category.reset(token)
    except ValueError:
        pass


def ensure_budget() -> None:
    """Avant un appel au fournisseur : lève WinAIDailyCapReached si le plafond du jour est atteint."""
    if not is_integration():
        return
    category = _category.get()
    used = usage_today()
    for cat in {category, "general"}:
        cap = _cap_for(cat)
        if cap and used.get(cat if cat == "translation" else "total", 0) >= cap:
            raise WinAIDailyCapReached(cat)


def add_usage(tokens: int) -> None:
    """Après un appel : ajoute le coût au compteur du jour (intégration seulement)."""
    if not is_integration() or not tokens or tokens <= 0:
        return
    category = _category.get()

    def update(d: Dict[str, int]) -> Dict[str, int]:
        d["total"] = int(d.get("total", 0)) + int(tokens)
        if category == "translation":
            d["translation"] = int(d.get("translation", 0)) + int(tokens)
        return d

    _locked_update(update)


# ── WinAI simulé ───────────────────────────────────────────────────────────

SIMULATED_MARK = "[WinAI simulé]"


def simulated_content(json_mode: bool) -> str:
    text = f"{SIMULATED_MARK} Réponse factice de l'environnement d'intégration (WINAI_SIMULATED=true)."
    if json_mode:
        return json.dumps({"simulated": True, "message": text}, ensure_ascii=False)
    return text
