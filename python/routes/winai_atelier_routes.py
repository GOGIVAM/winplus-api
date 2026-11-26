"""
Atelier WinAI (25/11/2026) : productions générées à partir des sources choisies par l'élève.
  POST /api/winai/flashcards/generate-content   fiches recto verso (question au recto, réponse au verso)
  POST /api/winai/resume-audio/generate-content script d'un résumé à écouter (lu par la synthèse vocale
                                                 de l'appareil, aucun fichier audio produit ici)
  POST /api/teacher/epreuve/generate-content    épreuve complète pour l'enseignant : sujet, barème, corrigé

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
import time
from typing import Any, Dict, List, Optional

from fastapi import APIRouter, Depends, HTTPException
from pydantic import BaseModel, Field

from auth import verify_token, require_role, UserTokenData
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
    # Plancher large : avec 700 jetons le modèle rendait une réponse vide (budget consommé avant la sortie).
    for max_tokens in (max(2500, int(target * 3) + 800), max(4000, int(target * 4) + 1200)):
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


# -- Épreuve complète pour l'enseignant : sujet, barème et corrigé (26/11/2026) -----------------------------

EXAM_TYPES = {
    "interrogation": "une interrogation écrite courte",
    "devoir": "un devoir surveillé",
    "composition": "une composition trimestrielle",
    "examen-blanc": "un examen blanc au format de l'examen officiel (BEPC, Probatoire, Baccalauréat, GCE)",
}


class EpreuveRequest(AtelierRequest):
    exam_type: str = "devoir"
    duration_minutes: int = Field(120, ge=15, le=300)
    total_points: int = Field(20, ge=5, le=100)
    exercises: int = Field(3, ge=1, le=6)
    instructions: Optional[str] = Field(None, max_length=1000)


def _num(v: Any) -> float:
    try:
        return max(0.0, float(str(v).replace(",", ".")))
    except (TypeError, ValueError):
        return 0.0


def _quarter(x: float) -> float:
    return round(x * 4) / 4


def _clean_exam(data: dict, total: int) -> List[dict]:
    """Exercices exploitables, barème ramené au total demandé (au quart de point), points
    d'un exercice = somme de ses questions."""
    exercises: List[dict] = []
    for ex in data.get("exercises") if isinstance(data.get("exercises"), list) else []:
        if not isinstance(ex, dict):
            continue
        questions = []
        for i, q in enumerate(ex.get("questions") if isinstance(ex.get("questions"), list) else [], start=1):
            if not isinstance(q, dict) or not str(q.get("statement") or "").strip():
                continue
            questions.append({
                "number": str(q.get("number") or i)[:10],
                "statement": str(q.get("statement")).strip()[:2000],
                "points": _num(q.get("points")),
                "answer": str(q.get("answer") or "").strip()[:3000],
                "scoringGuide": str(q.get("scoring_guide") or q.get("scoringGuide") or "").strip()[:1000],
            })
        if questions:
            exercises.append({
                "title": str(ex.get("title") or f"Exercice {len(exercises) + 1}").strip()[:200],
                "context": str(ex.get("context") or "").strip()[:3000] or None,
                "questions": questions,
            })
    all_q = [q for ex in exercises for q in ex["questions"]]
    if not all_q:
        return []
    raw_sum = sum(q["points"] for q in all_q)
    if raw_sum <= 0:
        for q in all_q:
            q["points"] = total / len(all_q)
    elif abs(raw_sum - total) > 0.01:
        for q in all_q:
            q["points"] = q["points"] * total / raw_sum
    for q in all_q:
        q["points"] = max(0.25, _quarter(q["points"]))
    # Écart d'arrondi reporté sur la question la plus notée.
    gap = total - sum(q["points"] for q in all_q)
    if abs(gap) > 0.001:
        top = max(all_q, key=lambda q: q["points"])
        top["points"] = max(0.25, _quarter(top["points"] + gap))
    for ex in exercises:
        ex["points"] = sum(q["points"] for q in ex["questions"])
    return exercises


EXAM_LABELS = {"interrogation": "Interrogation écrite", "devoir": "Devoir surveillé",
               "composition": "Composition", "examen-blanc": "Examen blanc"}
