"""
Atelier WinAI (25/11/2026) : productions gÃ©nÃ©rÃ©es Ã  partir des sources choisies par l'Ã©lÃ¨ve.
  POST /api/winai/flashcards/generate-content   fiches recto verso (question au recto, rÃ©ponse au verso)
  POST /api/winai/resume-audio/generate-content script d'un rÃ©sumÃ© Ã  Ã©couter (lu par la synthÃ¨se vocale
                                                 de l'appareil, aucun fichier audio produit ici)

AppelÃ©es par le .NET (WinAiAtelierController), qui contrÃ´le l'accÃ¨s aux sources, le droit d'abonnement
et le quota, puis enregistre le rÃ©sultat. Les sources arrivent dÃ©jÃ  traduites : sources_filter
({doc_ids, subject_ids, course_ids}) pour la recherche RAG, sources_context pour le texte prÃ©parÃ©
(ex. erreurs de quiz). Le coÃ»t de la recherche est imputÃ© Ã  la requÃªte (en-tÃªte X-WinAI-Tokens),
le coÃ»t du modÃ¨le l'est dÃ©jÃ  par DeepSeekClient.
"""

from __future__ import annotations

import json
import logging
import re
from typing import Any, Dict, List, Optional

from fastapi import APIRouter, Depends, HTTPException
from pydantic import BaseModel, Field

from auth import verify_token, UserTokenData
from services.deepseek_client import get_deepseek_client
from services.language import language_directive
from services.rag_chat_bridge import build_sources_context_block
from services.token_accounting import record_usage

logger = logging.getLogger(__name__)

winai_atelier_router = APIRouter(tags=["winai-atelier"])

MIN_CARDS, MAX_CARDS, DEFAULT_CARDS = 5, 30, 12
MIN_MINUTES, MAX_MINUTES, DEFAULT_MINUTES = 1, 5, 3
# DÃ©bit de lecture d'une voix de synthÃ¨se en franÃ§ais, en mots par minute.
WORDS_PER_MINUTE = 140


class AtelierRequest(BaseModel):
    user_id: int
    subject: Optional[str] = None
    topic: Optional[str] = None
    level: Optional[str] = None
    sources_filter: Optional[Dict[str, Any]] = None
    sources_context: Optional[str] = None


class FlashcardsRequest(AtelierRequest):
    count: int = Field(DEFAULT_CARDS, ge=MIN_CARDS, le=MAX_CARDS)


class ResumeAudioRequest(AtelierRequest):
    minutes: int = Field(DEFAULT_MINUTES, ge=MIN_MINUTES, le=MAX_MINUTES)


def _query_text(body: AtelierRequest) -> str:
    """Texte de recherche dans les sources : ce que l'Ã©lÃ¨ve a demandÃ©, sinon une demande gÃ©nÃ©rale."""
    parts = [p for p in (body.subject, body.topic) if p and p.strip()]
    return " ".join(parts) if parts else "notions essentielles, dÃ©finitions, formules et mÃ©thodes Ã  retenir"


async def sources_block(body: AtelierRequest, user_id: int) -> tuple[str, List[dict]]:
    """Bloc de prompt tirÃ© des sources choisies et citations ({doc_id, subject_id, course_id, title, page})."""
    block, citations = "", []
    if body.sources_filter:
        rag_block, cost, citations = await build_sources_context_block(
            body.sources_filter, [{"role": "user", "content": _query_text(body)}], user_id)
        if cost:
            record_usage(cost)
        block += rag_block or ""
    ctx = (body.sources_context or "").strip()[:6000]
    if ctx:
        block += "\n\n[Ã‰lÃ©ments choisis par l'Ã©lÃ¨ve]\n" + ctx
    return block, citations


def _subject_lines(body: AtelierRequest) -> str:
    lines = []
    if body.subject:
        lines.append(f"MatiÃ¨re : {body.subject}.")
    if body.topic:
        lines.append(f"Chapitre ou notion : {body.topic}.")
    if body.level:
        lines.append(f"Niveau scolaire de l'Ã©lÃ¨ve : {body.level}.")
    return "\n".join(lines)


def _extract_json(raw: str) -> dict:
    content = (raw or "").strip()
    if content.startswith("```"):
        content = "\n".join(content.split("\n")[1:]).rstrip("`").strip()
    match = re.search(r"\{.*\}", content, re.DOTALL)
    if not match:
        raise ValueError("aucun objet JSON dans la rÃ©ponse")
    text = match.group()
    try:
        return json.loads(text)
    except json.JSONDecodeError:
        # Formules LaTeX : barres obliques non Ã©chappÃ©es (mÃªme rÃ©paration que les quiz).
        from routes.smart_ai_routes import _repair_json_escapes
        return json.loads(_repair_json_escapes(text))


def _require_material(body: AtelierRequest, block: str) -> None:
    if not block.strip() and not (body.subject or body.topic):
        raise HTTPException(status_code=422, detail="Choisissez au moins une source, une matiÃ¨re ou un chapitre.")


def _clean_cards(raw_cards: Any, count: int) -> List[dict]:
    cards, seen = [], set()
    for c in raw_cards if isinstance(raw_cards, list) else []:
        if not isinstance(c, dict):
            continue
        front = str(c.get("front") or c.get("recto") or "").strip()
        back = str(c.get("back") or c.get("verso") or "").strip()
        if not front or not back or front.lower() in seen:
            continue
        seen.add(front.lower())
        hint = str(c.get("hint") or "").strip() or None
        cards.append({"front": front[:500], "back": back[:1500], "hint": hint[:300] if hint else None})
        if len(cards) >= count:
            break
    return cards


