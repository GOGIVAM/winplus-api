"""
Pont entre WinAI (routes/chatbot_routes.py) et la base de connaissance RAG.

Déclenchement hybride PUBLIC (topo validé avec l'utilisateur) :
  1. Formation actuellement consultée, déduite de `navigation_history`
     (dernière entrée envoyée par le frontend à chaque changement de page).
  2. À défaut, mention explicite d'une formation inscrite dans le message
     de l'utilisateur (correspondance sur le titre).
  3. Sinon, pas de recherche PUBLIQUE — pas de coût ni de latence ajoutée
     sur les messages qui ne portent pas sur un contenu du catalogue.

Base de connaissance PERSONNELLE (décision utilisateur : "tout document
uploadé doit servir dans la base de connaissance") : en complément du
scope public ci-dessus, TOUJOURS interroger aussi le corpus personnel de
l'utilisateur (pièces jointes de chat qu'il a lui-même envoyées, indexées
avec owner_user_id — voir RAG/router.py). Peu coûteux : ce corpus est
petit par utilisateur, et beaucoup n'y auront jamais rien indexé (Qdrant
répond alors vite, liste vide).

Contrôle d'accès aux citations (topo validé, point 5) : RAG cherche dans
TOUT le corpus PUBLIC indexé sans filtrage d'accès (un abonnement peut
donner accès à de l'information sans donner accès au document source
lui-même). Le filtrage se fait uniquement sur l'AFFICHAGE de la source :
une citation n'est marquée « visible » que si son subject_id (resp.
course_id) figure dans les formations (resp. cours) auxquels l'utilisateur
est inscrit. Les citations personnelles (owner_user_id) sont toujours
visibles pour leur propriétaire — c'est son propre contenu. WinAI reçoit
une consigne explicite de ne jamais donner de lien/référence vers une
source non visible.
"""

from __future__ import annotations

import asyncio
import logging
import re
from typing import List, Optional, Set

from RAG.query_service import retrieve_context
from RAG.shared.contracts import RAGQueryRequest, RetrievedContext
from schemas import ChatbotContextRequest

logger = logging.getLogger(__name__)

RAG_TOP_K = 5
RAG_TIMEOUT_SECONDS = 8.0
_SUBJECT_PATH_RE = re.compile(r"/subjects?/(\d+)")
_COURSE_PATH_RE = re.compile(r"/formations/(\d+)")


def _scope_from_navigation(user_context: ChatbotContextRequest) -> dict:
    """Formation (Subject) ou cours (Course) actuellement consulté par
    l'utilisateur, déduit de la dernière page visitée (ex: "/subjects/42",
    "/formations/42/play"). Retourne un filtre partiel ({} si rien trouvé)."""
    nav = getattr(user_context, "navigation_history", None) or []
    if not nav:
        return {}
    last = nav[-1]
    # Tolère PascalCase ("Path") en plus de snake_case/camelCase ("path") :
    # NavigationItemDto (.NET) sérialise ses champs en PascalCase par défaut
    # via System.Text.Json (System.Net.Http.Json ne reprend pas la politique
    # camelCase configurée pour les contrôleurs MVC), tant que ce détail de
    # casse n'a pas été confirmé par un test de bout en bout.
    path = (last.get("path") or last.get("Path") or "") if isinstance(last, dict) else ""
    subject_match = _SUBJECT_PATH_RE.search(path)
    if subject_match:
        return {"subject_id": int(subject_match.group(1))}
    course_match = _COURSE_PATH_RE.search(path)
    if course_match:
        return {"course_id": int(course_match.group(1))}
    return {}


def _scope_from_explicit_mention(user_context: ChatbotContextRequest, message: str) -> dict:
    """Repli si la navigation ne donne rien : l'utilisateur nomme
    explicitement une de ses formations inscrites dans son message."""
    enrolled = getattr(user_context, "enrolled_subjects", None) or []
    message_lower = message.lower()
    for subject in enrolled:
        title = getattr(subject, "title", None)
        subject_id = getattr(subject, "id", None)
        if title and subject_id and len(title) >= 3 and title.lower() in message_lower:
            return {"subject_id": subject_id}
    return {}


def _accessible_subject_ids(user_context: ChatbotContextRequest) -> Set[int]:
    enrolled = getattr(user_context, "enrolled_subjects", None) or []
    return {s.id for s in enrolled if getattr(s, "id", None) is not None}


def _accessible_course_ids(user_context: ChatbotContextRequest) -> Set[int]:
    enrolled = getattr(user_context, "enrolled_courses", None) or []
    return {c.id for c in enrolled if getattr(c, "id", None) is not None}


