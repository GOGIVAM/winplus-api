"""NLPAnalyzer (models/nlp_analyzer.py) : difficulté, niveaux, tags, métriques, durée, similarité et
encodage. Le modèle BERT est remplacé par un faux modèle déterministe : rien n'est téléchargé."""

from types import SimpleNamespace

import numpy as np
import pytest
import torch

from models.nlp_analyzer import NLPAnalyzer


@pytest.fixture
def analyzer(monkeypatch):
    monkeypatch.delenv("NLP_MODEL", raising=False)
    return NLPAnalyzer()


# ── construction ────────────────────────────────────────────────────────────

def test_default_model_name_and_no_loading_at_init(analyzer):
    assert analyzer.model_name == "camembert-base"
    assert analyzer.model is None and analyzer.tokenizer is None and analyzer.load_failed is False


def test_model_name_from_argument_then_environment(monkeypatch):
    monkeypatch.setenv("NLP_MODEL", "flaubert/base-cased")
    assert NLPAnalyzer().model_name == "flaubert/base-cased"
    assert NLPAnalyzer("autre-modele").model_name == "autre-modele"


def test_difficulty_keywords_cover_three_levels(analyzer):
    assert set(analyzer.difficulty_keywords) == {"facile", "moyen", "difficile"}
    assert "avancé" in analyzer.difficulty_keywords["difficile"]


# ── niveaux ─────────────────────────────────────────────────────────────────

@pytest.mark.parametrize("score,level", [
    (0.0, "facile"), (0.39, "facile"), (0.4, "moyen"), (0.69, "moyen"), (0.7, "difficile"), (1.0, "difficile"),
])
def test_score_to_level_boundaries(analyzer, score, level):
    assert analyzer._score_to_level(score) == level


# ── difficulté ──────────────────────────────────────────────────────────────

def test_easy_keywords_and_short_sentences_lower_the_score(analyzer):
    # 2 mots-clés faciles : 0.2 ; phrases courtes (moyenne 2 mots) : -0.1
    score = analyzer._compute_difficulty("Introduction aux bases. Court.")
    assert score == pytest.approx(0.1)


def test_text_without_keywords_defaults_to_medium(analyzer):
    text = "Le cours porte sur la manière de ranger des objets dans une boîte"  # 13 mots
    assert analyzer._compute_difficulty(text) == pytest.approx(0.5)


def test_long_sentences_raise_the_score(analyzer):
    assert analyzer._compute_difficulty(" ".join(["mot"] * 22)) == pytest.approx(0.6)


def test_hard_keywords_and_clipping(analyzer):
    assert analyzer._compute_difficulty("avancé complexe expert") == pytest.approx(0.8)
    # 0.9 + 0.1 (phrase longue) est ramené à 1.0
    assert analyzer._compute_difficulty("avancé " + " ".join(["mot"] * 25)) == pytest.approx(1.0)


def test_mixed_keywords_are_averaged_and_case_is_ignored(analyzer):
    # (0.2 + 0.9) / 2 = 0.55 puis -0.1 pour deux mots seulement
    assert analyzer._compute_difficulty("INTRODUCTION Avancé") == pytest.approx(0.45)


# ── tags ────────────────────────────────────────────────────────────────────

def test_tags_from_theme_and_keywords(analyzer):
    tags = analyzer._extract_tags("Une équation en Python avec de la grammaire", {"theme": "Sciences"})
    assert set(tags) == {"sciences", "mathématiques", "programmation", "langues"}


def test_tags_for_artificial_intelligence_and_none(analyzer):
    assert analyzer._extract_tags("Le deep learning moderne", {}) == ["intelligence artificielle"]
    assert analyzer._extract_tags("Un texte neutre", {}) == []


# ── métriques et durée ──────────────────────────────────────────────────────

def test_complexity_metrics(analyzer):
    metrics = analyzer._compute_complexity_metrics("Un deux trois. Quatre cinq.")
    assert metrics["word_count"] == 5 and metrics["sentence_count"] == 2
    assert metrics["avg_word_length"] == pytest.approx(4.6)   # (2 + 4 + 6 + 6 + 5) / 5, ponctuation comprise
    assert metrics["avg_sentence_length"] == pytest.approx(2.5)


