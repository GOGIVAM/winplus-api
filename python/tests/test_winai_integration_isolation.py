"""Environnement d'intégration de WinAI : isolation des données, plafond quotidien, mode simulé.

Preuve demandée : une requête de l'intégration n'atteint ni la base ni l'index de
production. Les variables d'environnement reproduisent
infrastructure/integration/env/fastapi.env.example.
"""

import importlib
import os
import sys
import types

import pytest

INTEGRATION_ENV = {
    "WINPLUS_ENV": "integration",
    "DB_HOST": "172.31.2.201",
    "DB_NAME": "winplus_integration",
    "DB_USER": "cademi",
    "DB_PASSWORD": "x",
    "RAG_API_COLLECTION": "integration_winplus_api",
    "RAG_SH_COLLECTION": "integration_winplus_self_hosted",
    "RAG_BM25_STORAGE_DIR": "/home/ubuntu/winplus-integration/rag/bm25",
    "RAG_GRAPH_STORAGE_DIR": "/home/ubuntu/winplus-integration/rag/graph",
    "WINAI_USAGE_DIR": None,  # rempli par la fixture (dossier temporaire)
    "WINAI_DAILY_TOKEN_CAP": "1000",
    "WINAI_TRANSLATION_DAILY_TOKEN_CAP": "300",
    "WINAI_SIMULATED": "false",
}


@pytest.fixture
def integration(monkeypatch, tmp_path):
    for k, v in INTEGRATION_ENV.items():
        monkeypatch.setenv(k, v if v is not None else str(tmp_path / "winai-usage-integration"))
    import services.winai_environment as env
    return importlib.reload(env)


@pytest.fixture
def production(monkeypatch, tmp_path):
    for k in INTEGRATION_ENV:
        monkeypatch.delenv(k, raising=False)
    monkeypatch.setenv("WINAI_USAGE_DIR", str(tmp_path / "usage"))
    import services.winai_environment as env
    return importlib.reload(env)


# ── Isolation ────────────────────────────────────────────────────────────

def test_integration_config_is_isolated(integration):
    assert integration.isolation_problems() == []
    integration.assert_isolation()


@pytest.mark.parametrize("var,value", [
    ("DB_NAME", "winplus_db"),
    ("DB_NAME", None),
    ("RAG_API_COLLECTION", "winplus_api"),
    ("RAG_SH_COLLECTION", None),
    ("RAG_BM25_STORAGE_DIR", "./RAG/.bm25_storage"),
    ("RAG_GRAPH_STORAGE_DIR", None),
])
def test_integration_refuses_any_production_resource(integration, monkeypatch, var, value):
    if value is None:
        monkeypatch.delenv(var, raising=False)
    else:
        monkeypatch.setenv(var, value)
    with pytest.raises(integration.IsolationError):
        integration.assert_isolation()


def test_production_is_unchanged(production):
    assert production.isolation_problems() == []
    production.assert_isolation()


def test_database_connects_to_integration_base_only(integration, monkeypatch):
    """La connexion SQLAlchemy de WinAI vise winplus_integration, jamais winplus_db."""
    import database
    urls = []
    monkeypatch.setattr(database, "create_engine", lambda url, **kw: urls.append(url) or types.SimpleNamespace())
    monkeypatch.setattr(database, "sessionmaker", lambda **kw: None)
    database.Database()
    assert len(urls) == 1
    assert urls[0].endswith("/winplus_integration")
    assert "/winplus_db" not in urls[0]