def detect_scope(user_context: Optional[ChatbotContextRequest], last_user_message: str) -> dict:
    """Retourne le filtre ({"subject_id": ...} ou {"course_id": ...}) à
    appliquer à la recherche RAG, ou {} si RAG ne doit pas être déclenché
    pour ce message (pas de formation identifiable)."""
    if not user_context:
        return {}
    return _scope_from_navigation(user_context) or _scope_from_explicit_mention(user_context, last_user_message)


def _extract_last_user_text(messages: List[dict]) -> str:
    for msg in reversed(messages or []):
        if isinstance(msg, dict) and msg.get("role") == "user":
            content = msg.get("content", "")
            if isinstance(content, list):
                return " ".join(b.get("text", "") for b in content if isinstance(b, dict) and b.get("type") == "text")
            return content or ""
    return ""


def _format_context_block(
    retrieved: RetrievedContext, accessible_subject_ids: Set[int], accessible_course_ids: Set[int]
) -> str:
    lines = []
    for i, (passage, citation) in enumerate(zip(retrieved.passages, retrieved.citations), start=1):
        subject_ok = citation.subject_id is None or citation.subject_id in accessible_subject_ids
        course_ok = citation.course_id is None or citation.course_id in accessible_course_ids
        visible = subject_ok and course_ok
        tag = f"[Source citable : {citation.title}]" if visible else "[Source NON citable — accès non détenu par l'utilisateur]"
        excerpt = passage.strip().replace("\n", " ")[:600]
        lines.append(f"{i}. {tag}\n{excerpt}")
    return (
        "\n\n[Extraits trouvés dans la base de connaissance WinPlus]\n"
        + "\n\n".join(lines)
        + "\n\nConsignes d'utilisation de ces extraits :\n"
        "- Utilise ces extraits pour répondre si et seulement si ils sont pertinents à la question.\n"
        "- Pour un extrait marqué [Source citable], tu peux nommer la formation/le document et inviter "
        "l'utilisateur à le consulter.\n"
        "- Pour un extrait marqué [Source NON citable], tu peux utiliser l'information pour répondre "
        "mais SANS jamais nommer le document, la formation ou proposer un lien vers celui-ci — "
        "présente l'information comme une connaissance générale, et si l'utilisateur veut approfondir, "
        "indique qu'une formation existe sur ce thème et qu'il peut s'y abonner pour y accéder.\n"
        "- Si aucun extrait n'est pertinent, ignore ce bloc et réponds normalement."
    )


async def _safe_retrieve(request: RAGQueryRequest, label: str) -> Optional[RetrievedContext]:
    try:
        return await asyncio.wait_for(retrieve_context(request), timeout=RAG_TIMEOUT_SECONDS)
    except asyncio.TimeoutError:
        logger.warning(f"[RAG chat bridge] Timeout ({RAG_TIMEOUT_SECONDS}s) dépassé sur la recherche {label}")
        return None
    except Exception as e:
        logger.warning(f"[RAG chat bridge] Recherche {label} échouée, poursuite sans : {e}")
        return None


async def build_rag_context_block(
    user_context: Optional[ChatbotContextRequest],
    messages: List[dict],
    user_id: Optional[int] = None,
) -> str:
    """Retourne un bloc de texte à ajouter au prompt système WinAI, ou une
    chaîne vide si RAG n'a rien trouvé de pertinent (ni côté public, ni
    côté personnel). Ne lève jamais d'exception : un souci RAG ne doit
    jamais empêcher WinAI de répondre normalement (dégradation silencieuse
    par source — un échec sur l'une n'empêche pas l'autre)."""
    last_message = _extract_last_user_text(messages)
    if not last_message:
        return ""

    passages: List[str] = []
    citations = []

    scope = detect_scope(user_context, last_message)
    if scope:
        public_request = RAGQueryRequest(
            question=last_message, filters={**scope, "status": "active"}, top_k=RAG_TOP_K
        )
        public_result = await _safe_retrieve(public_request, "publique (catalogue)")
        if public_result and not public_result.refused:
            passages.extend(public_result.passages)
            citations.extend(public_result.citations)

    if user_id is not None:
        personal_request = RAGQueryRequest(
            question=last_message, filters={"owner_user_id": user_id, "status": "active"}, top_k=RAG_TOP_K
        )
        personal_result = await _safe_retrieve(personal_request, "personnelle")
        if personal_result and not personal_result.refused:
            passages.extend(personal_result.passages)
            citations.extend(personal_result.citations)

    if not passages:
        return ""

    accessible_subject_ids = _accessible_subject_ids(user_context)
    accessible_course_ids = _accessible_course_ids(user_context)
    merged = RetrievedContext(passages=passages, citations=citations, refused=False)
    return _format_context_block(merged, accessible_subject_ids, accessible_course_ids)