# Budget d'un exercice : le modèle raisonne avant d'écrire (8 000 jetons ne donnaient que 4 300 caractères
# pour une épreuve entière, en 40 s). Un appel par exercice, en parallèle, reste sous le délai de 60 s du .NET.
# À 4 000 jetons, les exercices exigeants revenaient vides (tout le budget passé à raisonner, en ~23 s) ;
# 7 000 jetons tiennent en ~35 s, plan compris, sous le délai.
EXERCISE_TOKENS = 7000
PLAN_TOKENS = 1500
# Le .NET coupe à 60 s : un second essai d'exercice n'est lancé que s'il peut finir avant.
EXAM_DEADLINE = 52.0
LETTERS = ["ABC", "EFG", "MNP", "RST", "IJK", "UVW"]
_POINTS_IN_TITLE = re.compile(r"\s*[\(\[–—-]?\s*\d+(?:[.,]\d+)?\s*(?:pts?|points?)\s*[\)\]]?\s*$", re.IGNORECASE)

_TAG = re.compile(r"^\s*\**\s*(TITRE|CONTEXTE|QUESTION|[ÉE]NONC[ÉE]|R[ÉE]PONSE|BAR[ÈE]ME)\s*\**\s*:\s*(.*)$", re.IGNORECASE)
_TAG_KEYS = {"TITRE": "title", "CONTEXTE": "context", "ENONCE": "statement", "REPONSE": "answer", "BAREME": "scoring_guide"}


def _tag_name(t: str) -> str:
    return t.upper().replace("É", "E").replace("È", "E")


def _parse_exercise(raw: str) -> Optional[dict]:
    """Exercice au format balisé (TITRE, CONTEXTE, puis QUESTION: n | points, ÉNONCÉ, RÉPONSE, BARÈME).
    Pas de JSON : les formules LaTeX et les guillemets n'ont rien à échapper."""
    ex: dict = {"title": "", "context": "", "questions": []}
    field: Optional[tuple] = None
    for line in (raw or "").replace("\r", "").split("\n"):
        m = _TAG.match(line)
        if m:
            tag, rest = _tag_name(m.group(1)), m.group(2).strip()
            if tag == "QUESTION":
                number, _, pts = rest.partition("|")
                pts = pts.strip().split()
                ex["questions"].append({"number": number.strip().rstrip(".") or str(len(ex["questions"]) + 1),
                                        "points": _num(pts[0]) if pts else 0.0,
                                        "statement": "", "answer": "", "scoring_guide": ""})
                field = None
                continue
            key = _TAG_KEYS[tag]
            target = ex if key in ("title", "context") or not ex["questions"] else ex["questions"][-1]
            target[key] = rest
            field = (target, key)
        elif field is not None:
            target, key = field
            target[key] = f"{target[key]}\n{line}" if target[key] else line.strip()
    for q in ex["questions"]:
        for k in ("statement", "answer", "scoring_guide"):
            q[k] = q[k].strip()
    ex["questions"] = [q for q in ex["questions"] if q["statement"]]
    if not ex["questions"]:
        return None
    ex["title"], ex["context"] = ex["title"].strip(), ex["context"].strip()
    return ex


def _exercise_targets(n: int, total: int) -> List[float]:
    """Points visés par exercice : croissants avec la difficulté (poids 2, 3, 4…), au demi-point."""
    weights = [2 + k for k in range(n)]
    targets = [round(total * w / sum(weights) * 2) / 2 for w in weights]
    targets[-1] += total - sum(targets)
    return targets


def _exercise_role(k: int, n: int) -> str:
    if n == 1:
        return "un exercice complet qui va des questions de cours aux applications plus exigeantes"
    if k == 0:
        return "le premier exercice : restitution des connaissances et applications directes du cours"
    if k == n - 1:
        return ("le dernier exercice : un problème ou une situation d'intégration plus exigeante, dans un contexte "
                "concret de la vie au Cameroun")
    return f"l'exercice {k + 1} : une application de difficulté moyenne, sur une autre partie du programme que le premier"


def _parse_plan(raw: str, n: int) -> List[str]:
    """Plan de l'épreuve : une ligne « k. partie du chapitre : type d'exercice » par exercice."""
    lines = []
    for line in (raw or "").replace("\r", "").split("\n"):
        m = re.match(r"^\s*\**\s*(\d+)\s*[.)\-:]\s*(.+?)\s*$", line)
        if m and m.group(2).strip("* "):
            lines.append(m.group(2).strip("* ")[:400])
    return lines[:n] if len(lines) >= n else []


