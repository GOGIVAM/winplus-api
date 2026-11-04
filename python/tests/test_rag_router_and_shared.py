"""Base de connaissance RAG : routes HTTP (/rag/ingest, /status, /query, /chat-attachment, /health), tâche
d'ingestion en arrière-plan, aiguillage de moteur, résolution des fichiers (URL S3, URL ordinaire, chemin local,
contenu en base64), registre des tâches, coûts, identifiants et graphe. Aucun accès réseau, S3 ni Qdrant."""

import asyncio
import base64
import contextlib
import os
import sys
from datetime import date
from types import ModuleType, SimpleNamespace

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

import RAG.query_service as qs
import RAG.router as router
import RAG.shared.cost as cost
import RAG.shared.file_resolver as fr
import RAG.shared.metadata as metadata
from RAG.shared.contracts import (
    Chunk, ChunkMetadata, IngestJobStatus, IngestRequest, IngestResult, RAGAnswer, RAGQueryRequest, RetrievedContext,
    SourceType,
)
from RAG.shared.graph_registry import GraphRegistry
from RAG.shared.ingest_jobs import IngestJobRegistry


@pytest.fixture(autouse=True)
def clean_registry():
    IngestJobRegistry._jobs.clear()
    yield
    IngestJobRegistry._jobs.clear()


# ═══════════════════════════ registre des tâches ═══════════════════════════

def test_job_lifecycle_is_tracked_per_document():
    assert IngestJobRegistry.get("d1") is None
    IngestJobRegistry.set_queued("d1")
    assert IngestJobRegistry.get("d1").status == IngestJobStatus.QUEUED
    IngestJobRegistry.set_processing("d1")
    assert IngestJobRegistry.get("d1").status == IngestJobStatus.PROCESSING
    result = IngestResult(doc_id="d1", chunks_indexed=4, source_type=SourceType.PDF_NATIVE, warnings=["w"])
    IngestJobRegistry.set_done("d1", result)
    record = IngestJobRegistry.get("d1")
    assert record.status == IngestJobStatus.DONE and record.result.chunks_indexed == 4 and record.error is None
    assert IngestJobRegistry.get("d2") is None


def test_a_failed_job_keeps_its_error_and_can_be_queued_again():
    IngestJobRegistry.set_failed("d1", "OCR indisponible")
    record = IngestJobRegistry.get("d1")
    assert record.status == IngestJobStatus.FAILED and record.error == "OCR indisponible" and record.result is None
    IngestJobRegistry.set_queued("d1")
    assert IngestJobRegistry.get("d1").error is None


# ═══════════════════════════ identifiants et coûts ═══════════════════════════

def test_document_id_scheme():
    assert metadata.build_doc_id("EPREUVE", "BACC2026", 1, date(2026, 9, 1)) == "EPREUVE_BACC2026_v1_20260901"
    assert metadata.build_doc_id("COURS", "MATHS", 12, date(2027, 1, 5)) == "COURS_MATHS_v12_20270105"


def test_document_id_defaults_to_today(monkeypatch):
    class Frozen(date):
        @classmethod
        def today(cls):
            return date(2026, 3, 15)

    monkeypatch.setattr(metadata, "date", Frozen)
    assert metadata.build_doc_id("COURS", "SVT", 2) == "COURS_SVT_v2_20260315"


def test_token_estimates_use_four_characters_per_token_with_a_floor_of_one():
    assert cost._tokens("") == 1 and cost._tokens("abc") == 1 and cost._tokens("a" * 400) == 100 and cost._tokens(None) == 1
    assert cost.embedding_cost_tokens("a" * 400) == int(100 * cost.EMBEDDING_TOKEN_FACTOR)
    assert cost.embedding_cost_tokens("") >= 1


def test_reformulation_and_search_cost():
    assert cost.reformulation_cost_tokens("a" * 40, "b" * 80) == cost.REFORMULATION_PROMPT_TOKENS + 10 + 20
    assert cost.search_cost_estimate("a" * 400) == cost.embedding_cost_tokens("a" * 400) + cost.RERANK_TOKEN_EQUIVALENT
    assert cost.CHAT_MAX_ITERATIONS == 2


# ═══════════════════════════ graphe de connaissances ═══════════════════════════

@pytest.fixture
def graphs(tmp_path, monkeypatch):
    monkeypatch.setattr(GraphRegistry, "_graphs", {})
    monkeypatch.setattr(GraphRegistry, "_storage_dir", str(tmp_path / "graphs"))
    return tmp_path / "graphs"


