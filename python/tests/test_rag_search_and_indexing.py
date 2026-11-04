"""Base de connaissance RAG, moteur « api » : recherche hybride (embeddings simulés, Qdrant simulé), reclassement,
boucle de reformulation, citations, refus ; indexation ; score de pertinence composite ; fonctions de bas niveau
de la base vectorielle. Aucun appel à Qdrant, Cohere, DeepSeek ou au réseau."""

import math
from types import SimpleNamespace

import pytest

import RAG.api.engine.pipeline as eng
import RAG.api.indexing.pipeline as idx
import RAG.shared.relevance_scoring as rs
import RAG.shared.vector_store as vs
from RAG.api import config
from RAG.shared.contracts import Chunk, ChunkMetadata, RAGQueryRequest
from RAG.shared.cost import RERANK_TOKEN_EQUIVALENT, embedding_cost_tokens, reformulation_cost_tokens


# ═══════════════════════════ moteur : recherche hybride ═══════════════════════════

def hit(chunk_id, **payload):
    return SimpleNamespace(id=chunk_id, payload=payload)


class FakeBM25:
    def __init__(self, ids):
        self.ids, self.calls = ids, []

    def search(self, query, top_k=20):
        self.calls.append((query, top_k))
        return self.ids


def test_hybrid_retrieve_fuses_dense_and_keyword_rankings_and_keeps_active_chunks_only(monkeypatch):
    dense_calls = []
    monkeypatch.setattr(eng, "search_dense", lambda collection, vector, top_k, filters: dense_calls.append(
        (collection, vector, top_k, filters)) or [hit("a"), hit("b"), hit("c")])
    bm25 = FakeBM25(["c", "d"])
    monkeypatch.setattr(eng.BM25Registry, "get", classmethod(lambda cls, collection: bm25))
    records = [hit("a", text="A"), hit("b", text="B", status="superseded"), hit("c", text="C", status="active")]
    fetched = []
    monkeypatch.setattr(eng, "get_by_ids", lambda collection, ids: fetched.append(ids) or records)

    result = eng._hybrid_retrieve([0.1, 0.2], "ma question", {"subject_id": 3}, top_k=2)

    # c est cité par les deux listes : premier ; a, b, d suivent (b puis d à égalité) ; d n'est pas retrouvé en base
    assert fetched == [["c", "a", "b", "d"]]
    assert [r["id"] for r in result] == ["c", "a"]                           # b est « superseded »
    assert dense_calls == [(config.QDRANT_COLLECTION, [0.1, 0.2], 4, {"subject_id": 3})]
    assert bm25.calls == [("ma question", 4)]


def test_hybrid_retrieve_without_any_hit_returns_nothing(monkeypatch):
    monkeypatch.setattr(eng, "search_dense", lambda *a, **k: [])
    monkeypatch.setattr(eng.BM25Registry, "get", classmethod(lambda cls, collection: FakeBM25([])))
    monkeypatch.setattr(eng, "get_by_ids", lambda collection, ids: [])
    assert eng._hybrid_retrieve([0.0], "q", {}, 5) == []


def test_relevance_boost_reorders_without_changing_the_candidate_set():
    candidates = [{"payload": {"relevance_score": 0.0}}, {"payload": {"relevance_score": 1.0}}, {"payload": {}}]
    boosted = eng._apply_relevance_boost(candidates, [(0, 0.8), (1, 0.8), (2, 0.9)])
    assert boosted == [(2, 0.9), (1, 0.8), (0, 0.4)]      # score nul : moitié ; inconnu : neutre ; 1 : inchangé


def test_relevance_boost_of_an_empty_ranking():
    assert eng._apply_relevance_boost([], []) == []


# ═══════════════════════════ moteur : boucle de recherche ═══════════════════════════

