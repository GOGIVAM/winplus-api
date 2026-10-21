"""
Résout `IngestRequest.file_path`/`inline_content_base64` vers un chemin
local réel, utilisable par `fitz.open()`, `whisper`, etc.

Bug réel trouvé en vérifiant le code (pas supposé) : tous les appelants
(.NET via QueueRagIngestion, le script de backfill) passent une URL S3
publique comme `file_path`. Or `fitz.open(pdf_path)`  utilisé partout
dans `RAG/*/ingestion/pipeline.py`  n'accepte qu'un chemin local ou un
flux d'octets, PAS une URL http(s) : ça aurait échoué dès le premier vrai
appel avec de vraies données. Ce module télécharge l'URL une fois vers un
fichier temporaire avant tout traitement.

Sert aussi à décoder un contenu inline en base64 (pièce jointe de chat,
qui ne passe jamais par S3) vers ce même chemin temporaire  l'appelant
(process_document) n'a donc jamais besoin de savoir d'où vient le fichier.
"""

from __future__ import annotations

import base64
import contextlib
import logging
import os
import tempfile
from typing import Iterator

import requests

from RAG.shared.contracts import IngestRequest

logger = logging.getLogger(__name__)

_DOWNLOAD_TIMEOUT_SECONDS = 60


@contextlib.contextmanager
def resolve_local_path(request: IngestRequest) -> Iterator[str]:
    """Context manager : renvoie un chemin de fichier local valide pour la
    durée du bloc `with`, en gérant les trois cas (URL distante, chemin
    local déjà valide, contenu inline base64)  nettoie le fichier
    temporaire créé, le cas échéant, à la sortie du bloc."""
    if request.inline_content_base64:
        yield from _from_inline_base64(request)
        return

    if not request.file_path:
        raise ValueError(f"IngestRequest {request.doc_id} : ni file_path ni inline_content_base64 fournis")

    if request.file_path.startswith(("http://", "https://")):
        yield from _from_url(request.file_path)
        return

    # Chemin déjà local (self_hosted en environnement de test, ou fichier
    # déjà présent sur la même machine que le worker d'ingestion).
    if not os.path.exists(request.file_path):
        raise FileNotFoundError(f"IngestRequest {request.doc_id} : fichier local introuvable : {request.file_path}")
    yield request.file_path


def _cleanup(tmp_path: str) -> None:
    """Best-effort : sur Windows, un fichier encore ouvert par un handle
    fitz (PyMuPDF ne le ferme pas toujours explicitement avant la fin du
    traitement) ne peut pas être supprimé (PermissionError)  trouvé en
    testant réellement le cycle complet, pas supposé. Un fichier temporaire
    qui traîne est un détail de nettoyage, jamais une raison de faire
    échouer une ingestion par ailleurs réussie."""
    try:
        if os.path.exists(tmp_path):
            os.remove(tmp_path)
    except OSError as e:
        logger.debug(f"[RAG] Nettoyage du fichier temporaire {tmp_path} reporté (probablement encore ouvert) : {e}")


def _from_url(url: str) -> Iterator[str]:
    ext = os.path.splitext(url.split("?")[0])[1] or ".bin"
    fd, tmp_path = tempfile.mkstemp(suffix=ext, prefix="rag_dl_")
    try:
        logger.info(f"[RAG] Téléchargement de {url} vers {tmp_path}")
        s3 = _s3_location(url)
        if s3:
            # Le bucket est privé : une URL S3 publique répond 403. On lit l'objet
            # avec les identifiants AWS du service (variables AWS_* ou rôle IAM).
            import boto3
            bucket, region, key = s3
            body = boto3.client("s3", region_name=region).get_object(Bucket=bucket, Key=key)["Body"]
            with os.fdopen(fd, "wb") as f:
                for chunk in iter(lambda: body.read(1024 * 1024), b""):
                    f.write(chunk)
        else:
            response = requests.get(url, timeout=_DOWNLOAD_TIMEOUT_SECONDS, stream=True)
            response.raise_for_status()
            with os.fdopen(fd, "wb") as f:
                for chunk in response.iter_content(chunk_size=1024 * 1024):
                    f.write(chunk)
        yield tmp_path
    finally:
        _cleanup(tmp_path)


def _from_inline_base64(request: IngestRequest) -> Iterator[str]:
    payload = request.inline_content_base64
    # Tolère une data URL complète (data:application/pdf;base64,....) en
    # plus du base64 brut, au cas où l'appelant n'aurait pas déjà découpé.
    if payload.startswith("data:") and "," in payload:
        payload = payload.split(",", 1)[1]

    ext = request.file_extension_hint or os.path.splitext(request.title or "")[1] or ".bin"
    if not ext.startswith("."):
        ext = f".{ext}"

    fd, tmp_path = tempfile.mkstemp(suffix=ext, prefix="rag_inline_")
    try:
        with os.fdopen(fd, "wb") as f:
            f.write(base64.b64decode(payload))
        yield tmp_path
    finally:
        _cleanup(tmp_path)


def _s3_location(url: str):
    """(bucket, région, clé) pour une URL S3 virtual-hosted ou path-style, sinon None."""
    from urllib.parse import unquote, urlparse
    import re
    parsed = urlparse(url)
    host, path = parsed.netloc, unquote(parsed.path.lstrip("/"))
    if not host.endswith(".amazonaws.com") or not path:
        return None
    m = re.match(r"^s3[.-]?([a-z0-9-]+)?\.amazonaws\.com$", host)
    if m:
        if "/" not in path:
            return None
        bucket, key = path.split("/", 1)
        return (bucket, m.group(1) or "us-east-1", key)
    m = re.match(r"^(.+?)\.s3[.-]([a-z0-9-]+)\.amazonaws\.com$", host)
    if m:
        return (m.group(1), m.group(2), path)
    m = re.match(r"^(.+?)\.s3\.amazonaws\.com$", host)
    if m:
        return (m.group(1), "us-east-1", path)
    return None
