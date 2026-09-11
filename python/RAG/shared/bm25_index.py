"""
Index BM25 (recherche lexicale éparse, Phase 2 §2.9) — `rank_bm25` est une
implémentation pure Python de l'algorithme, pas un framework ML : elle reste
hors du périmètre "PyTorch uniquement" au même titre que Qdrant.

Un index par collection, persisté sur disque (pickle) pour survivre aux
redémarrages sans reconstruire depuis Qdrant à chaque fois.
"""

from __future__ import annotations

import os
import pickle
import re
from dataclasses import dataclass, field
from threading import Lock
from typing import Dict, List

from rank_bm25 import BM25Okapi

_TOKEN_RE = re.compile(r"\w+", re.UNICODE)


def _tokenize(text: str) -> List[str]:
    return _TOKEN_RE.findall(text.lower())


@dataclass
class BM25Index:
    chunk_ids: List[str] = field(default_factory=list)
    corpus_tokens: List[List[str]] = field(default_factory=list)
    _bm25: BM25Okapi = None
    _lock: Lock = field(default_factory=Lock)

    def add(self, chunk_id: str, text: str) -> None:
        with self._lock:
            self.chunk_ids.append(chunk_id)
            self.corpus_tokens.append(_tokenize(text))
            self._bm25 = BM25Okapi(self.corpus_tokens) if self.corpus_tokens else None

    def add_many(self, items: List[tuple]) -> None:
        with self._lock:
            for chunk_id, text in items:
                self.chunk_ids.append(chunk_id)
                self.corpus_tokens.append(_tokenize(text))
            self._bm25 = BM25Okapi(self.corpus_tokens) if self.corpus_tokens else None

    def search(self, query: str, top_k: int = 20) -> List[str]:
        if not self._bm25:
            return []
        scores = self._bm25.get_scores(_tokenize(query))
        ranked = sorted(range(len(scores)), key=lambda i: scores[i], reverse=True)
        return [self.chunk_ids[i] for i in ranked[:top_k] if scores[i] > 0]

    def save(self, path: str) -> None:
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "wb") as f:
            pickle.dump({"chunk_ids": self.chunk_ids, "corpus_tokens": self.corpus_tokens}, f)

    @classmethod
    def load(cls, path: str) -> "BM25Index":
        if not os.path.exists(path):
            return cls()
        with open(path, "rb") as f:
            data = pickle.load(f)
        idx = cls(chunk_ids=data["chunk_ids"], corpus_tokens=data["corpus_tokens"])
        idx._bm25 = BM25Okapi(idx.corpus_tokens) if idx.corpus_tokens else None
        return idx


class BM25Registry:
    """Un index BM25 par collection (self_hosted / api partagent le
    registre mais utilisent des noms de collection distincts)."""

    _indexes: Dict[str, BM25Index] = {}
    _storage_dir = os.getenv("RAG_BM25_STORAGE_DIR", "./RAG/.bm25_storage")

    @classmethod
    def get(cls, collection: str) -> BM25Index:
        if collection not in cls._indexes:
            cls._indexes[collection] = BM25Index.load(cls._path(collection))
        return cls._indexes[collection]

    @classmethod
    def persist(cls, collection: str) -> None:
        if collection in cls._indexes:
            cls._indexes[collection].save(cls._path(collection))

    @classmethod
    def _path(cls, collection: str) -> str:
        return os.path.join(cls._storage_dir, f"{collection}.pkl")