@winai_atelier_router.post("/teacher/epreuve/generate-content", tags=["winai-atelier"])
async def generate_epreuve(body: EpreuveRequest,
                           current_user: UserTokenData = Depends(require_role("teacher", "admin"))):
    block, citations = await sources_block(body, int(current_user.user_id))
    _require_material(body, block)
    exam_type = body.exam_type if body.exam_type in EXAM_TYPES else "devoir"
    kind = EXAM_TYPES[exam_type]
    extra = f"Consignes de l'enseignant : {body.instructions.strip()}\n" if (body.instructions or "").strip() else ""
    subject_lines = _subject_lines(body).replace("Niveau scolaire de l'élève", "Classe")
    n, targets = body.exercises, _exercise_targets(body.exercises, body.total_points)
    client = get_deepseek_client()
    started = time.monotonic()

    # Plan commun : chaque exercice reçoit une partie distincte du chapitre, sinon les appels parallèles,
    # qui ne se voient pas, écrivent le même exercice (essai réel : deux fois le même triangle 6-8-10).
    plan: List[str] = []
    if n > 1:
        plan_prompt = (
            f"Tu prépares {kind} pour des enseignants camerounais (programmes MINESEC).\n{subject_lines}\n{extra}"
            f"Découpe le contenu à évaluer en {n} exercices qui portent sur des parties ou compétences DIFFÉRENTES, "
            "du plus simple au plus exigeant ; le dernier est une situation concrète de la vie au Cameroun.\n"
            f"{block}\n\nRéponds UNIQUEMENT par {n} lignes numérotées « 1. partie évaluée : type d'exercice », sans "
            f"autre texte.{language_directive()}"
        )
        try:
            raw_plan = (await asyncio.to_thread(client.chat, messages=[{"role": "user", "content": plan_prompt}],
                                                max_tokens=PLAN_TOKENS, temperature=0.4)).get("content", "")
            plan = _parse_plan(raw_plan, n)
            if not plan:
                logger.warning(f"[epreuve] plan illisible : {raw_plan[:200]!r}")
        except Exception as e:
            logger.warning(f"[epreuve] plan impossible : {e}")

    def prompt_for(k: int) -> str:
        focus = f"Cet exercice porte UNIQUEMENT sur : {plan[k]}.\n" if plan else ""
        others = ("Les autres exercices portent sur : " + " ; ".join(p for i, p in enumerate(plan) if i != k)
                  + ". N'y empiète pas.\n") if plan else ""
        return (
            "Tu es WinAI, concepteur d'épreuves pour des enseignants camerounais (programmes officiels MINESEC).\n"
            f"Tu rédiges l'exercice {k + 1} sur {n} de {kind}, avec son corrigé et son barème.\n"
            f"{subject_lines}\n"
            f"Durée totale de l'épreuve : {body.duration_minutes} minutes pour {n} exercice(s).\n"
            f"Cet exercice est {_exercise_role(k, n)}. Il vaut exactement {targets[k]:g} points.\n"
            f"{focus}{others}"
            "Données originales (pas le triangle 3-4-5 ni ses multiples) ; nomme les points avec les lettres "
            f"{LETTERS[k % len(LETTERS)]}… pour ne pas reprendre les figures des autres exercices.\n"
            f"{extra}"
            "Exigences : niveau exact de la classe, questions numérotées et progressives, faisable dans le temps ; "
            "pour chaque question, la réponse attendue complète (démarche et résultat) et le barème par étape ; "
            "formules en LaTeX ($...$) ; aucune donnée personnelle ; si des sources sont fournies, l'exercice porte "
            "sur leur contenu.\n"
            f"{block}\n\n"
            "Réponds UNIQUEMENT dans ce format balisé, sans JSON ni Markdown, chaque balise en début de ligne :\n"
            f"TITRE: Exercice {k + 1} : ...\n"
            "CONTEXTE: énoncé commun, données ou document (sur une ou plusieurs lignes, ou vide)\n"
            f"QUESTION: {k + 1}.a | 2\n"
            "ÉNONCÉ: ...\n"
            "RÉPONSE: ...\n"
            "BARÈME: ...\n"
            f"QUESTION: {k + 1}.b | 3\n"
            "(et ainsi de suite ; le nombre après | est le nombre de points de la question).\n"
            f"Les nombres après | doivent totaliser EXACTEMENT {targets[k]:g} points, et le BARÈME de chaque question "
            "détaille exactement ses points : vérifie l'addition avant de répondre. Ne mets pas les points dans le TITRE."
            f"{language_directive()}"
        )

    def one(k: int) -> Optional[dict]:
        best: Optional[dict] = None
        for attempt in (1, 2):
            raw, t0 = "", time.monotonic()
            try:
                raw = client.chat(messages=[{"role": "user", "content": prompt_for(k)}],
                                  max_tokens=EXERCISE_TOKENS, temperature=0.4).get("content", "")
                ex = _parse_exercise(raw)
                if not ex:
                    raise ValueError("aucune question exploitable")
                ex["title"] = _POINTS_IN_TITLE.sub("", ex["title"]).strip() or f"Exercice {k + 1}"
                best = ex
                pts = sum(q["points"] for q in ex["questions"])
                if abs(pts - targets[k]) < 0.01:
                    break  # barème respecté : les points et le détail du barème concordent
                raise ValueError(f"barème à {pts:g} points au lieu de {targets[k]:g}")
            except Exception as e:
                logger.warning(f"[epreuve] exercice {k + 1}, essai {attempt} : {e} ; brut : {raw[:200]!r}")
                if time.monotonic() - started + (time.monotonic() - t0) > EXAM_DEADLINE:
                    break
        if best:  # dernier recours : barème ramené aux points visés
            pts = sum(q["points"] for q in best["questions"])
            if abs(pts - targets[k]) >= 0.01:
                for q in best["questions"]:
                    q["points"] = q["points"] * targets[k] / pts if pts > 0 else targets[k] / len(best["questions"])
        return best

    results = await asyncio.gather(*(asyncio.to_thread(one, k) for k in range(n)))
    # Épreuve incomplète refusée : étirer un exercice simple jusqu'au total donnait 9 points pour un calcul direct.
    if not all(results):
        logger.error(f"[epreuve] échec : {sum(1 for ex in results if ex)} exercice(s) obtenu(s) sur {n}")
        raise HTTPException(status_code=502, detail="WinAI n'a pas pu préparer l'épreuve. Réessayez dans un instant.")
    exercises = _clean_exam({"exercises": results}, body.total_points)
    if len(exercises) < n:
        raise HTTPException(status_code=502, detail="WinAI n'a pas pu préparer l'épreuve. Réessayez dans un instant.")
    subject = (body.subject or "").strip() or None
    title = " ".join(p for p in (EXAM_LABELS[exam_type], f"de {subject}" if subject else "",
                                 f"({body.level})" if body.level else "") if p)
    if body.topic:
        title += f" : {body.topic}"
    instructions = (f"Durée : {body.duration_minutes} minutes. L'épreuve est notée sur {body.total_points} points. "
                    "Les exercices sont indépendants et peuvent être traités dans l'ordre choisi. "
                    "La qualité de la rédaction et la rigueur des justifications seront prises en compte.")
    return {
        "success": True, "title": title[:200], "subject": subject, "instructions": instructions,
        "exam_type": exam_type, "duration_minutes": body.duration_minutes, "total_points": body.total_points,
        "exercises_requested": n, "exercises": exercises, "citations": citations,
    }


