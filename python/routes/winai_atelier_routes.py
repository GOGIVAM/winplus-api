"""
Atelier WinAI (25/11/2026) : productions générées à partir des sources choisies par l'élève.
  POST /api/winai/flashcards/generate-content   fiches recto verso (question au recto, réponse au verso)
  POST /api/winai/resume-audio/generate-content script d'un résumé à écouter (lu par la synthèse vocale
                                                 de l'appareil, aucun fichier audio produit ici)

Appelées par le .NET (WinAiAtelierController), qui contrôle l'accès aux sources, le droit d'abonnement
et le quota, puis enregistre le résultat. Les sources arrivent déjà traduites : sources_filter
({doc_ids, subject_ids, course_ids}) pour la recherche RAG, sources_context pour le texte préparé
(ex. erreurs de quiz). Le coût de la recherche est imputé à la requête (en-tête X-WinAI-Tokens),
le coût du modèle l'est déjà par DeepSeekClient.
"""

from __future__ import annotations

import asyncio
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
# Débit de lecture d'une voix de synthèse en français, en mots par minute.
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
    """Texte de recherche dans les sources : ce que l'élève a demandé, sinon une demande générale."""
    parts = [p for p in (body.subject, body.topic) if p and p.strip()]
    return " ".join(parts) if parts else "notions essentielles, définitions, formules et méthodes à retenir"


MAX_FALLBACK_PASSAGES = 10


def _scroll_sources(sources_filter: Dict[str, Any], user_id: int) -> tuple[str, List[dict]]:
    """Repli sans requête : premiers extraits des sources choisies, lus par filtre (mêmes filtres d'accès
    que la recherche : documents personnels limités à leur propriétaire, extraits actifs seulement).
    Sert quand l'élève a seulement coché des sources, sans matière ni chapitre : une requête générique
    ne remonte alors souvent rien au-dessus du seuil de pertinence."""
    from RAG.router import _active_collection
    from RAG.shared.vector_store import scroll_by_filter

    groups: List[dict] = []
    doc_ids = [str(d) for d in (sources_filter.get("doc_ids") or []) if d]
    subject_ids = [int(s) for s in (sources_filter.get("subject_ids") or []) if s is not None]
    course_ids = [int(c) for c in (sources_filter.get("course_ids") or []) if c is not None]
    if doc_ids:
        groups.append({"doc_id": doc_ids, "owner_user_id": int(user_id), "status": "active"})
    if subject_ids:
        groups.append({"subject_id": subject_ids, "status": "active"})
    if course_ids:
        groups.append({"course_id": course_ids, "status": "active"})
    collection = _active_collection()
    payloads: List[dict] = []
    for f in groups:
        payloads += [r.payload or {} for r in scroll_by_filter(collection, f, limit=MAX_FALLBACK_PASSAGES)]
    payloads.sort(key=lambda p: (str(p.get("doc_id")), p.get("page") or 0))
    payloads = [p for p in payloads if str(p.get("text") or "").strip()][:MAX_FALLBACK_PASSAGES]
    if not payloads:
        return "", []
    lines, cited, seen = [], [], set()
    for i, p in enumerate(payloads, start=1):
        where = f", page {p.get('page')}" if p.get("page") else ""
        excerpt = str(p.get("text")).strip().replace("\n", " ")[:700]
        lines.append(f"{i}. [Source : {p.get('title') or 'document'}{where}]\n{excerpt}")
        key = (p.get("doc_id"), p.get("page"))
        if key not in seen:
            seen.add(key)
            cited.append({"doc_id": p.get("doc_id"), "subject_id": p.get("subject_id"), "course_id": p.get("course_id"),
                          "title": p.get("title"), "page": p.get("page")})
    block = ("\n\n[Sources choisies par l'élève]\n" + "\n\n".join(lines)
             + "\n\nConsignes : appuie-toi en priorité sur ces sources, que l'élève a sélectionnées.")
    return block, cited


