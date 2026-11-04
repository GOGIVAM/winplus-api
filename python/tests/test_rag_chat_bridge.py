"""Pont chat WinAI / base de connaissance (services/rag_chat_bridge.py) : périmètre déduit de la
navigation ou du message, visibilité des citations, coût de recherche, tolérance aux pannes RAG.
Le moteur de recherche est remplacé par un simulacre : ni Qdrant ni fournisseur d'embeddings."""

import asyncio
import os

os.environ.setdefault("JWT_SECRET_KEY", "test-secret-key-for-rag-bridge-tests-0123456789")

import pytest  # noqa: E402

from RAG.shared.contracts import Citation, RAGQueryRequest, RetrievedContext  # noqa: E402
from RAG.shared.cost import CHAT_MAX_ITERATIONS, search_cost_estimate  # noqa: E402
from schemas import ChatbotContextRequest  # noqa: E402
from services import rag_chat_bridge as rb  # noqa: E402


def _ctx(**kw):
    return ChatbotContextRequest(**kw)


def _cite(doc="d1", title="Cours de maths", subject_id=None, course_id=None):
    return Citation(doc_id=doc, title=title, chunk_id=doc + "-c", score=0.9, subject_id=subject_id, course_id=course_id)


# ── périmètre ───────────────────────────────────────────────────────────────

@pytest.mark.parametrize("path,expected", [
    ("/subjects/42", {"subject_id": 42}),
    ("/subject/7/lesson", {"subject_id": 7}),
    ("/formations/9/play", {"course_id": 9}),
    ("/accueil", {}),
    ("", {}),
])
def test_scope_from_navigation_paths(path, expected):
    assert rb._scope_from_navigation(_ctx(navigation_history=[{"path": path}])) == expected


def test_scope_from_navigation_uses_last_entry_and_tolerates_pascal_case():
    ctx = _ctx(navigation_history=[{"path": "/subjects/1"}, {"Path": "/formations/5"}])
    assert rb._scope_from_navigation(ctx) == {"course_id": 5}


def test_scope_from_navigation_without_history_or_with_bad_entry():
    assert rb._scope_from_navigation(_ctx()) == {}
    assert rb._scope_from_navigation(_ctx(navigation_history=[{"path": "/subjects/3"}, {"autre": 1}])) == {}


def test_subject_wins_over_course_when_both_in_path():
    assert rb._scope_from_navigation(_ctx(navigation_history=[{"path": "/subjects/2/formations/3"}])) == {"subject_id": 2}


def test_explicit_mention_matches_enrolled_title_case_insensitively():
    ctx = _ctx(enrolled_subjects=[{"id": 4, "title": "Physique"}, {"id": 5, "title": "Chimie"}])
    assert rb._scope_from_explicit_mention(ctx, "Aide-moi en CHIMIE svp") == {"subject_id": 5}
    assert rb._scope_from_explicit_mention(ctx, "Bonjour") == {}


def test_explicit_mention_ignores_too_short_titles_and_zero_ids():
    ctx = _ctx(enrolled_subjects=[{"id": 1, "title": "SV"}, {"id": 0, "title": "Histoire"}])
    assert rb._scope_from_explicit_mention(ctx, "sv et histoire") == {}


def test_detect_scope_prefers_navigation_and_needs_a_context():
    ctx = _ctx(navigation_history=[{"path": "/subjects/8"}], enrolled_subjects=[{"id": 4, "title": "Physique"}])
    assert rb.detect_scope(ctx, "parle-moi de physique") == {"subject_id": 8}
    assert rb.detect_scope(None, "physique") == {}
    assert rb.detect_scope(_ctx(), "physique") == {}


def test_accessible_ids():
    ctx = _ctx(enrolled_subjects=[{"id": 1, "title": "A"}, {"SubjectId": 2, "Title": "B"}],
               enrolled_courses=[{"id": 9, "title": "C"}])
    assert rb._accessible_subject_ids(ctx) == {1, 2}
    assert rb._accessible_course_ids(ctx) == {9}
    assert rb._accessible_subject_ids(_ctx()) == set()


# ── dernier message utilisateur ─────────────────────────────────────────────

def test_extract_last_user_text():
    assert rb._extract_last_user_text([]) == ""
    assert rb._extract_last_user_text(None) == ""
    messages = [{"role": "user", "content": "premier"}, {"role": "assistant", "content": "rép"},
                {"role": "user", "content": "dernier"}, {"role": "assistant", "content": "x"}]
    assert rb._extract_last_user_text(messages) == "dernier"


def test_extract_last_user_text_from_multimodal_blocks():
    content = [{"type": "text", "text": "Que vois-tu ?"}, {"type": "image_url", "image_url": {}},
               {"type": "text", "text": "Merci"}]
    assert rb._extract_last_user_text([{"role": "user", "content": content}]) == "Que vois-tu ? Merci"


# ── bloc de contexte et visibilité ──────────────────────────────────────────

def test_format_block_marks_visible_and_hidden_sources():
    retrieved = RetrievedContext(
        passages=["Extrait public", "Extrait privé"],
        citations=[_cite("a", "Maths 3e", subject_id=1), _cite("b", "Physique Pro", subject_id=2)])
    block = rb._format_context_block(retrieved, {1}, set())
    assert "1. [Source citable : Maths 3e]\nExtrait public" in block
    assert "2. [Source NON citable" in block and "Physique Pro" not in block
    assert block.startswith("\n\n[Extraits trouvés dans la base de connaissance WinPlus]")
    assert "Consignes d'utilisation" in block


def test_format_block_personal_source_without_ids_is_visible():
    retrieved = RetrievedContext(passages=["Mon devoir"], citations=[_cite("p", "devoir.pdf")])
    assert "[Source citable : devoir.pdf]" in rb._format_context_block(retrieved, set(), set())


