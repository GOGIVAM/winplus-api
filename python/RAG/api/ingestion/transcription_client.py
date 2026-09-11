"""
Transcription des vidéos de formation via Groq Whisper (large-v3-turbo) —
le plus économique et le plus rapide des fournisseurs vérifiés (voir
RAG/README.md). Extraction audio préalable par ffmpeg.
"""

from __future__ import annotations

import logging
import subprocess
import tempfile
from dataclasses import dataclass
from typing import List

from groq import Groq

from RAG.api import config

logger = logging.getLogger(__name__)

_client: Groq | None = None


def _get_client() -> Groq:
    global _client
    if _client is None:
        _client = Groq(api_key=config.GROQ_API_KEY)
    return _client


@dataclass
class TranscriptSegment:
    text: str
    start: float
    end: float


def extract_audio(video_path: str) -> str:
    """Extrait la piste audio en WAV mono 16kHz (format attendu par Whisper)."""
    audio_path = tempfile.mktemp(suffix=".wav")
    subprocess.run(
        ["ffmpeg", "-y", "-i", video_path, "-ar", "16000", "-ac", "1", audio_path],
        check=True,
        capture_output=True,
    )
    return audio_path


def transcribe_video(video_path: str) -> List[TranscriptSegment]:
    audio_path = extract_audio(video_path)
    client = _get_client()

    with open(audio_path, "rb") as f:
        response = client.audio.transcriptions.create(
            file=f,
            model=config.GROQ_WHISPER_MODEL,
            response_format="verbose_json",
            # Sans timestamp_granularities, l'API ne garantit pas de renvoyer
            # les segments horodatés (vérifié sur la doc officielle Groq) —
            # or c'est précisément ce dont on a besoin pour citer "à 12:34".
            timestamp_granularities=["segment"],
            language="fr",
        )

    segments = getattr(response, "segments", None) or []
    return [
        TranscriptSegment(text=text.strip(), start=_field(seg, "start"), end=_field(seg, "end"))
        for seg in segments
        for text in [_field(seg, "text") or ""]
        if text.strip()
    ]


def _field(obj, name: str):
    """Le SDK Groq peut renvoyer les segments verbose_json comme dicts bruts
    ou comme attributs d'un modèle Pydantic selon la version — non vérifiable
    sans appel réel à l'API, donc on gère les deux plutôt que de parier."""
    if isinstance(obj, dict):
        return obj.get(name)
    return getattr(obj, name, None)