def triple(subject, obj, relation="définit", doc_id="d1"):
    return SimpleNamespace(subject=subject, obj=obj, relation=relation, doc_id=doc_id)


def test_graph_of_an_unknown_collection_is_empty(graphs):
    assert GraphRegistry.get("coll").number_of_nodes() == 0


def test_triples_become_nodes_and_labelled_edges_and_are_saved_to_disk(graphs):
    GraphRegistry.add_triples("coll", [triple("dérivée", "pente"), triple("pente", "tangente", "mesure", "d2")])
    graph = GraphRegistry.get("coll")
    assert set(graph.nodes) == {"dérivée", "pente", "tangente"}
    assert graph["dérivée"]["pente"] == {"relation": "définit", "doc_id": "d1"}
    assert graph["pente"]["tangente"]["doc_id"] == "d2"
    assert os.path.exists(graphs / "coll.gpickle")


def test_graph_is_reloaded_from_disk_after_a_restart(graphs, monkeypatch):
    GraphRegistry.add_triples("coll", [triple("a", "b")])
    monkeypatch.setattr(GraphRegistry, "_graphs", {})                 # simule un redémarrage du service
    assert list(GraphRegistry.get("coll").edges) == [("a", "b")]


def test_graphs_of_two_collections_are_independent(graphs):
    GraphRegistry.add_triples("c1", [triple("a", "b")])
    assert GraphRegistry.get("c2").number_of_nodes() == 0


# ═══════════════════════════ résolution des fichiers ═══════════════════════════

def _request(**kw):
    base = {"doc_id": "d1", "title": "cours.pdf"}
    base.update(kw)
    return IngestRequest(**base)


def test_inline_base64_is_decoded_to_a_temporary_file_that_is_removed_afterwards():
    payload = base64.b64encode(b"%PDF-contenu").decode()
    with fr.resolve_local_path(_request(inline_content_base64=payload)) as path:
        assert path.endswith(".pdf") and open(path, "rb").read() == b"%PDF-contenu"
    assert not os.path.exists(path)


def test_inline_base64_accepts_a_data_url_and_an_extension_hint_without_dot():
    payload = "data:application/pdf;base64," + base64.b64encode(b"abc").decode()
    with fr.resolve_local_path(_request(title="sans-extension", inline_content_base64=payload,
                                        file_extension_hint="mp4")) as path:
        assert path.endswith(".mp4") and open(path, "rb").read() == b"abc"


def test_inline_content_without_any_extension_hint_falls_back_to_bin():
    with fr.resolve_local_path(_request(title="sans-extension", inline_content_base64=base64.b64encode(b"x").decode())) as path:
        assert path.endswith(".bin")


def test_an_existing_local_path_is_used_as_is_and_never_deleted(tmp_path):
    local = tmp_path / "doc.pdf"
    local.write_bytes(b"x")
    with fr.resolve_local_path(_request(file_path=str(local))) as path:
        assert path == str(local)
    assert local.exists()


def test_a_missing_local_file_and_an_empty_request_are_errors(tmp_path):
    with pytest.raises(FileNotFoundError, match="introuvable"):
        with fr.resolve_local_path(_request(file_path=str(tmp_path / "absent.pdf"))):
            pass
    with pytest.raises(ValueError, match="ni file_path ni inline_content_base64"):
        with fr.resolve_local_path(_request()):
            pass


class FakeResponse:
    def __init__(self, chunks, error=None):
        self.chunks, self.error = chunks, error

    def raise_for_status(self):
        if self.error:
            raise self.error

    def iter_content(self, chunk_size):
        yield from self.chunks


def test_a_plain_url_is_downloaded_with_a_timeout_then_cleaned_up(monkeypatch):
    seen = {}

    def get(url, timeout, stream):
        seen.update(url=url, timeout=timeout, stream=stream)
        return FakeResponse([b"abc", b"def"])

    monkeypatch.setattr(fr.requests, "get", get)
    with fr.resolve_local_path(_request(file_path="https://cdn.example.test/docs/cours.pdf?v=2")) as path:
        assert path.endswith(".pdf") and open(path, "rb").read() == b"abcdef"
    assert seen == {"url": "https://cdn.example.test/docs/cours.pdf?v=2", "timeout": 60, "stream": True}
    assert not os.path.exists(path)


@pytest.mark.xfail(strict=False, reason="_from_url ouvre un descripteur avec mkstemp puis ne le ferme (os.fdopen) qu'après "
                   "raise_for_status : si le téléchargement échoue, le descripteur fuit et, sous Windows, le fichier "
                   "temporaire ne peut pas être supprimé (le nettoyage avale l'erreur). Aucun fichier ne devrait rester.")
