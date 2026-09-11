from RAG.shared.contracts import (
    Chunk,
    ChunkMetadata,
    ChunkType,
    Citation,
    DocStatus,
    IngestRequest,
    RAGAnswer,
    RAGQueryRequest,
)


def test_rag_query_request_defaults():
    req = RAGQueryRequest(question="Question ?")
    assert req.filters == {}
    assert req.top_k == 5


def test_rag_query_request_filters_roundtrip():
    req = RAGQueryRequest(question="Q", filters={"subject_id": 3, "status": "active"}, top_k=8)
    dumped = req.model_dump()
    assert dumped["filters"] == {"subject_id": 3, "status": "active"}
    assert dumped["top_k"] == 8


def test_chunk_metadata_defaults_active_short():
    meta = ChunkMetadata(doc_id="D1", title="Titre")
    assert meta.status == DocStatus.ACTIVE
    assert meta.chunk_type == ChunkType.SHORT
    assert meta.superseded_by is None


def test_chunk_carries_parent_text():
    meta = ChunkMetadata(doc_id="D1", title="Titre")
    chunk = Chunk(chunk_id="c1", text="court", parent_text="plus long", metadata=meta)
    assert chunk.text == "court"
    assert chunk.parent_text == "plus long"


def test_rag_answer_refusal_shape():
    answer = RAGAnswer(answer="refus", refused=True, backend="self_hosted")
    assert answer.refused is True
    assert answer.citations == []
    assert answer.faithfulness is None


def test_citation_and_ingest_request_serialize():
    citation = Citation(doc_id="D1", title="T", chunk_id="c1", score=0.87)
    assert 0.0 <= citation.score <= 1.0

    req = IngestRequest(doc_id="D1", title="T", file_path="/tmp/x.pdf", subject_id=3)
    assert req.subject_id == 3
    assert req.course_id is None