@pytest.fixture
def pipeline(monkeypatch):
    state = SimpleNamespace(candidates=[{"id": "a", "payload": {"text": "Texte A", "doc_id": "d1", "title": "Cours",
                                                                 "page": 4, "section": "S1", "subject_id": 7}},
                                        {"id": "b", "payload": {"text": "Texte B", "doc_id": "d2", "title": "Annale"}}],
                            rerank_answers=[[(0, 0.9)]], rerank_calls=[], embed_calls=[], reformulations=[],
                            retrieve_calls=[], answer="Réponse générée.", validation=None, generated=[])

    monkeypatch.setattr(eng, "embed_query", lambda text: state.embed_calls.append(text) or [float(len(text))])

    def retrieve(vector, text, filters, top_k):
        state.retrieve_calls.append((vector, text, filters, top_k))
        return list(state.candidates)

    monkeypatch.setattr(eng, "_hybrid_retrieve", retrieve)

    def rerank(question, texts, top_k):
        state.rerank_calls.append((question, list(texts), top_k))
        answers = state.rerank_answers
        return answers.pop(0) if len(answers) > 1 else answers[0]

    monkeypatch.setattr(eng, "rerank", rerank)
    monkeypatch.setattr(eng, "reformulate_query", lambda q: state.reformulations.append(q) or "question reformulée")
    monkeypatch.setattr(eng, "generate_answer", lambda q, passages: state.generated.append((q, passages)) or state.answer)
    state.validation = SimpleNamespace(passed=True, faithfulness=0.95, answer_relevancy=0.9)
    monkeypatch.setattr(eng, "validate_answer", lambda q, a, p: state.validation)
    return state


REQUEST = RAGQueryRequest(question="Qu'est-ce qu'une dérivée ?", filters={"status": "active"}, top_k=3)


def test_confident_rerank_stops_after_one_pass_and_costs_one_embedding_and_one_rerank(pipeline):
    candidates, reranked, cost = eng._retrieve_and_rerank(REQUEST)
    assert reranked == [(0, 0.9)] and len(candidates) == 2
    assert cost == embedding_cost_tokens(REQUEST.question) + RERANK_TOKEN_EQUIVALENT
    assert pipeline.reformulations == [] and len(pipeline.rerank_calls) == 1
    assert pipeline.rerank_calls[0] == (REQUEST.question, ["Texte A", "Texte B"], 3)
    assert pipeline.retrieve_calls[0][2:] == ({"status": "active"}, 3)


def test_low_confidence_triggers_one_reformulation_per_extra_iteration(pipeline):
    pipeline.rerank_answers = [[(0, 0.05)]]
    request = REQUEST.model_copy(update={"max_iterations": 2})
    _, reranked, cost = eng._retrieve_and_rerank(request)
    assert pipeline.reformulations == [REQUEST.question] and len(pipeline.rerank_calls) == 2
    assert pipeline.embed_calls == [REQUEST.question, "question reformulée"]
    assert pipeline.retrieve_calls[1][1] == "question reformulée"
    assert cost == (embedding_cost_tokens(REQUEST.question) + 2 * RERANK_TOKEN_EQUIVALENT
                    + reformulation_cost_tokens(REQUEST.question, "question reformulée")
                    + embedding_cost_tokens("question reformulée"))
    assert reranked == [(0, 0.05)]


def test_reformulation_can_rescue_a_weak_first_pass(pipeline):
    pipeline.rerank_answers = [[(0, 0.05)], [(1, 0.95)]]
    _, reranked, _ = eng._retrieve_and_rerank(REQUEST.model_copy(update={"max_iterations": 3}))
    assert reranked == [(1, 0.95)] and len(pipeline.rerank_calls) == 2


def test_a_single_iteration_never_reformulates(pipeline):
    pipeline.rerank_answers = [[(0, 0.01)]]
    eng._retrieve_and_rerank(REQUEST.model_copy(update={"max_iterations": 1}))
    assert pipeline.reformulations == [] and len(pipeline.rerank_calls) == 1


def test_no_candidate_means_no_rerank_and_an_empty_result(pipeline):
    pipeline.candidates = []
    candidates, reranked, cost = eng._retrieve_and_rerank(REQUEST)
    assert candidates == [] and reranked == [] and pipeline.rerank_calls == []
    assert cost == embedding_cost_tokens(REQUEST.question)


