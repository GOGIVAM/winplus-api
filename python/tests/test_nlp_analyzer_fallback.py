"""NLPAnalyzer : un modèle introuvable ne doit jamais faire tomber le service.

Incident du 2026-10-21 (intégration) : NLP_MODEL=fr_core_news_sm, le repli
distilbert échouait aussi et load_model() se rappelait sans fin jusqu'à
l'arrêt de FastAPI par le noyau (mémoire épuisée), d'où un 503 sur
/api/ai/success-prediction.
"""

import pytest

from models import nlp_analyzer as nlp_module
from models.nlp_analyzer import NLPAnalyzer


class _Refuse:
    """Remplace une classe transformers : compte les appels, échoue toujours."""

    def __init__(self):
        self.calls = 0

    def from_pretrained(self, *args, **kwargs):
        self.calls += 1
        raise OSError("Unable to load vocabulary from file.")


@pytest.fixture
def refused(monkeypatch, tmp_path):
    monkeypatch.setenv("MODEL_CACHE_DIR", str(tmp_path))
    loaders = {name: _Refuse() for name in ("CamembertTokenizer", "CamembertModel", "AutoTokenizer", "AutoModel")}
    for name, loader in loaders.items():
        monkeypatch.setattr(nlp_module, name, loader)
    return loaders


def test_analyze_sans_embedding_ne_charge_pas_le_modele(refused):
    analyzer = NLPAnalyzer(model_name="fr_core_news_sm")
    result = analyzer.analyze("Introduction aux matrices. Les bases de l'algèbre.", {"theme": "maths"})

    assert result["difficulty_level"] in {"facile", "moyen", "difficile"}
    assert result["embedding"] is None
    assert all(loader.calls == 0 for loader in refused.values())


def test_echec_du_modele_et_du_repli_sans_boucle(refused):
    analyzer = NLPAnalyzer(model_name="fr_core_news_sm")

    first = analyzer.analyze("Optimisation avancée.", {"compute_embedding": True})
    second = analyzer.analyze("Optimisation avancée.", {"compute_embedding": True})

    assert first["embedding"] is None and second["embedding"] is None
    assert analyzer.load_failed
    # Un essai du modèle demandé, un essai du repli, puis plus rien.
    assert refused["CamembertTokenizer"].calls == 1
    assert refused["AutoTokenizer"].calls == 1


def test_similarite_refusee_proprement_sans_modele(refused):
    analyzer = NLPAnalyzer(model_name="fr_core_news_sm")
    with pytest.raises(RuntimeError):
        analyzer.compute_similarity("a", "b")
    with pytest.raises(RuntimeError):
        analyzer.batch_encode(["a"])