def test_format_block_course_access_is_checked_independently():
    retrieved = RetrievedContext(passages=["x"], citations=[_cite("c", "Leçon", course_id=3)])
    assert "[Source citable" in rb._format_context_block(retrieved, set(), {3})
    assert "NON citable" in rb._format_context_block(retrieved, set(), {4})


def test_format_block_flattens_newlines_and_truncates_excerpt():
    # extrait = "ligne1 ligne2 " (14 caractères) puis des z : coupé à 600 caractères au total
    retrieved = RetrievedContext(passages=["ligne1\nligne2\n" + "z" * 1000], citations=[_cite()])
    block = rb._format_context_block(retrieved, set(), set())
    assert "ligne1 ligne2 " in block
    assert "z" * 586 in block and "z" * 587 not in block


# ── _safe_retrieve ──────────────────────────────────────────────────────────

def _request(question="Qu'est-ce qu'un vecteur ?"):
    return RAGQueryRequest(question=question)


def test_safe_retrieve_returns_engine_cost(monkeypatch):
    async def fake(request):
        return RetrievedContext(cost_tokens=500)

    monkeypatch.setattr(rb, "retrieve_context", fake)
    result, cost = asyncio.run(rb._safe_retrieve(_request(), "test"))
    assert result.cost_tokens == 500 and cost == 500


def test_safe_retrieve_estimates_cost_when_engine_reports_none(monkeypatch):
    async def fake(request):
        return RetrievedContext(cost_tokens=0)

    monkeypatch.setattr(rb, "retrieve_context", fake)
    request = _request()
    _, cost = asyncio.run(rb._safe_retrieve(request, "test"))
    assert cost == search_cost_estimate(request.question)


def test_safe_retrieve_timeout_returns_none_with_estimated_cost(monkeypatch):
    async def slow(request):
        await asyncio.sleep(5)

    monkeypatch.setattr(rb, "retrieve_context", slow)
    monkeypatch.setattr(rb, "RAG_TIMEOUT_SECONDS", 0.01)
    request = _request()
    assert asyncio.run(rb._safe_retrieve(request, "test")) == (None, search_cost_estimate(request.question))


def test_safe_retrieve_engine_error_is_absorbed(monkeypatch):
    async def broken(request):
        raise RuntimeError("Qdrant injoignable")

    monkeypatch.setattr(rb, "retrieve_context", broken)
    request = _request()
    assert asyncio.run(rb._safe_retrieve(request, "test")) == (None, search_cost_estimate(request.question))


# ── build_rag_context_block ─────────────────────────────────────────────────

@pytest.fixture
def engine(monkeypatch):
    calls, results = [], []

    async def fake(request):
        calls.append(request)
        return results.pop(0) if results else RetrievedContext()

    monkeypatch.setattr(rb, "retrieve_context", fake)
    return calls, results


def test_build_block_without_user_message_does_nothing(engine):
    calls, _ = engine
    assert asyncio.run(rb.build_rag_context_block(None, [{"role": "assistant", "content": "x"}], 1)) == ("", 0)
    assert calls == []


def test_build_block_without_scope_or_user_does_not_search(engine):
    calls, _ = engine
    assert asyncio.run(rb.build_rag_context_block(_ctx(), [{"role": "user", "content": "salut"}], None)) == ("", 0)
    assert calls == []


def test_build_block_queries_public_scope_then_personal_corpus(engine):
    calls, results = engine
    results += [
        RetrievedContext(passages=["Public"], citations=[_cite("a", "Maths", subject_id=42)], cost_tokens=100),
        RetrievedContext(passages=["Perso"], citations=[_cite("b", "devoir.pdf")], cost_tokens=50),
    ]
    ctx = _ctx(navigation_history=[{"path": "/subjects/42"}], enrolled_subjects=[{"id": 42, "title": "Maths"}])
    block, cost = asyncio.run(rb.build_rag_context_block(ctx, [{"role": "user", "content": "Explique"}], user_id=30))

    assert cost == 150
    assert calls[0].filters == {"subject_id": 42, "status": "active"}
    assert calls[1].filters == {"owner_user_id": 30, "status": "active"}
    assert all(c.top_k == rb.RAG_TOP_K and c.max_iterations == CHAT_MAX_ITERATIONS for c in calls)
    assert "[Source citable : Maths]" in block and "[Source citable : devoir.pdf]" in block


def test_build_block_hides_sources_the_user_cannot_access(engine):
    _, results = engine
    results.append(RetrievedContext(passages=["Secret"], citations=[_cite("a", "Cours payant", subject_id=99)],
                                    cost_tokens=10))
    ctx = _ctx(navigation_history=[{"path": "/subjects/99"}], enrolled_subjects=[{"id": 1, "title": "Autre"}])
    block, _ = asyncio.run(rb.build_rag_context_block(ctx, [{"role": "user", "content": "Explique"}], None))
    assert "NON citable" in block and "Cours payant" not in block


def test_build_block_skips_refused_results_but_still_charges_cost(engine):
    _, results = engine
    results.append(RetrievedContext(refused=True, passages=["x"], citations=[_cite()], cost_tokens=77))
    block, cost = asyncio.run(rb.build_rag_context_block(_ctx(), [{"role": "user", "content": "q"}], user_id=1))
    assert block == "" and cost == 77


def test_build_block_never_raises_when_the_engine_fails(monkeypatch):
    async def broken(request):
        raise RuntimeError("panne")

    monkeypatch.setattr(rb, "retrieve_context", broken)
    block, cost = asyncio.run(rb.build_rag_context_block(_ctx(), [{"role": "user", "content": "q"}], user_id=1))
    assert block == "" and cost == search_cost_estimate("q")
