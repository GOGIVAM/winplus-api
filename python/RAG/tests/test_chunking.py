from RAG.shared.chunking import (
    fixed_window_chunk_pairs,
    has_structural_markers,
    semantic_chunk_pairs,
)


def _count_words(text: str) -> int:
    return len(text.split())


def test_fixed_window_chunk_pairs_respects_short_bound():
    text = " ".join(f"mot{i}" for i in range(500))
    pairs = fixed_window_chunk_pairs(text, _count_words, short_tokens=50, long_tokens=200, overlap_tokens=10)
    assert pairs, "au moins un chunk doit être produit"
    for p in pairs:
        assert _count_words(p.short_text) <= 50 * 1.15
        assert _count_words(p.long_text) <= 200 * 1.15
        assert len(p.long_text) >= len(p.short_text)


def test_fixed_window_chunk_pairs_overlap_creates_repetition():
    text = " ".join(f"mot{i}" for i in range(200))
    pairs = fixed_window_chunk_pairs(text, _count_words, short_tokens=50, long_tokens=100, overlap_tokens=20)
    assert len(pairs) >= 2
    # Le chevauchement doit produire au moins un mot commun entre deux chunks consécutifs.
    words_a = set(pairs[0].short_text.split())
    words_b = set(pairs[1].short_text.split())
    assert words_a & words_b


def test_fixed_window_chunk_pairs_empty_text():
    assert fixed_window_chunk_pairs("", _count_words) == []
    assert fixed_window_chunk_pairs("   ", _count_words) == []


def test_has_structural_markers_detects_articles():
    text = "Article 1\nContenu.\nArticle 2\nContenu.\nArticle 3\nContenu."
    assert has_structural_markers(text, min_hits=3)


def test_has_structural_markers_false_on_prose():
    text = "Ceci est un texte normal sans aucune structure particulière du tout."
    assert not has_structural_markers(text, min_hits=3)


def test_semantic_chunk_pairs_splits_on_articles():
    text = (
        "Article 1\nLe délai de préavis est d'un mois.\n"
        "Article 2\nLe délai de préavis est de deux mois pour les agents de maîtrise.\n"
        "Article 3\nLe délai de préavis est de trois mois pour les cadres."
    )
    pairs = semantic_chunk_pairs(text, _count_words, short_tokens=50, long_tokens=200, page=1)
    assert len(pairs) == 3
    assert all(p.page == 1 for p in pairs)
    assert all(p.section for p in pairs)
    assert "Article 2" in pairs[1].section
