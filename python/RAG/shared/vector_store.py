"""
Client Qdrant partagé par les deux moteurs (Phase 2, §2.3-2.4) — confirmé
hors périmètre "PyTorch uniquement" : c'est une base de données auto-hébergée
en conteneur Docker, pas une librairie de deep learning.

self_hosted et api utilisent la MÊME instance Qdrant (une seule à opérer
pendant la phase de construction) mais des collections distinctes, puisque
les dimensions de vecteurs diffèrent (Qwen3-Embedding-8B: 3072 ; Cohere
embed-v4: 1536 par défaut).
"""

from __future__ import annotations

import os
from typing import Any, Dict, List, Optional

from qdrant_client import QdrantClient
from qdrant_client.http import models as qm

from RAG.shared.contracts import Chunk

QDRANT_URL = os.getenv("QDRANT_URL", "http://localhost:6333")
QDRANT_API_KEY = os.getenv("QDRANT_API_KEY") or None

_client: Optional[QdrantClient] = None


def get_client() -> QdrantClient:
    global _client
    if _client is None:
        _client = QdrantClient(url=QDRANT_URL, api_key=QDRANT_API_KEY)
    return _client


def ensure_collection(collection: str, vector_size: int) -> None:
    client = get_client()
    existing = [c.name for c in client.get_collections().collections]
    if collection in existing:
        return
    client.create_collection(
        collection_name=collection,
        vectors_config=qm.VectorParams(size=vector_size, distance=qm.Distance.COSINE),
    )
    # Index payload pour un filtrage sans surcoût de latence (équivalent du
    # filtre RBAC natif décrit en Phase 2 §2.4).
    for field_name, schema in (
        ("subject_id", qm.PayloadSchemaType.INTEGER),
        ("course_id", qm.PayloadSchemaType.INTEGER),
        ("status", qm.PayloadSchemaType.KEYWORD),
        ("doc_id", qm.PayloadSchemaType.KEYWORD),
    ):
        client.create_payload_index(collection, field_name=field_name, field_schema=schema)


def upsert_chunks(collection: str, chunks: List[Chunk], vectors: List[List[float]]) -> None:
    client = get_client()
    points = [
        qm.PointStruct(
            id=c.chunk_id,
            vector=v,
            payload={
                "text": c.text,
                "parent_text": c.parent_text,
                **c.metadata.model_dump(),
            },
        )
        for c, v in zip(chunks, vectors)
    ]
    client.upsert(collection_name=collection, points=points)


def build_filter(filters: Dict[str, Any]) -> Optional[qm.Filter]:
    """Construit un filtre Qdrant depuis le dict générique reçu dans
    RAGQueryRequest.filters. Le module reste agnostique des règles d'accès
    WinPlus : il applique tel quel ce que l'appelant lui donne."""
    if not filters:
        return None
    must = []
    for key, value in filters.items():
        if isinstance(value, list):
            must.append(qm.FieldCondition(key=key, match=qm.MatchAny(any=value)))
        else:
            must.append(qm.FieldCondition(key=key, match=qm.MatchValue(value=value)))
    return qm.Filter(must=must)


def search_dense(
    collection: str,
    vector: List[float],
    top_k: int = 10,
    filters: Optional[Dict[str, Any]] = None,
) -> List[qm.ScoredPoint]:
    # `query_points` plutôt que `search` (déprécié dans qdrant-client — les
    # méthodes search/search_batch/recommend* seront retirées côté serveur
    # à partir de Qdrant v1.18).
    client = get_client()
    result = client.query_points(
        collection_name=collection,
        query=vector,
        limit=top_k,
        query_filter=build_filter(filters or {}),
        with_payload=True,
    )
    return result.points


def scroll_by_filter(collection: str, filters: Dict[str, Any], limit: int = 20) -> List[qm.Record]:
    """Récupération sans vecteur de requête, filtrée sur les métadonnées —
    utilisé par GraphRAG pour remonter les chunks des documents connectés
    par le graphe (Phase 3, §3.7), en dehors de toute similarité cosinus."""
    client = get_client()
    records, _ = client.scroll(
        collection_name=collection,
        scroll_filter=build_filter(filters),
        limit=limit,
        with_payload=True,
    )
    return records


def get_by_ids(collection: str, ids: List[str]) -> List[qm.Record]:
    if not ids:
        return []
    client = get_client()
    return client.retrieve(collection_name=collection, ids=ids, with_payload=True)


def mark_superseded(collection: str, doc_id: str, superseded_by: str) -> None:
    """Gestion des versions documentaires (Phase 1, §1.9) : les chunks de
    l'ancienne version passent en `status=superseded` plutôt que d'être
    supprimés, pour rester consultables par un administrateur."""
    client = get_client()
    client.set_payload(
        collection_name=collection,
        payload={"status": "superseded", "superseded_by": superseded_by},
        points=qm.Filter(must=[qm.FieldCondition(key="doc_id", match=qm.MatchValue(value=doc_id))]),
    )
