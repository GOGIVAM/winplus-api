import pytest

pytest.importorskip("rank_bm25", reason="rank_bm25 fait partie de requirements-self-hosted.txt / requirements-api.txt")

from RAG.shared.bm25_index import BM25Index


def test_bm25_search_ranks_exact_term_match_first():
    idx = BM25Index()
    idx.add_many(
        [
            ("c1", "le délai de préavis est de deux mois pour les agents de maîtrise"),
            ("c2", "la rémunération des heures supplémentaires est majorée de 25 pourcent"),
            ("c3", "aucune information sur le sujet recherché ici"),
        ]
    )
    results = idx.search("délai de préavis", top_k=2)
    assert results
    assert results[0] == "c1"


def test_bm25_search_empty_index_returns_empty():
    idx = BM25Index()
    assert idx.search("quoi que ce soit") == []


def test_bm25_save_and_load_roundtrip(tmp_path):
    idx = BM25Index()
    idx.add_many([("c1", "texte un"), ("c2", "texte deux")])
    path = str(tmp_path / "index.pkl")
    idx.save(path)

    loaded = BM25Index.load(path)
    assert loaded.search("texte") != []
    assert set(loaded.chunk_ids) == {"c1", "c2"}


def test_bm25_load_missing_file_returns_empty_index(tmp_path):
    loaded = BM25Index.load(str(tmp_path / "does_not_exist.pkl"))
    assert loaded.search("anything") == []