def test_a_failed_download_is_an_error_and_leaves_no_temporary_file(monkeypatch):
    created = []
    real = fr.tempfile.mkstemp

    def tracking(*a, **k):
        fd, path = real(*a, **k)
        created.append(path)
        return fd, path

    monkeypatch.setattr(fr.tempfile, "mkstemp", tracking)
    monkeypatch.setattr(fr.requests, "get", lambda *a, **k: FakeResponse([], error=ConnectionError("404")))
    with pytest.raises(ConnectionError):
        with fr.resolve_local_path(_request(file_path="https://cdn.example.test/x.pdf")):
            pass
    assert created and not os.path.exists(created[0])


def test_a_private_s3_url_is_read_with_the_service_credentials_not_over_http(monkeypatch):
    import boto3
    seen = {}

    class Body:
        def __init__(self):
            self.parts = [b"un", b"deux", b""]

        def read(self, size):
            return self.parts.pop(0)

    class S3:
        def get_object(self, Bucket, Key):
            seen["object"] = (Bucket, Key)
            return {"Body": Body()}

    monkeypatch.setattr(boto3, "client", lambda service, region_name=None: seen.update(region=region_name) or S3())
    monkeypatch.setattr(fr.requests, "get", lambda *a, **k: pytest.fail("pas de requête HTTP attendue pour S3"))
    with fr.resolve_local_path(_request(file_path="https://winplus-files.s3.eu-west-1.amazonaws.com/epreuves/a%20b.pdf")) as path:
        assert open(path, "rb").read() == b"undeux"
    assert seen == {"region": "eu-west-1", "object": ("winplus-files", "epreuves/a b.pdf")}


@pytest.mark.parametrize("url,expected", [
    ("https://mybucket.s3.eu-west-1.amazonaws.com/dir/f.pdf", ("mybucket", "eu-west-1", "dir/f.pdf")),
    ("https://mybucket.s3-eu-west-1.amazonaws.com/f.pdf", ("mybucket", "eu-west-1", "f.pdf")),
    ("https://mybucket.s3.amazonaws.com/f.pdf", ("mybucket", "us-east-1", "f.pdf")),
    ("https://s3.eu-west-3.amazonaws.com/mybucket/dir/f.pdf", ("mybucket", "eu-west-3", "dir/f.pdf")),
    ("https://s3.amazonaws.com/mybucket/f.pdf", ("mybucket", "us-east-1", "f.pdf")),
    ("https://mybucket.s3.eu-west-1.amazonaws.com/a%20b.pdf", ("mybucket", "eu-west-1", "a b.pdf")),
])
def test_s3_location_of_supported_url_styles(url, expected):
    assert fr._s3_location(url) == expected


@pytest.mark.parametrize("url", ["https://example.com/f.pdf", "https://mybucket.s3.eu-west-1.amazonaws.com/",
                                 "https://s3.amazonaws.com/onlybucket", "https://autre.amazonaws.com/f.pdf", ""])
def test_s3_location_of_other_urls_is_none(url):
    assert fr._s3_location(url) is None


# ═══════════════════════════ aiguillage de moteur ═══════════════════════════

def _fake_modules(monkeypatch, backend):
    """Installe de faux modules de pipeline pour le moteur demandé (aucun import lourd)."""
    names = {"engine": ("run_query", "retrieve_passages"), "indexing": ("index_chunks",), "ingestion": ("process_document",)}
    for part, functions in names.items():
        module = ModuleType(f"RAG.{backend}.{part}.pipeline")
        for fn in functions:
            setattr(module, fn, (lambda label: (lambda *a, **k: label))(f"{backend}:{fn}"))
        monkeypatch.setitem(sys.modules, module.__name__, module)


@pytest.mark.parametrize("backend", ["api", "self_hosted"])
def test_router_picks_the_engine_functions_of_the_active_backend(monkeypatch, backend):
    _fake_modules(monkeypatch, backend)
    monkeypatch.setattr(router, "RAG_BACKEND", backend)
    process_document, index_chunks, run_query = router._active_backend()
    assert (process_document(), index_chunks(), run_query()) == (
        f"{backend}:process_document", f"{backend}:index_chunks", f"{backend}:run_query")


@pytest.mark.parametrize("backend,fn", [("api", "retrieve_passages"), ("self_hosted", "retrieve_passages")])
def test_query_service_picks_the_engine_of_the_active_backend(monkeypatch, backend, fn):
    _fake_modules(monkeypatch, backend)
    monkeypatch.setattr(qs, "RAG_BACKEND", backend)
    assert qs._active_retrieve_passages()() == f"{backend}:retrieve_passages"
    assert qs._active_run_query()() == f"{backend}:run_query"