def test_relevance_score_of_a_chunk_weighs_on_the_final_ranking(pipeline):
    pipeline.candidates[0]["payload"]["relevance_score"] = 0.0
    pipeline.candidates[1]["payload"]["relevance_score"] = 1.0
    pipeline.rerank_answers = [[(0, 0.95), (1, 0.9)]]
    _, reranked, _ = eng._retrieve_and_rerank(REQUEST)
    assert [i for i, _ in reranked] == [1, 0]


# ═══════════════════════════ moteur : réponses ═══════════════════════════

def test_citations_follow_the_reranked_order(pipeline):
    selected, passages, citations = eng._to_citations(pipeline.candidates, [(1, 0.8), (0, 0.6)])
    assert passages == ["Texte B", "Texte A"] and [c.doc_id for c in citations] == ["d2", "d1"]
    assert citations[1].page == 4 and citations[1].section == "S1" and citations[1].subject_id == 7
    assert citations[0].chunk_id == "b" and citations[0].score == 0.8 and citations[0].page is None


def test_retrieve_passages_returns_context_without_generating(pipeline):
    pipeline.rerank_answers = [[(1, 0.8), (0, 0.6)]]
    context = eng.retrieve_passages(REQUEST)
    assert context.refused is False and context.passages == ["Texte B", "Texte A"]
    assert len(context.citations) == 2 and context.cost_tokens > 0
    assert pipeline.generated == []                           # ni génération ni validation


def test_retrieve_passages_refuses_when_nothing_is_found_but_still_reports_the_cost(pipeline):
    pipeline.candidates = []
    context = eng.retrieve_passages(REQUEST)
    assert context.refused is True and context.passages == [] and context.cost_tokens == embedding_cost_tokens(REQUEST.question)


def test_run_query_returns_the_generated_answer_with_its_citations_and_scores(pipeline):
    answer = eng.run_query(REQUEST)
    assert answer.refused is False and answer.answer == "Réponse générée." and answer.backend == "api"
    assert answer.faithfulness == 0.95 and answer.answer_relevancy == 0.9 and len(answer.citations) == 1
    assert pipeline.generated == [(REQUEST.question, ["Texte A"])]


def test_run_query_refuses_when_nothing_is_retrieved(pipeline):
    from RAG.shared.config import REFUSAL_MESSAGE
    pipeline.candidates = []
    answer = eng.run_query(REQUEST)
    assert answer.refused is True and answer.answer == REFUSAL_MESSAGE and answer.citations == []
    assert pipeline.generated == []


def test_run_query_replaces_an_unfaithful_answer_by_the_refusal_message(pipeline):
    from RAG.shared.config import REFUSAL_MESSAGE
    pipeline.validation = SimpleNamespace(passed=False, faithfulness=0.4, answer_relevancy=0.7)
    answer = eng.run_query(REQUEST)
    assert answer.refused is True and answer.answer == REFUSAL_MESSAGE and "générée" not in answer.answer
    assert answer.faithfulness == 0.4 and len(answer.citations) == 1


# ═══════════════════════════ indexation ═══════════════════════════

def make_chunks(n=3, **meta):
    return [Chunk(chunk_id=f"c{i}", text=f"Texte {i}", metadata=ChunkMetadata(doc_id="doc1", title="Cours", **meta))
            for i in range(n)]


@pytest.fixture
def indexing(monkeypatch):
    state = SimpleNamespace(ensured=[], superseded=[], upserts=[], scoring=[], embedded=[], bm25_added=[], persisted=[],
                            score=(0.8, {"topic_fit": 0.9}))
    monkeypatch.setattr(idx, "ensure_collection", lambda collection, dim: state.ensured.append((collection, dim)))
    monkeypatch.setattr(idx, "mark_superseded", lambda c, old, new: state.superseded.append((c, old, new)))
    monkeypatch.setattr(idx, "upsert_chunks", lambda c, chunks, vectors: state.upserts.append((c, chunks, vectors)))
    monkeypatch.setattr(idx, "resolve_topic_label", lambda subject_id, category: f"label:{category}")
    monkeypatch.setattr(idx, "embed_texts", lambda texts: state.embedded.append(texts) or [[float(i)] for i, _ in enumerate(texts)])

    def score(**kwargs):
        state.scoring.append(kwargs)
        return state.score

    monkeypatch.setattr(idx, "compute_relevance_score", score)

    class Bm25:
        def add_many(self, items):
            state.bm25_added.append(items)

    monkeypatch.setattr(idx.BM25Registry, "get", classmethod(lambda cls, c: Bm25()))
    monkeypatch.setattr(idx.BM25Registry, "persist", classmethod(lambda cls, c: state.persisted.append(c)))
    return state


