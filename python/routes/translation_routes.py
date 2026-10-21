"""
Traduction des contenus de la base (fr -> en), par lots, avec glossaire imposé.

Appelé uniquement par le backend .NET (Services/Translation/ContentTextTranslator.cs)
avec le jeton technique « ai.content-translation » : aucun utilisateur n'est
débité, le coût est celui de la plateforme.

    POST /api/winai/translate-batch
    {
      "source_lang": "fr", "target_lang": "en",
      "rules": "...",                                   (consignes en plus des règles fixes)
      "glossary": [{"fr": "épreuve", "en": "paper"}],   (termes imposés)
      "items": [{"id": "0", "text": "Épreuve de mathématiques, BEPC 2023"}]
    }

Réponse (200) :
    { "items": [{"id": "0", "text": "Mathematics paper, BEPC 2023"}],
      "tokens_used": 412, "cached": 0, "missing": [] }

Un texte absent de la réponse du modèle est listé dans « missing » : .NET le
considère comme non traduit et le reprendra plus tard. 503 si WinAI ne répond pas.

Enregistrement dans app.py :
    from routes.translation_routes import router as translation_router
    app.include_router(translation_router)
"""

from __future__ import annotations

import hashlib
import json
import logging
import re
from collections import OrderedDict
from threading import Lock
from typing import Dict, List, Optional, Tuple

from fastapi import APIRouter, Depends, HTTPException
from pydantic import BaseModel, Field

from auth import UserTokenData, require_user_or_service
from services.deepseek_client import get_deepseek_client
from services import winai_environment

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/winai", tags=["translation"])

MAX_ITEMS = 40
MAX_TOTAL_CHARS = 12000

BASE_RULES = (
    "You are a professional translator for WinPlus, a Cameroonian exam-preparation platform. "
    "Translate each text from French into natural British English for students and parents. "
    "Use the glossary terms exactly when the French term appears. "
    "Keep proper nouns, school and exam names, acronyms (BEPC, ENS, ENSET, FMSB, IUT, ENSP, GCE...), "
    "numbers, years, codes, mathematical formulas, URLs and Markdown formatting exactly as they are. "
    "Do not add, explain or remove anything. If a text is already in English, return it unchanged."
)


class GlossaryTerm(BaseModel):
    fr: str = Field(..., min_length=1, max_length=300)
    en: str = Field(..., min_length=1, max_length=300)


class TranslateItem(BaseModel):
    id: str = Field(..., min_length=1, max_length=40)
    text: str = Field(..., max_length=20000)


class TranslateBatchRequest(BaseModel):
    source_lang: str = "fr"
    target_lang: str = "en"
    rules: Optional[str] = Field(None, max_length=2000)
    glossary: List[GlossaryTerm] = []
    items: List[TranslateItem]


class TranslatedItem(BaseModel):
    id: str
    text: str


class TranslateBatchResponse(BaseModel):
    items: List[TranslatedItem]
    tokens_used: int = 0
    cached: int = 0
    missing: List[str] = []


# ── Cache mémoire (le cache durable est côté .NET, table TranslationCache) ──

_CACHE_MAX = 5000
_cache: "OrderedDict[str, str]" = OrderedDict()
_cache_lock = Lock()


def _cache_key(text: str, glossary: List[GlossaryTerm], target: str) -> str:
    g = "|".join(f"{t.fr}={t.en}" for t in sorted(glossary, key=lambda t: t.fr))
    return hashlib.sha256(f"{target}\n{g}\n{text}".encode("utf-8")).hexdigest()


def _cache_get(key: str) -> Optional[str]:
    with _cache_lock:
        value = _cache.get(key)
        if value is not None:
            _cache.move_to_end(key)
        return value


def _cache_put(key: str, value: str) -> None:
    with _cache_lock:
        _cache[key] = value
        _cache.move_to_end(key)
        while len(_cache) > _CACHE_MAX:
            _cache.popitem(last=False)


def clear_cache() -> None:
    with _cache_lock:
        _cache.clear()


# ── Prompt et lecture de la réponse ──

def build_system_prompt(glossary: List[GlossaryTerm], rules: Optional[str], target: str) -> str:
    lines = [BASE_RULES]
    if target != "en":
        lines[0] = lines[0].replace("British English", target)
    if rules:
        lines.append(rules.strip())
    if glossary:
        lines.append("Glossary (French -> English), mandatory:")
        lines.extend(f"- {t.fr} -> {t.en}" for t in glossary[:200])
    lines.append(
        'Answer with JSON only: {"items": [{"id": "<same id>", "text": "<translation>"}]}, '
        "one entry per input item, same ids."
    )
    return "\n".join(lines)