def test_query_service_runs_the_blocking_engine_off_the_event_loop(monkeypatch):
    request = RAGQueryRequest(question="Q ?")
    context = RetrievedContext(passages=["p"], cost_tokens=5)
    answer = RAGAnswer(answer="A", backend="api")
    monkeypatch.setattr(qs, "_active_retrieve_passages", lambda: lambda r: context)
    monkeypatch.setattr(qs, "_active_run_query", lambda: lambda r: answer)
    assert asyncio.run(qs.retrieve_context(request)) is context
    assert asyncio.run(qs.query_rag(request)) is answer


# ═══════════════════════════ tâche d'ingestion ═══════════════════════════

@pytest.fixture
def backend(monkeypatch):
    state = SimpleNamespace(processed=[], indexed=[], superseded=[], events=[], process_error=None,
                            warnings=["w-ingestion"], scoring=["w-scoring"])

    def process_document(request):
        state.processed.append(request)
        state.events.append("process")
        if state.process_error:
            raise state.process_error
        chunk = Chunk(chunk_id="c1", text="t", metadata=ChunkMetadata(doc_id=request.doc_id, title=request.title))
        return [chunk, chunk], SourceType.PDF_NATIVE, list(state.warnings)

    def index_chunks(chunks):
        state.events.append("index")
        state.indexed.append(chunks)
        return list(state.scoring)

    monkeypatch.setattr(router, "_active_backend", lambda: (process_document, index_chunks, None))

    def supersede(doc_id):
        state.events.append("supersede")
        state.superseded.append(doc_id)

    monkeypatch.setattr(router, "_supersede_previous_version", supersede)

    @contextlib.contextmanager
    def resolver(request):
        yield "/tmp/local-copy.pdf"

    monkeypatch.setattr(fr, "resolve_local_path", resolver)
    return state


def test_ingestion_job_success_records_chunks_source_and_merged_warnings(backend):
    router._run_ingestion_job(_request(file_path="https://x.test/a.pdf"))
    record = IngestJobRegistry.get("d1")
    assert record.status == IngestJobStatus.DONE
    assert record.result.chunks_indexed == 2 and record.result.source_type == SourceType.PDF_NATIVE
    assert record.result.warnings == ["w-ingestion", "w-scoring"]
    assert backend.events == ["supersede", "process", "index"]          # l'ancienne version est écartée en premier
    assert backend.processed[0].file_path == "/tmp/local-copy.pdf"       # le moteur reçoit le chemin local


def test_ingestion_job_failure_is_recorded_not_raised(backend):
    backend.process_error = RuntimeError("OCR indisponible")
    router._run_ingestion_job(_request(file_path="https://x.test/a.pdf"))
    record = IngestJobRegistry.get("d1")
    assert record.status == IngestJobStatus.FAILED and record.error == "OCR indisponible"
    assert backend.indexed == []


def test_superseding_marks_the_previous_version_in_the_right_collection(monkeypatch):
    import RAG.shared.vector_store as vs
    calls = []
    monkeypatch.setattr(vs, "mark_superseded", lambda collection, doc_id, superseded_by: calls.append((collection, doc_id, superseded_by)))
    monkeypatch.setattr(router, "RAG_BACKEND", "api")
    router._supersede_previous_version("d1")
    monkeypatch.setattr(router, "RAG_BACKEND", "self_hosted")
    router._supersede_previous_version("d2")
    from RAG.api.config import QDRANT_COLLECTION as api_collection
    from RAG.self_hosted.config import QDRANT_COLLECTION as sh_collection
    assert calls == [(api_collection, "d1", "d1"), (sh_collection, "d2", "d2")]


def test_superseding_on_a_first_ingestion_is_silent(monkeypatch):
    import RAG.shared.vector_store as vs
    monkeypatch.setattr(vs, "mark_superseded", lambda *a, **k: (_ for _ in ()).throw(RuntimeError("collection absente")))
    router._supersede_previous_version("d1")                      # ne lève rien


# ═══════════════════════════ routes HTTP ═══════════════════════════

@pytest.fixture
def client(monkeypatch):
    queued = []
    monkeypatch.setattr(router, "_run_ingestion_job", lambda request: queued.append(request))
    app = FastAPI()
    app.include_router(router.rag_router, prefix="/api")
    test_client = TestClient(app, raise_server_exceptions=False)
    test_client.queued = queued
    return test_client


INGEST = {"doc_id": "d1", "title": "Cours", "file_path": "https://x.test/a.pdf"}


