"""
Script one-shot de ré-ingestion du contenu déjà présent dans WinPlus (topo
validé avec l'utilisateur, point 2) — à lancer manuellement, UNE fois,
après le branchement de RAG au chat. Sans ce script, WinAI ne "connaît"
que le contenu uploadé APRES le branchement (voir AdminExamsController,
AdminLibraryController, TeacherCourseController qui déclenchent l'ingestion
au fil de l'eau pour tout nouvel upload).

Usage :
    cd backend/python
    python -m RAG.scripts.backfill_existing_content --dry-run   # compte et liste, n'ingère rien
    python -m RAG.scripts.backfill_existing_content             # ingère réellement
    python -m RAG.scripts.backfill_existing_content --limit 20  # test sur un échantillon

Le dry-run sert à estimer le coût AVANT de lancer (chaque document consomme
des appels Mistral OCR / Groq Whisper / Cohere embedding côté RAG/api) —
voir RAG/DEPLOYMENT.md §6 pour les ordres de grandeur.

Exécuté en série, volontairement : un backfill n'est pas sensible à la
latence comme le flux d'upload au fil de l'eau (voir RAG/router.py), pas
besoin de parallélisme qui compliquerait le rate-limiting des API tierces.
"""

from __future__ import annotations

import argparse
import logging
import sys
import time

from sqlalchemy import text

from database import CourseContent, Database, Exam
from RAG.shared.config import RAG_BACKEND
from RAG.shared.contracts import IngestRequest
from RAG.shared.file_resolver import resolve_local_path

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
logger = logging.getLogger("rag.backfill")


def _active_backend():
    if RAG_BACKEND == "self_hosted":
        from RAG.self_hosted.indexing.pipeline import index_chunks
        from RAG.self_hosted.ingestion.pipeline import process_document
        from RAG.self_hosted.config import QDRANT_COLLECTION
    else:
        from RAG.api.indexing.pipeline import index_chunks
        from RAG.api.ingestion.pipeline import process_document
        from RAG.api.config import QDRANT_COLLECTION
    return process_document, index_chunks, QDRANT_COLLECTION


def _supersede_previous_version(collection: str, doc_id: str) -> None:
    """Voir RAG/router.py::_supersede_previous_version  même logique, pour
    qu'un ré-exécution de ce script (ex: après restauration Qdrant) ne
    duplique pas indéfiniment le corpus."""
    from RAG.shared.vector_store import mark_superseded

    try:
        mark_superseded(collection, doc_id, superseded_by=doc_id)
    except Exception:
        pass


def _collect_items(session, limit: int | None) -> list[IngestRequest]:
    items: list[IngestRequest] = []

    exams = (
        session.query(Exam)
        .filter(Exam.IsDeleted == False, Exam.DocumentUrl.isnot(None), Exam.DocumentUrl != "")
        .all()
    )
    for e in exams:
        items.append(IngestRequest(
            doc_id=f"examdoc_{e.Id}",
            title=e.Title,
            file_path=e.DocumentUrl,
            category=e.Category,
            subject_id=e.SubjectId,
        ))

    contents = session.query(CourseContent).all()
    for c in contents:
        file_url = c.VideoUrl or c.DocumentUrl
        if not file_url:
            continue
        items.append(IngestRequest(
            doc_id=f"coursecontent_{c.Id}",
            title=c.Title,
            file_path=file_url,
            subject_id=c.SubjectId,
            course_id=c.Id,
        ))

    # CourseLessons n'a pas de modèle SQLAlchemy côté Python (table gérée
    # uniquement par EF Core aujourd'hui) — requête brute plutôt qu'ajouter
    # un modèle ORM complet pour un usage ponctuel de ce script.
    rows = session.execute(text(
        'SELECT "Id", "Title", "VideoUrl", "FileUrl", "CourseId" FROM "CourseLessons" '
        'WHERE ("VideoUrl" IS NOT NULL AND "VideoUrl" != \'\') '
        'OR ("FileUrl" IS NOT NULL AND "FileUrl" != \'\')'
    )).fetchall()
    for row in rows:
        file_url = row.VideoUrl or row.FileUrl
        items.append(IngestRequest(
            doc_id=f"lesson_{row.Id}",
            title=row.Title,
            file_path=file_url,
            course_id=row.CourseId,
            lesson_id=row.Id,
        ))

    if limit:
        items = items[:limit]
    return items


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dry-run", action="store_true", help="Compte et liste sans ingérer")
    parser.add_argument("--limit", type=int, default=None, help="Ne traiter que les N premiers éléments")
    args = parser.parse_args()

    db = Database()
    session = db.SessionLocal()
    try:
        items = _collect_items(session, args.limit)
    finally:
        session.close()

    logger.info(f"{len(items)} document(s)/vidéo(s) à ingérer (backend={RAG_BACKEND})")
    if args.dry_run:
        for it in items:
            logger.info(f"  [DRY-RUN] {it.doc_id}  {it.title}  {it.file_path}")
        logger.info("Dry-run terminé, rien n'a été ingéré. Relancer sans --dry-run pour ingérer réellement.")
        return 0

    process_document, index_chunks, collection = _active_backend()

    succeeded, failed = 0, []
    start = time.time()
    for i, request in enumerate(items, start=1):
        logger.info(f"[{i}/{len(items)}] Ingestion {request.doc_id} ({request.title})...")
        try:
            _supersede_previous_version(collection, request.doc_id)
            # resolve_local_path : les URL de ce script sont des URL S3
            # publiques, que fitz/whisper ne savent pas ouvrir directement.
            with resolve_local_path(request) as local_path:
                resolved_request = request.model_copy(update={"file_path": local_path})
                chunks, source_type, warnings = process_document(resolved_request)
            warnings = warnings + index_chunks(chunks)
            succeeded += 1
            logger.info(f"  -> OK : {len(chunks)} chunks ({source_type.value}){' - ' + '; '.join(warnings) if warnings else ''}")
        except Exception as e:
            failed.append((request.doc_id, str(e)))
            logger.error(f"  -> ÉCHEC {request.doc_id} : {e}")

    elapsed = time.time() - start
    logger.info(f"Terminé en {elapsed:.1f}s  {succeeded} succès, {len(failed)} échec(s)")
    if failed:
        logger.warning("Documents en échec (à ré-essayer manuellement après correction) :")
        for doc_id, err in failed:
            logger.warning(f"  - {doc_id} : {err}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