@pytest.mark.parametrize("meta,expected", [
    ({}, {"status": "active"}),
    ({"subject_id": 3}, {"status": "active", "subject_id": 3}),
    ({"course_id": 4, "owner_user_id": 9}, {"status": "active", "course_id": 4, "owner_user_id": 9}),
    ({"subject_id": 3, "course_id": 4, "owner_user_id": 9},
     {"status": "active", "subject_id": 3, "course_id": 4, "owner_user_id": 9}),
])
def test_scope_filters_only_contain_the_known_scopes(meta, expected):
    assert idx._scope_filters(ChunkMetadata(doc_id="d", title="t", **meta)) == expected


def test_indexing_nothing_does_nothing(indexing):
    assert idx.index_chunks([]) == []
    assert indexing.ensured == [] and indexing.upserts == [] and indexing.persisted == []


def test_indexing_stores_vectors_scores_and_keyword_index(indexing):
    chunks = make_chunks(3, category="epreuve", subject_id=5)
    warnings = idx.index_chunks(chunks)
    assert warnings == [] and indexing.ensured == [(config.QDRANT_COLLECTION, config.EMBEDDING_DIM)]
    assert all(c.metadata.relevance_score == 0.8 for c in chunks)
    assert indexing.upserts[0][0] == config.QDRANT_COLLECTION and indexing.upserts[0][2] == [[0.0], [1.0], [2.0]]
    assert indexing.bm25_added == [[("c0", "Texte 0"), ("c1", "Texte 1"), ("c2", "Texte 2")]]
    assert indexing.persisted == [config.QDRANT_COLLECTION]
    scoring = indexing.scoring[0]
    assert scoring["topic_label"] == "label:epreuve" and scoring["filters"] == {"status": "active", "subject_id": 5}
    assert scoring["chunks_text"] == ["Texte 0", "Texte 1", "Texte 2"]


def test_indexing_a_new_version_supersedes_the_previous_one(indexing):
    idx.index_chunks(make_chunks(1), previous_doc_id="doc0")
    assert indexing.superseded == [(config.QDRANT_COLLECTION, "doc0", "doc1")]
    indexing.superseded.clear()
    idx.index_chunks(make_chunks(1))
    assert indexing.superseded == []


@pytest.mark.parametrize("topic_fit,warns", [(0.29, True), (0.0, True), (0.3, False), (0.9, False), (None, False)])
def test_low_topic_fit_is_reported_as_a_warning_but_never_blocks_indexing(indexing, topic_fit, warns):
    indexing.score = (0.2, {"topic_fit": topic_fit})
    warnings = idx.index_chunks(make_chunks(2, category="maths"))
    assert bool(warnings) is warns and len(indexing.upserts) == 1
    if warns:
        assert f"{topic_fit:.2f}" in warnings[0] and "'label:maths'" in warnings[0]


# ═══════════════════════════ score de pertinence ═══════════════════════════

@pytest.mark.parametrize("a,b,expected", [
    ([1, 0], [1, 0], 1.0), ([1, 0], [0, 1], 0.0), ([1, 0], [-1, 0], -1.0), ([0, 0], [1, 1], 0.0), ([1, 1], [2, 2], 1.0)])
def test_cosine_similarity(a, b, expected):
    assert rs._cosine(a, b) == pytest.approx(expected)


def test_topic_label_prefers_the_category(monkeypatch):
    assert rs.resolve_topic_label(3, "Physique") == "Physique" and rs.resolve_topic_label(None, None) is None