@pytest.mark.parametrize("method,path,body", [("post", "/api/rag/ingest", INGEST), ("get", "/api/rag/ingest/d1/status", None),
                                              ("post", "/api/rag/query", {"question": "Q ?"}),
                                              ("post", "/api/rag/chat-attachment", {"data_url_or_base64": "abc"})])
def test_rag_routes_require_a_token_but_health_is_public(client, method, path, body):
    assert getattr(client, method)(path, **({"json": body} if body else {})).status_code in (401, 403)
    health = client.get("/api/rag/health")
    assert health.status_code == 200 and health.json()["status"] == "ok" and "active_backend" in health.json()


def test_ingest_answers_202_immediately_and_queues_the_job(client, headers_for):
    r = client.post("/api/rag/ingest", json={**INGEST, "subject_id": 4}, headers=headers_for(7))
    assert r.status_code == 202 and r.json() == {"doc_id": "d1", "status": "queued"}
    assert len(client.queued) == 1 and client.queued[0].doc_id == "d1"
    assert IngestJobRegistry.get("d1").status == IngestJobStatus.QUEUED


def test_a_document_without_subject_or_course_belongs_to_the_caller(client, headers_for):
    client.post("/api/rag/ingest", json=INGEST, headers=headers_for(7))
    assert client.queued[0].owner_user_id == 7


@pytest.mark.parametrize("scope", [{"subject_id": 4}, {"course_id": 9}])
def test_a_catalogue_document_never_keeps_an_owner_even_if_the_client_sent_one(client, headers_for, scope):
    client.post("/api/rag/ingest", json={**INGEST, **scope, "owner_user_id": 99}, headers=headers_for(7))
    assert client.queued[0].owner_user_id is None


def test_the_owner_cannot_be_spoofed_on_a_personal_document(client, headers_for):
    client.post("/api/rag/ingest", json={**INGEST, "owner_user_id": 99}, headers=headers_for(7))
    assert client.queued[0].owner_user_id == 7


@pytest.mark.parametrize("body", [{}, {"doc_id": "d1"}, {"title": "x"}, {**INGEST, "subject_id": "abc"}])
def test_ingest_validates_its_body(client, headers_for, body):
    assert client.post("/api/rag/ingest", json=body, headers=headers_for(7)).status_code == 422


def test_ingest_status(client, headers_for):
    assert client.get("/api/rag/ingest/inconnu/status", headers=headers_for(7)).status_code == 404
    IngestJobRegistry.set_failed("d1", "OCR indisponible")
    body = client.get("/api/rag/ingest/d1/status", headers=headers_for(7)).json()
    assert body["status"] == "failed" and body["error"] == "OCR indisponible"


def test_query_returns_the_engine_answer(client, headers_for, monkeypatch):
    seen = []

    async def fake_query(request):
        seen.append(request)
        return RAGAnswer(answer="Réponse", backend="api", faithfulness=0.95)

    monkeypatch.setattr(qs, "query_rag", fake_query)
    r = client.post("/api/rag/query", json={"question": "Qu'est-ce que ... ?", "filters": {"subject_id": 4}},
                    headers=headers_for(7))
    assert r.status_code == 200 and r.json()["answer"] == "Réponse" and r.json()["faithfulness"] == 0.95
    assert seen[0].top_k == 5 and seen[0].filters == {"subject_id": 4}


def test_query_failure_is_a_500_with_the_message(client, headers_for, monkeypatch):
    async def fail(request):
        raise TimeoutError("Qdrant ne répond pas")

    monkeypatch.setattr(qs, "query_rag", fail)
    r = client.post("/api/rag/query", json={"question": "Q ?"}, headers=headers_for(7))
    assert r.status_code == 500 and "Qdrant ne répond pas" in r.json()["detail"]
    assert client.post("/api/rag/query", json={}, headers=headers_for(7)).status_code == 422


def test_chat_attachment_is_processed_for_the_calling_user(client, headers_for, monkeypatch):
    seen = []

    async def fake(data, name, user_id):
        seen.append((data, name, user_id))
        return "[Fichier cours.pdf] texte extrait"

    monkeypatch.setattr("services.attachment_processor.process_chat_attachment_async", fake)
    r = client.post("/api/rag/chat-attachment", json={"data_url_or_base64": "QUJD", "file_name": "cours.pdf"},
                    headers=headers_for(12))
    assert r.json() == {"text_for_prompt": "[Fichier cours.pdf] texte extrait"} and seen == [("QUJD", "cours.pdf", 12)]
    assert client.post("/api/rag/chat-attachment", json={}, headers=headers_for(12)).status_code == 422