@pytest.mark.parametrize("words,avg,expected", [
    (0, 5, 1),          # minimum d'une minute
    (200, 10, 1),
    (201, 10, 2),
    (400, 20, 2),       # 20 n'est pas supérieur à 20 : vitesse normale
    (300, 21, 2),       # phrases longues : 150 mots par minute
    (301, 21, 3),
])
def test_estimate_duration(analyzer, words, avg, expected):
    assert analyzer._estimate_duration("", {"word_count": words, "avg_sentence_length": avg}) == expected


# ── analyze ─────────────────────────────────────────────────────────────────

def test_analyze_returns_all_fields_without_loading_a_model(analyzer):
    result = analyzer.analyze("Introduction aux matrices. Ce cours présente les bases de l'algèbre.", {"theme": "Maths"})
    assert set(result) == {"difficulty_score", "difficulty_level", "estimated_duration_minutes", "tags",
                           "complexity_metrics", "embedding"}
    assert result["difficulty_level"] == "facile"
    assert result["estimated_duration_minutes"] == 1
    assert "maths" in result["tags"] and "mathématiques" in result["tags"]
    assert result["embedding"] is None
    assert analyzer.model is None
    assert result["difficulty_score"] == round(result["difficulty_score"], 2)


def test_analyze_without_metadata(analyzer):
    assert analyzer.analyze("Un texte sans mot clé particulier ici")["tags"] == []


# ── faux modèle déterministe ────────────────────────────────────────────────

VECTORS = {"a": [1.0, 0.0], "b": [0.0, 1.0], "c": [1.0, 1.0]}


class FakeEncoding:
    def __init__(self, text):
        self.text = text

    def to(self, device):
        return {"text": self.text}


class FakeTokenizer:
    def __init__(self):
        self.calls = []

    def __call__(self, text, **kwargs):
        self.calls.append((text, kwargs))
        return FakeEncoding(text)


def fake_model(text):
    texts = [text] if isinstance(text, str) else list(text)
    hidden = torch.tensor([[VECTORS[t]] for t in texts])   # (n, 1, 2) : jeton CLS seul
    return SimpleNamespace(last_hidden_state=hidden)


@pytest.fixture
def loaded(analyzer):
    analyzer.tokenizer = FakeTokenizer()
    analyzer.model = lambda **kw: fake_model(kw["text"])
    return analyzer


def test_embedding_is_the_cls_vector_as_python_list(loaded):
    assert loaded._compute_bert_embedding("c") == [1.0, 1.0]
    text, kwargs = loaded.tokenizer.calls[0]
    assert kwargs["truncation"] is True and kwargs["max_length"] == 512


def test_analyze_computes_embedding_only_on_request(loaded):
    assert loaded.analyze("a", {"compute_embedding": True})["embedding"] == [1.0, 0.0]
    assert loaded.analyze("a", {"compute_embedding": False})["embedding"] is None


def test_embedding_is_none_when_model_cannot_load(analyzer):
    analyzer.load_failed = True
    assert analyzer._compute_bert_embedding("a") is None


def test_similarity_is_cosine(loaded):
    assert loaded.compute_similarity("a", "a") == pytest.approx(1.0)
    assert loaded.compute_similarity("a", "b") == pytest.approx(0.0)
    assert loaded.compute_similarity("a", "c") == pytest.approx(1 / np.sqrt(2))
    assert isinstance(loaded.compute_similarity("a", "c"), float)


def test_similarity_without_model_raises_clear_error(analyzer):
    analyzer.load_failed = True
    with pytest.raises(RuntimeError, match="similarité impossible"):
        analyzer.compute_similarity("a", "b")


def test_batch_encode_splits_into_batches_and_stacks_rows(loaded):
    matrix = loaded.batch_encode(["a", "b", "c"], batch_size=2)
    assert matrix.shape == (3, 2)
    assert matrix.tolist() == [[1.0, 0.0], [0.0, 1.0], [1.0, 1.0]]
    assert [len(call[0]) for call in loaded.tokenizer.calls] == [2, 1]


def test_batch_encode_without_model_raises(analyzer):
    analyzer.load_failed = True
    with pytest.raises(RuntimeError, match="encodage impossible"):
        analyzer.batch_encode(["a"])


def test_model_size_in_megabytes(analyzer):
    assert analyzer._get_model_size() == 0
    analyzer.model = torch.nn.Linear(2, 2)   # 4 poids + 2 biais en float32 = 24 octets
    assert analyzer._get_model_size() == pytest.approx(24 / (1024 ** 2))
