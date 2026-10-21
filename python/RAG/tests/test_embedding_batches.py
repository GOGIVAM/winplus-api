"""Cohere accepte au plus 96 textes par appel : embed_texts découpe en lots
et rend les vecteurs dans l'ordre d'origine."""

from types import SimpleNamespace

from RAG.api.indexing import embedding_client


class _FakeCohere:
    def __init__(self):
        self.calls = []

    def embed(self, texts, model, input_type, embedding_types):
        assert len(texts) <= 96
        self.calls.append(len(texts))
        return SimpleNamespace(embeddings=SimpleNamespace(float=[[float(t)] for t in texts]))


def test_large_document_is_split_in_batches(monkeypatch):
    fake = _FakeCohere()
    monkeypatch.setattr(embedding_client, "_get_client", lambda: fake)
    vectors = embedding_client.embed_texts([str(i) for i in range(157)])
    assert fake.calls == [96, 61]
    assert [v[0] for v in vectors] == [float(i) for i in range(157)]
