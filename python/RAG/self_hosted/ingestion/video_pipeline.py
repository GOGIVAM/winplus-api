"""
Transcription des vidéos de formation en local via Whisper (implémentation
PyTorch native `openai-whisper`, chargée une fois via models/loader.py) —
équivalent self_hosted du pipeline vidéo de RAG/api (qui, lui, utilise
l'API Groq). Répond à la demande initiale : ingérer le contenu des vidéos
de cours dans la base de connaissance, sans dépendance à un service tiers.
"""

from __future__ import annotations

import logging
import subprocess
import tempfile
from dataclasses import dataclass
from typing import List

from RAG.self_hosted.models.loader import get_whisper_model

logger = logging.getLogger(__name__)


@dataclass
class TranscriptSegment:
    text: str
    start: float
    end: float


def extract_audio(video_path: str) -> str:
    """Extrait la piste audio en WAV mono 16kHz (format attendu par Whisper).
    Nécessite ffmpeg installé sur la machine (voir DEPLOYMENT.md §2.5)."""
    audio_path = tempfile.mktemp(suffix=".wav")
    subprocess.run(
        ["ffmpeg", "-y", "-i", video_path, "-ar", "16000", "-ac", "1", audio_path],
        check=True,
        capture_output=True,
    )
    return audio_path


def transcribe_video(video_path: str) -> List[TranscriptSegment]:
    audio_path = extract_audio(video_path)
    model = get_whisper_model()

    result = model.transcribe(audio_path, language="fr", verbose=False)

    return [
        TranscriptSegment(text=seg["text"].strip(), start=seg["start"], end=seg["end"])
        for seg in result.get("segments", [])
        if seg.get("text", "").strip()
    ]