async def sources_block(body: AtelierRequest, user_id: int) -> tuple[str, List[dict]]:
    """Bloc de prompt tiré des sources choisies et citations ({doc_id, subject_id, course_id, title, page})."""
    block, citations = "", []
    if body.sources_filter:
        rag_block, cost, citations = await build_sources_context_block(
            body.sources_filter, [{"role": "user", "content": _query_text(body)}], user_id)
        if cost:
            record_usage(cost)
        if not (rag_block or "").strip():
            try:
                rag_block, citations = await asyncio.to_thread(_scroll_sources, body.sources_filter, user_id)
            except Exception as e:  # le repli ne doit jamais faire échouer la génération
                logger.warning(f"[atelier] lecture directe des sources impossible : {e}")
                rag_block, citations = "", []
        block += rag_block or ""
    ctx = (body.sources_context or "").strip()[:6000]
    if ctx:
        block += "\n\n[Éléments choisis par l'élève]\n" + ctx
    return block, citations


def _subject_lines(body: AtelierRequest) -> str:
    lines = []
    if body.subject:
        lines.append(f"Matière : {body.subject}.")
    if body.topic:
        lines.append(f"Chapitre ou notion : {body.topic}.")
    if body.level:
        lines.append(f"Niveau scolaire de l'élève : {body.level}.")
    return "\n".join(lines)


def _extract_json(raw: str) -> dict:
    content = (raw or "").strip()
    if content.startswith("```"):
        content = "\n".join(content.split("\n")[1:]).rstrip("`").strip()
    match = re.search(r"\{.*\}", content, re.DOTALL)
    if not match:
        raise ValueError("aucun objet JSON dans la réponse")
    text = match.group()
    try:
        return json.loads(text)
    except json.JSONDecodeError:
        # Formules LaTeX : barres obliques non échappées (même réparation que les quiz).
        from routes.smart_ai_routes import _repair_json_escapes
        return json.loads(_repair_json_escapes(text))


def _require_material(body: AtelierRequest, block: str) -> None:
    if not block.strip() and not (body.subject or body.topic):
        raise HTTPException(status_code=422, detail="Choisissez au moins une source, une matière ou un chapitre.")


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
        "Tu es WinAI, professeur particulier pour un élève camerounais qui prépare ses examens.\n"
        f"Crée exactement {body.count} fiches recto verso pour réviser.\n"
        f"{_subject_lines(body)}\n"
        "Recto : une question courte et précise (définition, formule, date, méthode, application directe).\n"
        "Verso : la réponse exacte, en une à trois phrases, avec l'unité ou la formule s'il y en a.\n"
        "Indice facultatif : un mot ou une piste qui aide sans donner la réponse.\n"
        "Varie les types de questions, du plus simple au plus exigeant, au niveau du programme officiel.\n"
        "Si des sources sont fournies, chaque fiche doit en venir ; n'invente rien qui les contredise.\n"
        f"{block}\n\n"
        "Réponds UNIQUEMENT en JSON (sans balises markdown autour) :\n"
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
            title = str(data.get("title") or (f"Fiches : {body.topic or subject}" if (body.topic or subject) else "Fiches de révision"))[:200]
            return {"success": True, "title": title, "subject": subject, "cards": cards, "citations": citations}
        except Exception as e:  # réponse tronquée ou illisible : un second essai plus large
            last_error = e
            logger.warning(f"[flashcards] essai max_tokens={max_tokens} échoué : {e} ; brut : {raw[:300]!r}")
    logger.error(f"[flashcards] échec : {last_error}")
    raise HTTPException(status_code=502, detail="WinAI n'a pas pu préparer les fiches. Réessayez dans un instant.")


