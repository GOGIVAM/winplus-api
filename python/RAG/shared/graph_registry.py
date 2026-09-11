"""
Persistance du graphe de connaissances GraphRAG entre les appels d'ingestion
et de requête — même principe que BM25Registry : un graphe par collection,
sérialisé sur disque (pickle) pour survivre aux redémarrages.
"""

from __future__ import annotations

import os
import pickle
from threading import Lock
from typing import Dict

import networkx as nx


class GraphRegistry:
    _graphs: Dict[str, nx.DiGraph] = {}
    _lock = Lock()
    _storage_dir = os.getenv("RAG_GRAPH_STORAGE_DIR", "./RAG/.graph_storage")

    @classmethod
    def get(cls, collection: str) -> nx.DiGraph:
        with cls._lock:
            if collection not in cls._graphs:
                cls._graphs[collection] = cls._load(collection)
            return cls._graphs[collection]

    @classmethod
    def add_triples(cls, collection: str, triples) -> None:
        graph = cls.get(collection)
        with cls._lock:
            for t in triples:
                graph.add_node(t.subject)
                graph.add_node(t.obj)
                graph.add_edge(t.subject, t.obj, relation=t.relation, doc_id=t.doc_id)
            cls._save(collection, graph)

    @classmethod
    def _path(cls, collection: str) -> str:
        return os.path.join(cls._storage_dir, f"{collection}.gpickle")

    @classmethod
    def _load(cls, collection: str) -> nx.DiGraph:
        path = cls._path(collection)
        if os.path.exists(path):
            with open(path, "rb") as f:
                return pickle.load(f)
        return nx.DiGraph()

    @classmethod
    def _save(cls, collection: str, graph: nx.DiGraph) -> None:
        path = cls._path(collection)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "wb") as f:
            pickle.dump(graph, f)