def test_topic_label_falls_back_on_the_subject_title(sqlite_database):
    import database as dbm
    db = sqlite_database(dbm, [dbm.Subject])
    db.add(dbm.Subject(Id=3, Title="Chimie organique"))
    assert rs.resolve_topic_label(3, None) == "Chimie organique"
    assert rs.resolve_topic_label(99, "") is None


def test_topic_label_survives_a_database_failure(monkeypatch):
    import database as dbm
    monkeypatch.setattr(dbm, "Database", lambda: (_ for _ in ()).throw(RuntimeError("base HS")))
    assert rs.resolve_topic_label(3, None) is None


class JudgeClient:
    def __init__(self, content=None, success=True, error=None):
        self.content, self.success, self.error, self.calls = content, success, error, []

    def chat(self, **kw):
        self.calls.append(kw)
        if self.error:
            raise self.error
        return {"success": self.success, "content": self.content}


def _judge(monkeypatch, **kw):
    client = JudgeClient(**kw)
    monkeypatch.setattr("services.deepseek_client.get_deepseek_client", lambda: client)
    return client


@pytest.mark.parametrize("content,expected", [("0.85", 0.85), ("0.7 très bon", 0.7), ("1.7", 1.0), ("-3", 0.0), ("0", 0.0)])
def test_quality_score_is_the_first_number_clamped_to_zero_one(monkeypatch, content, expected):
    _judge(monkeypatch, content=content)
    assert rs._quality_score_llm("extrait") == expected


@pytest.mark.parametrize("kwargs", [{"content": "bon", "success": True}, {"content": "0.5", "success": False},
                                    {"content": "", "success": True}, {"error": TimeoutError("lent")}])
def test_quality_score_is_none_when_the_judge_is_unusable(monkeypatch, kwargs):
    _judge(monkeypatch, **kwargs)
    assert rs._quality_score_llm("extrait") is None


def test_quality_judge_receives_a_truncated_sample_at_temperature_zero(monkeypatch):
    client = _judge(monkeypatch, content="0.5")
    rs._quality_score_llm("x" * 5000)
    call = client.calls[0]
    assert len(call["messages"][0]["content"]) == 2000 and call["temperature"] == 0.0 and call["max_tokens"] == 10


def test_novelty_is_one_when_the_scope_is_empty_and_inverse_of_similarity_otherwise(monkeypatch):
    searches = []
    monkeypatch.setattr(vs, "search_dense", lambda c, v, top_k, filters: searches.append((c, top_k, filters)) or [])
    assert rs._novelty_score([1.0], "coll", {"status": "active"}) == 1.0 and searches == [("coll", 1, {"status": "active"})]
    monkeypatch.setattr(vs, "search_dense", lambda *a, **k: [SimpleNamespace(score=0.9)])
    assert rs._novelty_score([1.0], "coll", {}) == pytest.approx(0.1)
    monkeypatch.setattr(vs, "search_dense", lambda *a, **k: [SimpleNamespace(score=1.3)])
    assert rs._novelty_score([1.0], "coll", {}) == 0.0


def test_novelty_is_none_when_the_collection_does_not_exist_yet(monkeypatch):
    monkeypatch.setattr(vs, "search_dense", lambda *a, **k: (_ for _ in ()).throw(RuntimeError("collection absente")))
    assert rs._novelty_score([1.0], "coll", {}) is None


def test_topic_fit_is_the_cosine_with_the_label_embedding_and_never_negative():
    assert rs._topic_fit_score([1.0, 0.0], "Maths", lambda t: [[1.0, 0.0]]) == pytest.approx(1.0)
    assert rs._topic_fit_score([1.0, 0.0], "Maths", lambda t: [[-1.0, 0.0]]) == 0.0
    assert rs._topic_fit_score([1.0, 0.0], "Maths", lambda t: (_ for _ in ()).throw(RuntimeError("HS"))) is None


def _score_env(monkeypatch, novelty_hits, quality, embed=None):
    monkeypatch.setattr(vs, "search_dense", lambda *a, **k: novelty_hits)
    _judge(monkeypatch, content=quality) if quality is not None else _judge(monkeypatch, error=TimeoutError("HS"))
    embedded = []

    def embed_fn(texts):
        embedded.append(list(texts))
        if embed:
            return embed(texts)
        return [[1.0, 0.0] for _ in texts]

    return embed_fn, embedded


