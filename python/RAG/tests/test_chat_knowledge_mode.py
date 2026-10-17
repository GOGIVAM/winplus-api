"""Lot 9.7b (décision 18.21) : mode « base de connaissance » du chat.

- désactivé par défaut sur /chat et /stream ;
- quand il est actif, le coût réel de la recherche (embedding, rerank,
  reformulation) est renvoyé par build_rag_context_block pour être imputé ;
- la boucle Self-RAG est limitée à une reformulation.
"""

import asyncio

import pytest

from RAG.shared import cost
from RAG.shared.contracts import RAGQueryRequest, RetrievedContext


def test_knowledge_base_disabled_by_default():
    from schemas import ChatRequest

    assert ChatRequest.model_fields["knowledge_base"].default is False


def test_stream_body_knowledge_base_disabled_by_default(monkeypatch):
    # auth.py refuse de s'importer sans secret : valeur factice pour le test.
    monkeypatch.setenv("JWT_SECRET_KEY", "test-only-not-a-secret")
    from routes.chatbot_routes import StreamChatBody

    assert StreamChatBody.model_fields["knowledge_base"].default is False


def test_chat_iterations_capped_to_one_reformulation():
    assert cost.CHAT_MAX_ITERATIONS == 2


def test_search_cost_estimate_includes_rerank_and_embedding():
    q = "Explique le théorème de Pythagore"
    assert cost.search_cost_estimate(q) == cost.embedding_cost_tokens(q) + cost.RERANK_TOKEN_EQUIVALENT
    assert cost.search_cost_estimate(q) > cost.RERANK_TOKEN_EQUIVALENT


@pytest.fixture
def bridge(monkeypatch):
    from services import rag_chat_bridge

    calls = []

    async def fake_retrieve(request: RAGQueryRequest):
        calls.append(request)
        return RetrievedContext(passages=[], citations=[], refused=False, cost_tokens=1234)

    monkeypatch.setattr(rag_chat_bridge, "retrieve_context", fake_retrieve)
    return rag_chat_bridge, calls


def test_bridge_returns_engine_cost_and_caps_iterations(bridge):
    module, calls = bridge
    messages = [{"role": "user", "content": "Qu'est-ce qu'une fonction affine ?"}]
    block, spent = asyncio.run(module.build_rag_context_block(None, messages, user_id=30))
    assert block == ""
    # Recherche personnelle seulement (pas de périmètre public détecté).
    assert spent == 1234 * len(calls)
    assert spent > 0
    assert all(c.max_iterations == cost.CHAT_MAX_ITERATIONS for c in calls)


def test_bridge_estimates_cost_when_search_fails(monkeypatch):
    from services import rag_chat_bridge

    async def failing(request):
        raise RuntimeError("boom")

    monkeypatch.setattr(rag_chat_bridge, "retrieve_context", failing)
    messages = [{"role": "user", "content": "Bonjour"}]
    block, spent = asyncio.run(rag_chat_bridge.build_rag_context_block(None, messages, user_id=30))
    assert block == ""
    assert spent == cost.search_cost_estimate("Bonjour")
