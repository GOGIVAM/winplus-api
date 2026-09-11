"""
Suivi de l'état des ingestions lancées en arrière-plan (voir RAG/router.py).

Registre en mémoire — suffisant pour la phase de test actuelle (un seul
worker FastAPI). À faire évoluer vers une table Postgres si l'ingestion doit
survivre à un redémarrage du service ou être visible depuis plusieurs
workers (voir DEPLOYMENT.md).
"""

from __future__ import annotations

from threading import Lock
from typing import Dict, Optional

from RAG.shared.contracts import IngestJobRecord, IngestJobStatus, IngestResult


class IngestJobRegistry:
    _jobs: Dict[str, IngestJobRecord] = {}
    _lock = Lock()

    @classmethod
    def set_queued(cls, doc_id: str) -> None:
        with cls._lock:
            cls._jobs[doc_id] = IngestJobRecord(doc_id=doc_id, status=IngestJobStatus.QUEUED)

    @classmethod
    def set_processing(cls, doc_id: str) -> None:
        with cls._lock:
            cls._jobs[doc_id] = IngestJobRecord(doc_id=doc_id, status=IngestJobStatus.PROCESSING)

    @classmethod
    def set_done(cls, doc_id: str, result: IngestResult) -> None:
        with cls._lock:
            cls._jobs[doc_id] = IngestJobRecord(doc_id=doc_id, status=IngestJobStatus.DONE, result=result)

    @classmethod
    def set_failed(cls, doc_id: str, error: str) -> None:
        with cls._lock:
            cls._jobs[doc_id] = IngestJobRecord(doc_id=doc_id, status=IngestJobStatus.FAILED, error=error)

    @classmethod
    def get(cls, doc_id: str) -> Optional[IngestJobRecord]:
        return cls._jobs.get(doc_id)
