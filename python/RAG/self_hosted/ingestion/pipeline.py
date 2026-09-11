"""
Orchestration de la Phase 1 complète pour un document — classification,
extraction (native ou OCR conditionnel), tableaux, images embarquées,
post-correction, chunking, métadonnées.
"""

from __future__ import annotations

import logging
from typing import List

import fitz

from RAG.self_hosted.ingestion import table_extractor
from RAG.self_hosted.ingestion.chunking import chunk_text
from RAG.self_hosted.ingestion.lexicon_correction import correct_text
from RAG.self_hosted.ingestion.ocr_engine import caption_embedded_image, ocr_transcribe
from RAG.self_hosted.ingestion.stamp_detector import has_stamp
from RAG.shared.contracts import Chunk, ChunkMetadata, ChunkType, IngestRequest, SourceType
from RAG.shared.pdf_utils import extract_native_text, page_count, render_page_image

logger = logging.getLogger(__name__)


def _extract_embedded_images(pdf_path: str, page_number: int) -> List[bytes]:
    doc = fitz.open(pdf_path)
    try:
        page = doc[page_number - 1]
        images = []
        for img in page.get_images(full=True):
            xref = img[0]
            base = doc.extract_image(xref)
            images.append(base["image"])
        return images
    finally:
        doc.close()


def process_document(request: IngestRequest) -> tuple[List[Chunk], SourceType, List[str]]:
    warnings: List[str] = []
    extraction = extract_native_text(request.file_path)
    n_pages = page_count(request.file_path)
    chunks: List[Chunk] = []

    if extraction.is_native:
        source_type = SourceType.PDF_NATIVE
        for page in extraction.pages:
            if not page.text.strip():
                continue
            corrected = correct_text(page.text)
            for chunk_pair in chunk_text(corrected, page=page.page_number):
                chunks.append(_to_chunk(chunk_pair, request, ChunkType.SHORT))

            for image_bytes in _extract_embedded_images(request.file_path, page.page_number):
                try:
                    caption = caption_embedded_image(image_bytes)
                    chunks.append(
                        Chunk(
                            chunk_id=f"{request.doc_id}_p{page.page_number}_img_{len(chunks)}",
                            text=caption,
                            metadata=_metadata(request, page.page_number, ChunkType.IMAGE_CAPTION),
                        )
                    )
                except Exception as e:
                    warnings.append(f"Page {page.page_number}: échec captioning image ({e})")
    else:
        source_type = SourceType.PDF_SCANNED
        for page_number in range(1, n_pages + 1):
            image_bytes = render_page_image(request.file_path, page_number)

            try:
                for table_box in table_extractor.detect_tables(image_bytes):
                    table = table_extractor.extract_table_structure(image_bytes, table_box)
                    chunks.append(
                        Chunk(
                            chunk_id=f"{request.doc_id}_p{page_number}_table_{len(chunks)}",
                            text=table.to_text(),
                            metadata=_metadata(
                                request, page_number, ChunkType.CELL, extra={"table_json": table.to_json()}
                            ),
                        )
                    )
            except Exception as e:
                warnings.append(f"Page {page_number}: échec extraction tableau ({e})")

            try:
                raw_text = ocr_transcribe(image_bytes)
            except Exception as e:
                warnings.append(f"Page {page_number}: échec OCR ({e})")
                continue

            if has_stamp(image_bytes):
                # GLM-OCR est déjà le moteur utilisé ci-dessus pour les deux
                # cas (remplace le routage PaddleOCR-VL/GLM-OCR à deux
                # moteurs du référentiel original) ; on garde la détection
                # de tampon comme signal de qualité tracé dans les warnings.
                warnings.append(f"Page {page_number}: tampon détecté (traité par le moteur OCR unique)")

            corrected = correct_text(raw_text)
            for chunk_pair in chunk_text(corrected, page=page_number):
                chunks.append(_to_chunk(chunk_pair, request, ChunkType.SHORT))

    return chunks, source_type, warnings


def _to_chunk(chunk_pair, request: IngestRequest, chunk_type: ChunkType) -> Chunk:
    return Chunk(
        chunk_id=chunk_pair.chunk_id,
        text=chunk_pair.short_text,
        parent_text=chunk_pair.long_text,
        metadata=ChunkMetadata(
            doc_id=request.doc_id,
            title=request.title,
            category=request.category,
            subject_id=request.subject_id,
            course_id=request.course_id,
            lesson_id=request.lesson_id,
            page=chunk_pair.page,
            section=chunk_pair.section,
            chunk_type=chunk_type,
        ),
    )


def _metadata(request: IngestRequest, page: int, chunk_type: ChunkType, extra: dict | None = None) -> ChunkMetadata:
    return ChunkMetadata(
        doc_id=request.doc_id,
        title=request.title,
        category=request.category,
        subject_id=request.subject_id,
        course_id=request.course_id,
        lesson_id=request.lesson_id,
        page=page,
        chunk_type=chunk_type,
        extra=extra or {},
    )