@winai_atelier_router.post("/winai/resume-audio/generate-content", tags=["winai-atelier"])
async def generate_resume_audio(body: ResumeAudioRequest, current_user: UserTokenData = Depends(verify_token)):
    block, citations = await sources_block(body, int(current_user.user_id))
    _require_material(body, block)
    target = body.minutes * WORDS_PER_MINUTE
    prompt = (
        "Tu es WinAI. Écris le texte d'un résumé audio que l'élève écoutera, lu par une voix de synthèse.\n"
        f"{_subject_lines(body)}\n"
        f"Longueur visée : environ {target} mots ({body.minutes} minute(s) d'écoute).\n"
        "Style parlé, phrases courtes, tutoiement, ton encourageant. Pas de titre, pas de liste à puces, "
        "pas de tableau, pas de Markdown, pas d'émoji. Écris les formules en toutes lettres "
        "(« a au carré plus b au carré »). Commence par annoncer le sujet, termine par trois points à retenir "
        "énoncés clairement.\n"
        "Si des sources sont fournies, le résumé doit en venir ; n'invente rien qui les contredise.\n"
        f"{block}\n\n"
        "Réponds UNIQUEMENT en JSON (sans balises markdown autour) :\n"
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
            title = str(data.get("title") or (f"Résumé : {body.topic or subject}" if (body.topic or subject) else "Résumé audio"))[:200]
            return {
                "success": True, "title": title, "subject": subject, "script": script, "word_count": words,
                "estimated_seconds": round(words / WORDS_PER_MINUTE * 60), "citations": citations,
            }
        except Exception as e:
            last_error = e
            logger.warning(f"[resume-audio] essai max_tokens={max_tokens} échoué : {e} ; brut : {raw[:300]!r}")
    logger.error(f"[resume-audio] échec : {last_error}")
    raise HTTPException(status_code=502, detail="WinAI n'a pas pu préparer le résumé. Réessayez dans un instant.")


# -- Synthèses : bilan de la semaine (parent) et synthèse de classe (établissement) -------------------------

SYNTHESIS_KINDS = {
    "parent-week": (
        "Tu es WinAI, conseiller pédagogique familial de WinPlus. Écris le bilan de la semaine de {name} pour "
        "son parent, à partir des faits ci-dessous et d'eux seuls.\n"
        "Quatre courts paragraphes, sans titre ni liste : ce qui a été fait, ce qui progresse, le point "
        "d'attention s'il y en a un, puis une suggestion concrète et réaliste pour la semaine qui vient. "
        "Vouvoie le parent, appelle l'élève par son prénom, ton chaleureux et factuel, 120 à 180 mots. "
        "Si les faits montrent peu ou pas d'activité, dis-le simplement et propose comment relancer.",
        "Bilan de la semaine de {name}",
    ),
    "class": (
        "Tu es WinAI, conseiller pédagogique d'un établissement scolaire camerounais. Écris la synthèse de la "
        "classe « {name} » pour la direction, à partir des faits ci-dessous et d'eux seuls.\n"
        "Quatre courts paragraphes, sans titre ni liste : niveau et activité de la classe, matières en réussite, "
        "matières ou élèves à surveiller (sans nommer d'élève), puis deux actions concrètes pour l'équipe "
        "pédagogique. Ton professionnel et factuel, 150 à 220 mots.",
        "Synthèse de la classe {name}",
    ),
}


class SynthesisRequest(BaseModel):
    kind: str
    name: str = Field(..., min_length=1, max_length=120)
    facts: List[str] = Field(..., min_length=1, max_length=40)


@winai_atelier_router.post("/winai/synthese/generate-content", tags=["winai-atelier"])
async def generate_synthesis(body: SynthesisRequest, current_user: UserTokenData = Depends(verify_token)):
    if body.kind not in SYNTHESIS_KINDS:
        raise HTTPException(status_code=422, detail="Type de synthèse inconnu.")
    instruction, title = SYNTHESIS_KINDS[body.kind]
    facts = "\n".join(f"- {f.strip()[:300]}" for f in body.facts if f and f.strip())
    prompt = (instruction.format(name=body.name.strip()) + "\n\nFaits :\n" + facts
              + "\n\nRéponds uniquement avec le texte, sans Markdown." + language_directive())
    try:
        result = get_deepseek_client().chat(messages=[{"role": "user", "content": prompt}], max_tokens=700, temperature=0.5)
        text = re.sub(r"[*#_`]+", "", str(result.get("content") or "")).strip()
    except Exception as e:
        logger.error(f"[synthese] {body.kind} : {e}")
        text = ""
    if len(text.split()) < 20:
        raise HTTPException(status_code=502, detail="WinAI n'a pas pu rédiger la synthèse. Réessayez dans un instant.")
    return {"success": True, "title": title.format(name=body.name.strip())[:200], "text": text}