def test_rag_index_reads_and_writes_integration_collection_only(integration, monkeypatch):
    """Recherche hybride et index BM25 : uniquement la collection et le dossier d'intégration."""
    import RAG.api.config as rag_config
    import RAG.shared.bm25_index as bm25
    importlib.reload(rag_config)
    importlib.reload(bm25)
    import RAG.api.engine.pipeline as pipeline
    importlib.reload(pipeline)

    collections = []

    class FakeQdrant:
        def search(self, collection_name, **kw):
            collections.append(collection_name)
            return []

        def query_points(self, collection_name, **kw):
            collections.append(collection_name)
            return types.SimpleNamespace(points=[])

        def retrieve(self, collection_name, **kw):
            collections.append(collection_name)
            return []

    import RAG.shared.vector_store as vs
    monkeypatch.setattr(vs, "_client", FakeQdrant())
    paths = []
    monkeypatch.setattr(bm25.BM25Index, "load", classmethod(lambda cls, p: paths.append(p) or cls()))
    bm25.BM25Registry._indexes.clear()

    try:
        pipeline._hybrid_retrieve([0.0] * 4, "équation du second degré", {}, top_k=3)
    except Exception:
        pass  # seule la cible des lectures compte ici

    assert rag_config.QDRANT_COLLECTION == "integration_winplus_api"
    assert collections and set(collections) == {"integration_winplus_api"}
    assert paths and all("winplus-integration" in p and "integration_winplus_api" in p for p in paths)


# ── Plafond quotidien ────────────────────────────────────────────────────

def test_daily_cap_blocks_after_limit(integration):
    integration.ensure_budget()
    integration.add_usage(600)
    integration.ensure_budget()
    integration.add_usage(500)
    with pytest.raises(integration.WinAIDailyCapReached) as exc:
        integration.ensure_budget()
    assert "plafond" in exc.value.message("fr")
    assert "daily limit" in exc.value.message("en-GB")


def test_translation_has_its_own_lower_cap(integration):
    token = integration.set_category("translation")
    try:
        integration.add_usage(350)
        with pytest.raises(integration.WinAIDailyCapReached):
            integration.ensure_budget()
    finally:
        integration.reset_category(token)
    integration.ensure_budget()  # le chat reste disponible (350 < 1000)


def test_production_has_no_cap_and_writes_no_counter(production, tmp_path):
    production.add_usage(10 ** 9)
    production.ensure_budget()
    assert not (tmp_path / "usage").exists()


def test_deepseek_client_never_called_once_cap_reached(integration, monkeypatch):
    import services.deepseek_client as dc
    importlib.reload(dc)
    monkeypatch.setattr(dc.requests, "post", lambda *a, **k: pytest.fail("appel DeepSeek malgré le plafond"))
    integration.add_usage(5000)
    with pytest.raises(integration.WinAIDailyCapReached):
        dc.DeepSeekClient().chat([{"role": "user", "content": "Bonjour"}])


def test_cap_reached_returns_translated_429(integration):
    from fastapi import FastAPI
    from fastapi.testclient import TestClient
    app = FastAPI()
    integration.register_cap_handler(app)  # même enregistrement que app.py
    integration.add_usage(5000)

    @app.get("/__test_cap")
    def _cap():
        integration.ensure_budget()
        return {"ok": True}

    client = TestClient(app)
    r = client.get("/__test_cap", headers={"X-User-Locale": "en"})
    assert r.status_code == 429
    assert r.json()["code"] == "winai_daily_cap"
    assert "daily limit" in r.json()["message"]


# ── WinAI simulé ─────────────────────────────────────────────────────────

def test_simulated_mode_is_off_by_default(integration):
    assert integration.is_simulated() is False


def test_simulated_mode_answers_without_provider(integration, monkeypatch):
    monkeypatch.setenv("WINAI_SIMULATED", "true")
    import services.deepseek_client as dc
    importlib.reload(dc)
    monkeypatch.setattr(dc.requests, "post", lambda *a, **k: pytest.fail("appel DeepSeek en mode simulé"))
    result = dc.DeepSeekClient().chat([{"role": "user", "content": "Bonjour"}], json_mode=True)
    assert result["simulated"] is True
    assert "[WinAI simulé]" in result["content"]
    chunks = list(dc.DeepSeekClient().chat_stream([{"role": "user", "content": "Bonjour"}]))
    assert "WinAI simul" in chunks[0] and chunks[-1].startswith("data: [DONE]")


def test_simulated_mode_is_impossible_in_production(production, monkeypatch):
    monkeypatch.setenv("WINAI_SIMULATED", "true")
    assert production.is_simulated() is False