def test_composite_score_combines_novelty_quality_and_topic_fit(monkeypatch):
    embed_fn, embedded = _score_env(monkeypatch, [SimpleNamespace(score=0.2)], "0.6")
    score, detail = rs.compute_relevance_score(["A", "B", "C", "D"], embed_fn, "coll", {}, topic_label="Maths")
    assert score == pytest.approx(0.7) and detail["novelty"] == pytest.approx(0.8) and detail["quality"] == 0.6
    assert detail["topic_fit"] == pytest.approx(1.0)
    assert embedded[0] == ["A\nB\nC"] and embedded[1] == ["Maths"]            # trois premiers chunks seulement


def test_off_topic_document_scores_zero_even_if_new_and_good(monkeypatch):
    embed_fn, _ = _score_env(monkeypatch, [], "1", embed=lambda t: [[1.0, 0.0]] if t != ["Cuisine"] else [[0.0, 1.0]])
    score, detail = rs.compute_relevance_score(["A"], embed_fn, "coll", {}, topic_label="Cuisine")
    assert score == 0.0 and detail["topic_fit"] == pytest.approx(0.0)


def test_unavailable_subscores_are_neutralised_not_penalised(monkeypatch):
    monkeypatch.setattr(vs, "search_dense", lambda *a, **k: (_ for _ in ()).throw(RuntimeError("collection absente")))
    _judge(monkeypatch, error=TimeoutError("HS"))
    score, detail = rs.compute_relevance_score(["A"], lambda t: [[1.0, 0.0]], "coll", {})
    assert score == 0.75 and detail == {"novelty": None, "quality": None, "topic_fit": None}


def test_no_topic_label_means_no_topic_penalty(monkeypatch):
    embed_fn, embedded = _score_env(monkeypatch, [SimpleNamespace(score=0.0)], "0.5")
    score, detail = rs.compute_relevance_score(["A"], embed_fn, "coll", {})
    assert score == 0.75 and detail["topic_fit"] is None and len(embedded) == 1


def test_no_chunks_gives_a_neutral_score(monkeypatch):
    assert rs.compute_relevance_score([], lambda t: pytest.fail("pas d'embedding attendu"), "coll", {}) == (
        0.5, {"novelty": None, "quality": None, "topic_fit": None, "reason": "no_chunks"})


def test_embedding_failure_gives_a_neutral_score_with_the_reason(monkeypatch):
    def broken(texts):
        raise RuntimeError("Cohere indisponible")

    score, detail = rs.compute_relevance_score(["A"], broken, "coll", {})
    assert score == 0.5 and detail["reason"] == "Cohere indisponible"


def test_sample_is_capped_at_three_thousand_characters(monkeypatch):
    embed_fn, embedded = _score_env(monkeypatch, [], "0.5")
    rs.compute_relevance_score(["x" * 5000], embed_fn, "coll", {})
    assert len(embedded[0][0]) == 3000


# ═══════════════════════════ base vectorielle ═══════════════════════════

class FakeQdrant:
    def __init__(self, collections=()):
        self.collections, self.created, self.indexes, self.upserts = list(collections), [], [], []
        self.queries, self.scrolls, self.retrieved, self.payloads = [], [], [], []

    def get_collections(self):
        return SimpleNamespace(collections=[SimpleNamespace(name=n) for n in self.collections])

    def create_collection(self, collection_name, vectors_config):
        self.created.append((collection_name, vectors_config))

    def create_payload_index(self, collection, field_name, field_schema):
        self.indexes.append((collection, field_name))

    def upsert(self, collection_name, points):
        self.upserts.append((collection_name, points))

    def query_points(self, **kw):
        self.queries.append(kw)
        return SimpleNamespace(points=["p1", "p2"])

    def scroll(self, **kw):
        self.scrolls.append(kw)
        return (["r1"], None)

    def retrieve(self, **kw):
        self.retrieved.append(kw)
        return ["r"]

    def set_payload(self, **kw):
        self.payloads.append(kw)


