"""
Point d'accès RAG pour les modules internes (ex: routes/chatbot_routes.py)
qui doivent interroger la base de connaissance sans passer par le routeur
HTTP RAG/router.py. Respecte la même bascule RAG_BACKEND que le routeur.

run_query()/retrieve_passages() des deux moteurs sont synchrones et
bloquants (appels réseau Cohere/DeepSeek ou inférence locale) : les
appeler directement depuis une route FastAPI async bloquerait l'event
loop et retarderait TOUTES les requêtes en cours, pas seulement celle qui
utilise RAG — d'où le passage par un threadpool ici.
"""

from __future__ import annotations

import logging

from starlette.concurrency import run_in_threadpool

from RAG.shared.config import RAG_BACKEND
from RAG.shared.contracts import RAGAnswer, RAGQueryRequest, RetrievedContext

logger = logging.getLogger(__name__)


def _active_retrieve_passages():
    if RAG_BACKEND == "self_hosted":
        from RAG.self_hosted.engine.pipeline import retrieve_passages

        return retrieve_passages
    from RAG.api.engine.pipeline import retrieve_passages

    return retrieve_passages


def _active_run_query():
    if RAG_BACKEND == "self_hosted":
        from RAG.self_hosted.engine.pipeline import run_query

        return run_query
    from RAG.api.engine.pipeline import run_query

    return run_query


async def retrieve_context(request: RAGQueryRequest) -> RetrievedContext:
    """Récupération + rerank seuls (pas de génération) — c'est la fonction
    à utiliser pour enrichir un autre prompt (ex: WinAI) plutôt que
    produire une réponse RAG autonome."""
    fn = _active_retrieve_passages()
    return await run_in_threadpool(fn, request)


async def query_rag(request: RAGQueryRequest) -> RAGAnswer:
    """Pipeline RAG complet (retrieval + génération + validation
    anti-hallucination) — utilisé par RAG/router.py pour /rag/query."""
    fn = _active_run_query()
    return await run_in_threadpool(fn, request)
