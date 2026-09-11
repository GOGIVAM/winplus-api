"""
Orchestration de l'ingestion côté api — PDF (natif ou scanné via Mistral
OCR), images embarquées (vision Gemini 2.5 Flash), et vidéos de formation
(transcription Groq Whisper) : c'est ce dernier point qui répond à la
demande initiale d'ingérer le contenu des vidéos de cours.
"""

from __future__ import annotations

import logging
import os
import uuid
from typing import List

import fitz

from RAG.api.ingestion.chunking import chunk_text
from RAG.api.ingestion.ocr_client import ocr_pdf
from RAG.api.ingestion.transcription_client import transcribe_video
from RAG.api.ingestion.vision_client import caption_image
from RAG.shared.contracts import Chunk, ChunkMetadata, ChunkType, IngestRequest, SourceType
from RAG.shared.pdf_utils import extract_native_text

logger = logging.getLogger(__name__)

VIDEO_EXTENSIONS = {".mp4", ".mov", ".mkv", ".webm", ".avi"}


def _extract_embedded_images(pdf_path: str) -> List[bytes]:
    doc = fitz.open(pdf_path)
    try:
        images = []
        for page in doc:
            for img in page.get_images(full=True):
                base = doc.extract_image(img[0])
                images.append(base["image"])
        return images
    finally:
        doc.close()


def _process_pdf(request: IngestRequest) -> tuple[List[Chunk], SourceType, List[str]]:
    warnings: List[str] = []
    extraction = extract_native_text(request.file_path)
    chunks: List[Chunk] = []

    if extraction.is_native:
        source_type = SourceType.PDF_NATIVE
        for page in extraction.pages:
            if not page.text.strip():
                continue
            for pair in chunk_text(page.text, page=page.page_number):
                chunks.append(_to_chunk(pair, request, ChunkType.SHORT))

        for image_bytes in _extract_embedded_images(request.file_path):
            try:
                caption = caption_image(image_bytes)
                chunks.append(
                    Chunk(
                        chunk_id=str(uuid.uuid4()),
                        text=caption,
                        metadata=_metadata(request, None, ChunkType.IMAGE_CAPTION),
                    )
                )
            except Exception as e:
                warnings.append(f"Échec captioning image ({e})")
    else:
        source_type = SourceType.PDF_SCANNED
        try:
            full_text = ocr_pdf(request.file_path)
        except Exception as e:
            warnings.append(f"Échec OCR Mistral ({e})")
            full_text = ""

        for page_block in full_text.split("--- page "):
            if not page_block.strip():
                continue
            try:
                page_num_str, text = page_block.split("---\n", 1)
                page_num = int(page_num_str.strip())
            except ValueError:
                page_num, text = None, page_block
            for pair in chunk_text(text, page=page_num):
                chunks.append(_to_chunk(pair, request, ChunkType.SHORT))

    return chunks, source_type, warnings


def _process_video(request: IngestRequest) -> tuple[List[Chunk], SourceType, List[str]]:
    warnings: List[str] = []
    chunks: List[Chunk] = []

    try:
        segments = transcribe_video(request.file_path)
    except Exception as e:
        return [], SourceType.VIDEO, [f"Échec transcription ({e})"]

    # Regroupe les segments Whisper en fenêtres ~128 tokens pour rester
    # cohérent avec la double granularité utilisée sur les documents.
    buffer_text = ""
    buffer_start = None
    for seg in segments:
        if buffer_start is None:
            buffer_start = seg.start
        buffer_text += " " + seg.text
        if len(buffer_text.split()) >= 90:
            chunks.append(
                Chunk(
                    chunk_id=str(uuid.uuid4()),
                    text=buffer_text.strip(),
                    metadata=_metadata(
                        request, None, ChunkType.VIDEO_SEGMENT,
                        extra={"timestamp_start": buffer_start, "timestamp_end": seg.end},
                    ),
                )
            )
            buffer_text, buffer_start = "", None

    if buffer_text.strip():
        chunks.append(
            Chunk(
                chunk_id=str(uuid.uuid4()),
                text=buffer_text.strip(),
                metadata=_metadata(
                    request, None, ChunkType.VIDEO_SEGMENT,
                    extra={"timestamp_start": buffer_start, "timestamp_end": segments[-1].end if segments else 0},
                ),
            )
        )

    return chunks, SourceType.VIDEO, warnings


def process_document(request: IngestRequest) -> tuple[List[Chunk], SourceType, List[str]]:
    ext = os.path.splitext(request.file_path)[1].lower()
    if ext in VIDEO_EXTENSIONS:
        return _process_video(request)
    return _process_pdf(request)


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


def _metadata(request: IngestRequest, page, chunk_type: ChunkType, extra: dict | None = None) -> ChunkMetadata:
    return ChunkMetadata(
        doc_id=request.doc_id,
        title=request.title,
        category=request.category,
        subject_id=request.subject_id,
        course_id=request.course_id,
        lesson_id=request.lesson_id,
        page=page,
        chunk_type=chunk_type,
        timestamp_start=(extra or {}).get("timestamp_start"),
        timestamp_end=(extra or {}).get("timestamp_end"),
        extra=extra or {},
    )
