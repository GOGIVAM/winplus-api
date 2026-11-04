"""Pièces jointes de chat (services/attachment_processor.py) : décodage, aperçu texte, identifiant
de document stable, ingestion de fond planifiée. L'ingestion RAG réelle est remplacée par un simulacre."""

import asyncio
import base64
import hashlib
import sys
import types

import pytest

from services import attachment_processor as ap


def _data_url(raw: bytes, mime="text/plain"):
    return f"data:{mime};base64," + base64.b64encode(raw).decode()


# ── analyse de la data URL ──────────────────────────────────────────────────

def test_parse_data_url_with_mime_and_base64():
    assert ap._parse_data_url("data:text/plain;base64,QUJD") == ("QUJD", "text/plain")


def test_parse_data_url_without_prefix_returns_raw_payload():
    assert ap._parse_data_url("QUJD") == ("QUJD", None)


def test_parse_data_url_without_mime():
    assert ap._parse_data_url("data:;base64,QUJD") == ("QUJD", None)


# ── extension ───────────────────────────────────────────────────────────────

@pytest.mark.parametrize("mime,filename,expected", [
    ("application/pdf", None, ".pdf"),
    ("text/csv", "x.bin", ".csv"),            # le type MIME connu prime sur le nom
    ("application/x-inconnu", "cours.docx", ".docx"),
    (None, "photo.PNG", ".PNG"),
    (None, None, ".bin"),
    (None, "sans_extension", ".bin"),
])
def test_guess_extension(mime, filename, expected):
    assert ap._guess_extension(mime, filename) == expected


# ── aperçu rapide ───────────────────────────────────────────────────────────

def test_quick_preview_of_text_files_decodes_leniently():
    assert ap._extract_quick_preview("é = 1".encode("utf-8"), ".txt") == "é = 1"
    assert ap._extract_quick_preview(b"\xff\xfeok", ".md") == "��ok"


def test_quick_preview_is_truncated():
    assert len(ap._extract_quick_preview(b"a" * 9000, ".csv")) == ap._MAX_PREVIEW_CHARS == 8000


def test_quick_preview_unsupported_extension_is_none():
    assert ap._extract_quick_preview(b"PK\x03\x04", ".docx") is None


def test_quick_preview_of_unreadable_pdf_is_none_not_an_error():
    # PyMuPDF absent ou fichier invalide : dans les deux cas l'aperçu est simplement indisponible
    assert ap._extract_quick_preview(b"ceci n'est pas un pdf", ".pdf") is None


# ── _prepare ────────────────────────────────────────────────────────────────

def test_prepare_text_attachment_builds_prompt_text_and_ingest_request():
    raw = "Bonjour la classe".encode("utf-8")
    text, request = ap._prepare(_data_url(raw), "notes.txt", user_id=42)
    assert text == "[Contenu du fichier joint « notes.txt »]\nBonjour la classe"
    assert request.doc_id == "chatattach_42_" + hashlib.sha256(raw).hexdigest()[:16]
    assert request.title == "notes.txt"
    assert request.owner_user_id == 42
    assert request.file_extension_hint == ".txt"
    assert base64.b64decode(request.inline_content_base64) == raw


def test_prepare_same_content_gives_same_doc_id_other_user_differs():
    url = _data_url(b"contenu")
    first = ap._prepare(url, "a.txt", 1)[1].doc_id
    assert ap._prepare(url, "b.txt", 1)[1].doc_id == first
    assert ap._prepare(url, "a.txt", 2)[1].doc_id != first


def test_prepare_without_filename_uses_default_title():
    text, request = ap._prepare(_data_url(b"abc"), None, 1)
    assert "« document »" in text and request.title == "document"


def test_prepare_binary_without_preview_asks_model_to_wait():
    text, request = ap._prepare(_data_url(b"PK\x03\x04", "application/zip"), "cours.docx", 7)
    assert "« cours.docx » reçue" in text and "traitement en cours" in text
    assert request is not None and request.file_extension_hint == ".docx"


def test_prepare_empty_text_file_has_no_preview():
    text, request = ap._prepare(_data_url(b""), "vide.txt", 7)
    assert "traitement en cours" in text and request is not None


def test_prepare_invalid_base64_is_reported_unreadable():
    text, request = ap._prepare("data:text/plain;base64,abc", "casse.txt", 7)  # remplissage invalide
    assert "casse.txt" in text and "contenu illisible" in text
    assert request is None


# ── process_chat_attachment_async ───────────────────────────────────────────

@pytest.fixture
def ingestion(monkeypatch):
    """Remplace RAG.router (lourd) par un module portant un faux _run_ingestion_job."""
    jobs = []
    fake = types.ModuleType("RAG.router")
    fake._run_ingestion_job = lambda request: jobs.append(request)
    monkeypatch.setitem(sys.modules, "RAG.router", fake)
    return jobs, fake


async def _run(url, filename, user_id):
    text = await ap.process_chat_attachment_async(url, filename, user_id)
    others = [t for t in asyncio.all_tasks() if t is not asyncio.current_task()]
    if others:
        await asyncio.gather(*others)
    return text


def test_async_entry_returns_text_and_schedules_background_ingestion(ingestion):
    jobs, _ = ingestion
    text = asyncio.run(_run(_data_url(b"Salut"), "mot.txt", 5))
    assert text.endswith("Salut")
    assert len(jobs) == 1 and jobs[0].owner_user_id == 5 and jobs[0].title == "mot.txt"


def test_async_entry_swallows_ingestion_failures(ingestion):
    jobs, fake = ingestion

    def boom(request):
        raise RuntimeError("Qdrant indisponible")

    fake._run_ingestion_job = boom
    text = asyncio.run(_run(_data_url(b"Salut"), "mot.txt", 5))
    # la réponse au message n'est jamais retardée ni cassée par l'échec de l'ingestion
    assert text.endswith("Salut") and jobs == []


def test_async_entry_unreadable_attachment_schedules_nothing(ingestion):
    jobs, _ = ingestion
    text = asyncio.run(_run("data:text/plain;base64,abc", "casse.txt", 5))
    assert "contenu illisible" in text and jobs == []