@winai_atelier_router.post("/winai/flashcards/generate-content", tags=["winai-atelier"])
async def generate_flashcards(body: FlashcardsRequest, current_user: UserTokenData = Depends(verify_token)):
    block, citations = await sources_block(body, int(current_user.user_id))
    _require_material(body, block)
    prompt = (
        "Tu es WinAI, professeur particulier pour un Ã©lÃ¨ve camerounais qui prÃ©pare ses examens.\n"
        f"CrÃ©e exactement {body.count} fiches recto verso pour rÃ©viser.\n"
        f"{_subject_lines(body)}\n"
        "Recto : une question courte et prÃ©cise (dÃ©finition, formule, date, mÃ©thode, application directe).\n"
        "Verso : la rÃ©ponse exacte, en une Ã  trois phrases, avec l'unitÃ© ou la formule s'il y en a.\n"
        "Indice facultatif : un mot ou une piste qui aide sans donner la rÃ©ponse.\n"
        "Varie les types de questions, du plus simple au plus exigeant, au niveau du programme officiel.\n"
        "Si des sources sont fournies, chaque fiche doit en venir ; n'invente rien qui les contredise.\n"
        f"{block}\n\n"
        "RÃ©ponds UNIQUEMENT en JSON (sans balises markdown autour) :\n"
        '{"title": "...", "subject": "...", "cards": [{"front": "...", "back": "...", "hint": "..."}]}'
        f"{language_directive()}"
    )
    client = get_deepseek_client()
    last_error: Optional[Exception] = None
    for max_tokens in (3500, 6000):
        raw = ""
        try:
            result = client.chat(messages=[{"role": "user", "content": prompt}], max_tokens=max_tokens, temperature=0.5)
            raw = result.get("content", "")
            data = _extract_json(raw)
            cards = _clean_cards(data.get("cards"), body.count)
            if len(cards) < min(MIN_CARDS, body.count):
                raise ValueError(f"{len(cards)} fiche(s) exploitable(s)")
            subject = body.subject or data.get("subject")
            title = str(data.get("title") or (f"Fiches : {body.topic or subject}" if (body.topic or subject) else "Fiches de rÃ©vision"))[:200]
            return {"success": True, "title": title, "subject": subject, "cards": cards, "citations": citations}
        except Exception as e:  # rÃ©ponse tronquÃ©e ou illisible : un second essai plus large
            last_error = e
            logger.warning(f"[flashcards] essai max_tokens={max_tokens} Ã©chouÃ© : {e} ; brut : {raw[:300]!r}")
    logger.error(f"[flashcards] Ã©chec : {last_error}")
    raise HTTPException(status_code=502, detail="WinAI n'a pas pu prÃ©parer les fiches. RÃ©essayez dans un instant.")


@winai_atelier_router.post("/winai/resume-audio/generate-content", tags=["winai-atelier"])
async def generate_resume_audio(body: ResumeAudioRequest, current_user: UserTokenData = Depends(verify_token)):
    block, citations = await sources_block(body, int(current_user.user_id))
    _require_material(body, block)
    target = body.minutes * WORDS_PER_MINUTE
    prompt = (
        "Tu es WinAI. Ã‰cris le texte d'un rÃ©sumÃ© audio que l'Ã©lÃ¨ve Ã©coutera, lu par une voix de synthÃ¨se.\n"
        f"{_subject_lines(body)}\n"
        f"Longueur visÃ©e : environ {target} mots ({body.minutes} minute(s) d'Ã©coute).\n"
        "Style parlÃ©, phrases courtes, tutoiement, ton encourageant. Pas de titre, pas de liste Ã  puces, "
        "pas de tableau, pas de Markdown, pas d'Ã©moji. Ã‰cris les formules en toutes lettres "
        "(Â« a au carrÃ© plus b au carrÃ© Â»). Commence par annoncer le sujet, termine par trois points Ã  retenir "
        "Ã©noncÃ©s clairement.\n"
        "Si des sources sont fournies, le rÃ©sumÃ© doit en venir ; n'invente rien qui les contredise.\n"
        f"{block}\n\n"
        "RÃ©ponds UNIQUEMENT en JSON (sans balises markdown autour) :\n"
        '{"title": "...", "subject": "...", "script": "..."}'
        f"{language_directive()}"
    )
    client = get_deepseek_client()
    last_error: Optional[Exception] = None
    for max_tokens in (int(target * 2.2) + 400, int(target * 3) + 800):
        raw = ""
        try:
            result = client.chat(messages=[{"role": "user", "content": prompt}], max_tokens=max_tokens, temperature=0.6)
            raw = result.get("content", "")
            data = _extract_json(raw)
            script = re.sub(r"[*#_`]+", "", str(data.get("script") or "")).strip()
            words = len(script.split())
            if words < 40:
                raise ValueError(f"script trop court ({words} mots)")
            subject = body.subject or data.get("subject")
            title = str(data.get("title") or (f"RÃ©sumÃ© : {body.topic or subject}" if (body.topic or subject) else "RÃ©sumÃ© audio"))[:200]
            return {
                "success": True, "title": title, "subject": subject, "script": script, "word_count": words,
                "estimated_seconds": round(words / WORDS_PER_MINUTE * 60), "citations": citations,
            }
        except Exception as e:
            last_error = e
            logger.warning(f"[resume-audio] essai max_tokens={max_tokens} Ã©chouÃ© : {e} ; brut : {raw[:300]!r}")
    logger.error(f"[resume-audio] Ã©chec : {last_error}")
    raise HTTPException(status_code=502, detail="WinAI n'a pas pu prÃ©parer le rÃ©sumÃ©. RÃ©essayez dans un instant.")