# -- Synthèses : bilan de la semaine (parent) et synthèse de classe (établissement) -------------------------

SYNTHESIS_KINDS = {
    "parent-week": (
        "Tu es WinAI, conseiller pédagogique familial de WinPlus. Écris le bilan de la semaine de {name} pour "
        "son parent, à partir des faits ci-dessous et d'eux seuls.\n"
        "Quatre courts paragraphes, sans titre ni liste : ce qui a été fait, ce qui progresse, le point "
        "d'attention s'il y en a un, puis une suggestion concrète et réaliste pour la semaine qui vient. "
        "Vouvoie le parent, appelle l'élève par son prénom, ton chaleureux et factuel, 120 à 180 mots. "
        "Tu ne connais pas le genre de l'élève : ne le déduis jamais du prénom, n'emploie ni « il » ni « elle » "
        "ni accord genré pour l'élève, répète le prénom ou tourne la phrase autrement (« votre enfant »). "
        "Si les faits montrent peu ou pas d'activité, dis-le simplement et propose comment relancer. "
        "Pas de formule d'appel ni de politesse (ni « Bonjour » ni signature), pas de tiret long : une virgule "
        "ou un deux-points à la place.",
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
        if body.kind == "parent-week":
            from services.parent_text import tidy_parent_text
            # Formule d'appel, tirets longs et apostrophes mêlées retirés ; paragraphes gardés (pas de limite de phrases).
            text = "\n\n".join(t for t in (tidy_parent_text(p, max_sentences=0) for p in text.split("\n\n")) if t)
    except Exception as e:
        logger.error(f"[synthese] {body.kind} : {e}")
        text = ""
    if len(text.split()) < 20:
        raise HTTPException(status_code=502, detail="WinAI n'a pas pu rédiger la synthèse. Réessayez dans un instant.")
    return {"success": True, "title": title.format(name=body.name.strip())[:200], "text": text}