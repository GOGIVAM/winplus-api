"""
Contrats partagés entre RAG/self_hosted et RAG/api.

Les deux moteurs implémentent la même interface d'entrée/sortie — c'est ce
qui permet au routeur unique (RAG/router.py) de les faire cohabiter derrière
UN SEUL endpoint, le choix du moteur n'étant qu'une variable d'environnement
(RAG_BACKEND) plutôt qu'un branchement différent côté appelant.
"""

from __future__ import annotations

from enum import Enum
from typing import Any, Dict, List, Optional

from pydantic import BaseModel, Field


class ChunkType(str, Enum):
    SHORT = "short"
    LONG = "long"
    CELL = "cell"
    IMAGE_CAPTION = "image_caption"
    GRAPH_SUMMARY = "graph_summary"
    VIDEO_SEGMENT = "video_segment"


class DocStatus(str, Enum):
    ACTIVE = "active"
    SUPERSEDED = "superseded"


class SourceType(str, Enum):
    PDF_NATIVE = "pdf_native"
    PDF_SCANNED = "pdf_scanned"
    IMAGE = "image"
    VIDEO = "video"


class ChunkMetadata(BaseModel):
    """Payload attaché à chaque chunk indexé dans Qdrant (Phase 1, §2.7 du
    référentiel KALATI-RAG, adapté au modèle de contenu WinPlus)."""

    doc_id: str
    title: str
    category: Optional[str] = None  # epreuve, correction, cours, formation_video...
    subject_id: Optional[int] = None
    course_id: Optional[int] = None
    lesson_id: Optional[int] = None
    page: Optional[int] = None
    section: Optional[str] = None
    chunk_type: ChunkType = ChunkType.SHORT
    status: DocStatus = DocStatus.ACTIVE
    superseded_by: Optional[str] = None
    # Uniquement pour chunk_type=video_segment (transcription horodatée).
    timestamp_start: Optional[float] = None
    timestamp_end: Optional[float] = None
    # Base de connaissance PERSONNELLE (topo validé) : un chunk avec
    # owner_user_id posé n'est retrouvable QUE pour cet utilisateur — voir
    # services/rag_chat_bridge.py. None = document public (catalogue).
    owner_user_id: Optional[int] = None
    # Score composite calculé à l'ingestion (voir RAG/shared/relevance_scoring.py) :
    # nouveauté vs corpus existant, qualité pédagogique jugée par LLM,
    # adéquation au sujet déclaré. Utilisé pour prioriser le retrieval.
    relevance_score: Optional[float] = None
    extra: Dict[str, Any] = Field(default_factory=dict)


class Chunk(BaseModel):
    chunk_id: str
    text: str
    parent_text: Optional[str] = None  # chunk long correspondant (parent-child chunking)
    metadata: ChunkMetadata


class IngestRequest(BaseModel):
    doc_id: str
    title: str
    # URL (http/https, téléchargée), chemin local, ou vide si
    # inline_content_base64 est fourni (pièce jointe de chat, jamais passée
    # par S3) — voir RAG/shared/file_resolver.py qui résout ce champ vers un
    # chemin local utilisable par fitz/whisper quel que soit le cas.
    file_path: str = ""
    inline_content_base64: Optional[str] = None
    # Requis si file_path est vide (inline_content_base64 seul ne dit pas
    # quel type de fichier décoder) : ".pdf", ".mp4", etc.
    file_extension_hint: Optional[str] = None
    category: Optional[str] = None
    subject_id: Optional[int] = None
    course_id: Optional[int] = None
    lesson_id: Optional[int] = None
    # Posé automatiquement par RAG/router.py à partir du JWT quand
    # subject_id/course_id sont absents — jamais fourni directement par
    # l'appelant (un utilisateur ne doit pas pouvoir usurper owner_user_id).
    owner_user_id: Optional[int] = None


class IngestResult(BaseModel):
    doc_id: str
    chunks_indexed: int
    source_type: SourceType
    warnings: List[str] = Field(default_factory=list)


class IngestJobStatus(str, Enum):
    QUEUED = "queued"
    PROCESSING = "processing"
    DONE = "done"
    FAILED = "failed"


class IngestQueuedResponse(BaseModel):
    """Réponse immédiate de POST /rag/ingest — le traitement réel (OCR,
    transcription, embedding, indexation) tourne en tâche d'arrière-plan
    après l'envoi de cette réponse, pour ne pas faire attendre l'appelant
    (upload d'une vidéo d'une heure, PDF scanné volumineux, etc.)."""

    doc_id: str
    status: IngestJobStatus = IngestJobStatus.QUEUED


class IngestJobRecord(BaseModel):
    doc_id: str
    status: IngestJobStatus
    result: Optional[IngestResult] = None
    error: Optional[str] = None


class Citation(BaseModel):
    doc_id: str
    title: str
    page: Optional[int] = None
    section: Optional[str] = None
    chunk_id: str
    score: float
    # Utilisé par l'intégration chatbot WinAI pour décider si la source
    # peut être citée nommément (l'utilisateur a accès à cette formation)
    # ou seulement utilisée en arrière-plan (voir services/rag_chat_bridge.py).
    subject_id: Optional[int] = None
    course_id: Optional[int] = None


class RAGQueryRequest(BaseModel):
    question: str
    filters: Dict[str, Any] = Field(
        default_factory=dict,
        description=(
            "Filtre de métadonnées appliqué à la recherche (subject_id, "
            "course_id, status='active', etc.). Le contrôle d'accès réel "
            "(qui a le droit de voir quoi) reste de la responsabilité de "
            "l'appelant — ce module reste agnostique du modèle de "
            "permissions WinPlus, il applique juste le filtre reçu."
        ),
    )
    top_k: int = 5


class RetrievedContext(BaseModel):
    """Résultat de récupération + rerank SANS génération ni validation —
    utilisé par l'intégration chatbot (RAG/query_service.py), qui délègue
    la génération finale au prompt WinAI existant plutôt qu'au prompt
    générique de run_query(). `passages[i]` correspond à `citations[i]`."""

    passages: List[str] = Field(default_factory=list)
    citations: List[Citation] = Field(default_factory=list)
    refused: bool = False
    complexity: Optional[str] = None
    latency_ms: int = 0


class RAGAnswer(BaseModel):
    answer: str
    citations: List[Citation] = Field(default_factory=list)
    refused: bool = False
    faithfulness: Optional[float] = None
    answer_relevancy: Optional[float] = None
    context_precision: Optional[float] = None
    context_recall: Optional[float] = None
    backend: str = ""
    complexity: Optional[str] = None  # "simple" | "complex"
    latency_ms: int = 0