@pytest.fixture
def qdrant(monkeypatch):
    fake = FakeQdrant()
    monkeypatch.setattr(vs, "get_client", lambda: fake)
    return fake


def test_client_is_created_once_with_the_configured_url(monkeypatch):
    created = []

    class Client:
        def __init__(self, url, api_key):
            created.append((url, api_key))

    monkeypatch.setattr(vs, "_client", None)
    monkeypatch.setattr(vs, "QdrantClient", Client)
    first, second = vs.get_client(), vs.get_client()
    assert first is second and len(created) == 1 and created[0][0] == vs.QDRANT_URL


def test_filter_building():
    assert vs.build_filter({}) is None and vs.build_filter(None) is None
    f = vs.build_filter({"subject_id": 3, "status": "active", "doc_id": ["a", "b"]})
    by_key = {c.key: c.match for c in f.must}
    assert by_key["subject_id"].value == 3 and by_key["status"].value == "active" and by_key["doc_id"].any == ["a", "b"]


def test_existing_collection_is_left_untouched(qdrant):
    qdrant.collections = ["winplus_api"]
    vs.ensure_collection("winplus_api", 1536)
    assert qdrant.created == [] and qdrant.indexes == []


def test_new_collection_gets_cosine_vectors_and_four_payload_indexes(qdrant):
    vs.ensure_collection("neuve", 1536)
    name, config_ = qdrant.created[0]
    assert name == "neuve" and config_.size == 1536 and config_.distance.value == "Cosine"
    assert [f for _, f in qdrant.indexes] == ["subject_id", "course_id", "status", "doc_id"]


def test_upsert_sends_points_in_batches_with_text_and_metadata(qdrant):
    chunks = [Chunk(chunk_id=f"c{i}", text=f"T{i}", parent_text=f"P{i}", metadata=ChunkMetadata(doc_id="d", title="t", subject_id=2))
              for i in range(130)]
    vs.upsert_chunks("coll", chunks, [[float(i)] for i in range(130)])
    assert [len(points) for _, points in qdrant.upserts] == [64, 64, 2]
    first = qdrant.upserts[0][1][0]
    assert first.id == "c0" and first.vector == [0.0]
    assert first.payload["text"] == "T0" and first.payload["parent_text"] == "P0" and first.payload["subject_id"] == 2


def test_upsert_of_nothing_sends_nothing(qdrant):
    vs.upsert_chunks("coll", [], [])
    assert qdrant.upserts == []


def test_dense_search_passes_vector_limit_and_filter(qdrant):
    points = vs.search_dense("coll", [0.1, 0.2], top_k=7, filters={"status": "active"})
    call = qdrant.queries[0]
    assert points == ["p1", "p2"] and call["collection_name"] == "coll" and call["query"] == [0.1, 0.2]
    assert call["limit"] == 7 and call["with_payload"] is True and call["query_filter"].must[0].key == "status"
    vs.search_dense("coll", [0.1])
    assert qdrant.queries[1]["query_filter"] is None and qdrant.queries[1]["limit"] == 10


def test_scroll_by_filter_returns_records_only(qdrant):
    assert vs.scroll_by_filter("coll", {"doc_id": "d"}, limit=5) == ["r1"]
    assert qdrant.scrolls[0]["limit"] == 5 and qdrant.scrolls[0]["scroll_filter"].must[0].key == "doc_id"


def test_get_by_ids_skips_the_call_for_an_empty_list(qdrant):
    assert vs.get_by_ids("coll", []) == [] and qdrant.retrieved == []
    assert vs.get_by_ids("coll", ["a"]) == ["r"] and qdrant.retrieved[0]["ids"] == ["a"]


def test_old_versions_are_marked_superseded_rather_than_deleted(qdrant):
    vs.mark_superseded("coll", "doc1", superseded_by="doc2")
    call = qdrant.payloads[0]
    assert call["payload"] == {"status": "superseded", "superseded_by": "doc2"}
    assert call["points"].must[0].key == "doc_id" and call["points"].must[0].match.value == "doc1"