def parse_model_items(raw: str) -> Dict[str, str]:
    raw = (raw or "").strip()
    if raw.startswith("```"):
        raw = "\n".join(raw.split("\n")[1:])
    if raw.endswith("```"):
        raw = raw.rsplit("```", 1)[0].strip()
    try:
        data = json.loads(raw)
    except json.JSONDecodeError:
        match = re.search(r"\{.*\}", raw, re.S)
        if not match:
            return {}
        try:
            data = json.loads(match.group(0))
        except json.JSONDecodeError:
            return {}
    items = data.get("items") if isinstance(data, dict) else data
    out: Dict[str, str] = {}
    if isinstance(items, list):
        for it in items:
            if isinstance(it, dict) and it.get("id") is not None and isinstance(it.get("text"), str) and it["text"].strip():
                out[str(it["id"])] = it["text"].strip()
    return out


_NUMBER = re.compile(r"\d+(?:[.,]\d+)?")


def keeps_numbers(source: str, translated: str) -> bool:
    """Numéros et années du texte source présents dans la traduction (contrôle de sûreté)."""
    return all(n in translated or n.replace(",", ".") in translated for n in set(_NUMBER.findall(source)))


def translate_items(items: List[TranslateItem], glossary: List[GlossaryTerm], rules: Optional[str],
                    target: str = "en") -> Tuple[Dict[str, str], int, int]:
    """Renvoie (traductions par id, tokens consommés, nombre servi par le cache)."""
    result: Dict[str, str] = {}
    cached = 0
    todo: List[TranslateItem] = []
    keys: Dict[str, str] = {}
    by_text: Dict[str, List[str]] = {}
    for it in items:
        if not it.text.strip():
            result[it.id] = it.text
            continue
        key = _cache_key(it.text, glossary, target)
        keys[it.id] = key
        hit = _cache_get(key)
        if hit is not None:
            result[it.id] = hit
            cached += 1
            continue
        # Textes identiques dans un même lot : un seul envoi.
        if it.text in by_text:
            by_text[it.text].append(it.id)
            continue
        by_text[it.text] = [it.id]
        todo.append(it)

    if not todo:
        return result, 0, cached

    system = build_system_prompt(glossary, rules, target)
    payload = json.dumps({"items": [{"id": it.id, "text": it.text} for it in todo]}, ensure_ascii=False)
    chars = sum(len(it.text) for it in todo)
    res = get_deepseek_client().chat(
        messages=[{"role": "user", "content": payload}],
        system_prompt=system,
        max_tokens=min(8000, 1500 + chars),
        temperature=0.2,
        json_mode=True,
    )
    tokens = int(res.get("tokens_used") or 0)
    if not res.get("success"):
        raise RuntimeError(res.get("error") or "WinAI indisponible")

    translated = parse_model_items(res.get("content", ""))
    for it in todo:
        text = translated.get(it.id)
        if not text:
            continue
        if not keeps_numbers(it.text, text):
            logger.warning("translate-batch : nombres modifiés pour l'élément %s, traduction écartée", it.id)
            continue
        _cache_put(keys[it.id], text)
        for same_id in by_text[it.text]:
            result[same_id] = text
    return result, tokens, cached


@router.post("/translate-batch", response_model=TranslateBatchResponse)
def translate_batch(
    request: TranslateBatchRequest,
    # Jeton technique seulement (roles=()) : aucun écran ne l'appelle directement.
    current_user: UserTokenData = Depends(require_user_or_service("ai.content-translation", roles=())),
):
    if request.source_lang != "fr" or request.target_lang not in ("en",):
        raise HTTPException(status_code=400, detail="Seule la traduction fr -> en est disponible.")
    if not request.items:
        return TranslateBatchResponse(items=[])
    if len(request.items) > MAX_ITEMS or sum(len(i.text) for i in request.items) > MAX_TOTAL_CHARS:
        raise HTTPException(status_code=413, detail=f"Lot trop grand ({MAX_ITEMS} textes, {MAX_TOTAL_CHARS} caractères au plus).")
    # Intégration : plafond quotidien propre à la traduction (WINAI_TRANSLATION_DAILY_TOKEN_CAP).
    category = winai_environment.set_category("translation")
    try:
        translated, tokens, cached = translate_items(request.items, request.glossary, request.rules, request.target_lang)
    except RuntimeError as exc:
        logger.warning("translate-batch : %s", exc)
        raise HTTPException(status_code=503, detail="Traduction automatique indisponible.")
    finally:
        winai_environment.reset_category(category)
    items = [TranslatedItem(id=i.id, text=translated[i.id]) for i in request.items if i.id in translated]
    missing = [i.id for i in request.items if i.id not in translated]
    return TranslateBatchResponse(items=items, tokens_used=tokens, cached=cached, missing=missing)
