"""Routes IA du professeur, dernière partie (routes/teacher_extra_routes.py) : téléchargement et lecture d'un fichier
déposé (PDF natif, PDF scanné, image, vidéo), analyse à l'envoi d'un contenu, évaluation d'une formation. Aucun accès
réseau : requests, OCR, vision et transcription sont remplacés par de faux modules."""

import os
import sys
from types import ModuleType, SimpleNamespace

import pytest
from fastapi import FastAPI, HTTPException
from fastapi.testclient import TestClient

import routes.teacher_extra_routes as tx


def fake_module(monkeypatch, name, **attrs):
    module = ModuleType(name)
    for key, value in attrs.items():
        setattr(module, key, value)
    monkeypatch.setitem(sys.modules, name, module)
    return module


@pytest.fixture
def client():
    app = FastAPI()
    app.include_router(tx.teacher_ai_router, prefix="/api")
    return TestClient(app, raise_server_exceptions=False)


def fake_json(monkeypatch, value):
    calls = []

    def fake(prompt, system, max_tokens=600):
        calls.append({"prompt": prompt, "system": system, "max_tokens": max_tokens})
        return value

    monkeypatch.setattr(tx, "_deepseek_json", fake)
    return calls


def teacher(headers_for, uid=3):
    return headers_for(uid, "teacher")


# ── téléchargement ──────────────────────────────────────────────────────────

class FakeResponse:
    def __init__(self, chunks, headers=None, error=None):
        self.chunks, self.headers, self.error = chunks, headers or {}, error

    def raise_for_status(self):
        if self.error:
            raise self.error

    def iter_content(self, chunk_size):
        yield from self.chunks


def test_download_returns_the_bytes_and_the_content_type(monkeypatch):
    seen = {}

    def get(url, timeout, stream):
        seen.update(url=url, timeout=timeout, stream=stream)
        return FakeResponse([b"ab", b"cd"], {"content-type": "application/pdf"})

    monkeypatch.setattr(tx._requests, "get", get)
    assert tx._download_for_analysis("https://x.test/a.pdf") == (b"abcd", "application/pdf")
    assert seen == {"url": "https://x.test/a.pdf", "timeout": 20, "stream": True}


def test_download_without_content_type(monkeypatch):
    monkeypatch.setattr(tx._requests, "get", lambda *a, **k: FakeResponse([b"x"]))
    assert tx._download_for_analysis("https://x.test/a")[1] is None


@pytest.mark.parametrize("error", [ConnectionError("injoignable"), TimeoutError("délai dépassé"), RuntimeError("404")])
def test_download_failure_is_a_502_with_a_french_message(monkeypatch, error):
    monkeypatch.setattr(tx._requests, "get", lambda *a, **k: FakeResponse([], error=error)
                        if isinstance(error, RuntimeError) else (_ for _ in ()).throw(error))
    with pytest.raises(HTTPException) as err:
        tx._download_for_analysis("https://x.test/a.pdf")
    assert err.value.status_code == 502 and "injoignable" in err.value.detail


def test_download_over_thirty_megabytes_is_refused_as_soon_as_the_limit_is_crossed(monkeypatch):
    big = b"x" * (tx._ANALYSIS_MAX_BYTES // 2 + 1)
    consumed = []

    def chunks():
        for _ in range(5):
            consumed.append(1)
            yield big

    monkeypatch.setattr(tx._requests, "get", lambda *a, **k: SimpleNamespace(
        raise_for_status=lambda: None, iter_content=lambda chunk_size: chunks(), headers={}))
    with pytest.raises(HTTPException) as err:
        tx._download_for_analysis("https://x.test/gros.mp4")
    assert err.value.status_code == 413 and "30 Mo" in err.value.detail and len(consumed) == 2


# ── OCR d'un PDF ────────────────────────────────────────────────────────────

def test_ocr_chain_prefers_mistral(monkeypatch):
    fake_module(monkeypatch, "RAG.api.ingestion.ocr_client", ocr_pdf=lambda path: f"texte de {path}")
    assert tx._ocr_pdf_chain("/tmp/a.pdf") == ("texte de /tmp/a.pdf", "mistral")


def test_ocr_chain_falls_back_on_the_local_engine_page_by_page(monkeypatch):
    def broken(path):
        raise RuntimeError("Mistral indisponible")

    fake_module(monkeypatch, "RAG.api.ingestion.ocr_client", ocr_pdf=broken)
    pages = {1: "A", 2: "   ", 3: "C"}
    fake_module(monkeypatch, "RAG.self_hosted.ingestion.ocr_engine", ocr_transcribe=lambda image: image)
    fake_module(monkeypatch, "RAG.shared.pdf_utils", page_count=lambda path: 3,
                render_page_image=lambda path, n, dpi: pages[n])
    text, engine = tx._ocr_pdf_chain("/tmp/a.pdf")
    assert engine == "local" and text == "--- page 1 ---\nA\n\n--- page 3 ---\nC"


# ── lecture d'un PDF ────────────────────────────────────────────────────────

LONG_TEXT = "mot " * 100          # 400 caractères, au-dessus du seuil de couche texte native


@pytest.fixture
def pdf(monkeypatch):
    state = SimpleNamespace(pages=[LONG_TEXT], total=3, native_error=None, ocr=("texte scanné", "mistral"), ocr_error=None,
                            sample_error=None, sampled=[], ocr_paths=[])

    def page_count(path):
        if isinstance(state.total, Exception):
            raise state.total
        return state.total

    def extract_native_text(path):
        state.sampled.append(path)
        if state.native_error:
            raise state.native_error
        return SimpleNamespace(pages=[SimpleNamespace(text=t) for t in state.pages])

    fake_module(monkeypatch, "RAG.shared.pdf_utils", page_count=page_count, extract_native_text=extract_native_text)

    def ocr(path):
        state.ocr_paths.append(path)
        if state.ocr_error:
            raise state.ocr_error
        return state.ocr

    monkeypatch.setattr(tx, "_ocr_pdf_chain", ocr)

    def sample(path, total):
        if state.sample_error:
            raise state.sample_error
        return "/tmp/echantillon-inexistant.pdf"

    monkeypatch.setattr(tx, "_write_first_last_pdf", sample)
    return state


def test_unopenable_pdf_is_reported_not_raised(pdf):
    pdf.total = RuntimeError("PDF protégé")
    assert tx._extract_pdf_for_analysis("/tmp/a.pdf") == (None, None, "Document illisible ou protégé : impossible à analyser automatiquement.")


def test_native_text_is_used_when_long_enough_and_cut_at_twelve_thousand_characters(pdf):
    pdf.pages = ["a" * 9000, "", "   ", "b" * 9000]
    text, duration, warning = tx._extract_pdf_for_analysis("/tmp/a.pdf")
    assert len(text) == 12000 and duration is None and warning is None and pdf.ocr_paths == []


def test_large_pdf_reads_only_the_first_and_last_pages_and_says_so(pdf):
    pdf.total = 31
    pdf.pages = ["x" * 9000, "y" * 9000]
    text, _, warning = tx._extract_pdf_for_analysis("/tmp/a.pdf")
    assert pdf.sampled == ["/tmp/echantillon-inexistant.pdf"] and len(text) == 12000
    assert text.count("x") == 6000 and text.count("y") == 6000                          # 6000 caractères par page au plus
    assert "31 pages" in warning and "première et dernière page seulement" in warning


def test_pdf_of_thirty_pages_is_still_read_in_full(pdf):
    pdf.total = 30
    _, _, warning = tx._extract_pdf_for_analysis("/tmp/a.pdf")
    assert pdf.sampled == ["/tmp/a.pdf"] and warning is None


def test_large_pdf_whose_sample_cannot_be_written_is_unreadable(pdf):
    pdf.total, pdf.sample_error = 40, RuntimeError("disque plein")
    assert tx._extract_pdf_for_analysis("/tmp/a.pdf") == (None, None, "Document illisible ou protégé : impossible à analyser automatiquement.")


def test_scanned_pdf_goes_through_ocr_with_a_warning(pdf):
    pdf.pages = [""]
    text, _, warning = tx._extract_pdf_for_analysis("/tmp/a.pdf")
    assert text == "texte scanné" and warning == "Document scanné : lecture par reconnaissance de caractères (OCR), relis les suggestions."


def test_scanned_pdf_read_by_the_local_engine_mentions_the_fallback(pdf):
    pdf.pages, pdf.ocr = [""], ("texte local", "local")
    _, _, warning = tx._extract_pdf_for_analysis("/tmp/a.pdf")
    assert "locale de secours (Mistral indisponible)" in warning


def test_large_scanned_pdf_mentions_the_sampling(pdf):
    pdf.total, pdf.pages = 50, [""]
    _, _, warning = tx._extract_pdf_for_analysis("/tmp/a.pdf")
    assert "première et dernière page seulement" in warning and warning.endswith("relis les suggestions.")


def test_native_extraction_error_falls_through_to_ocr(pdf):
    pdf.native_error = RuntimeError("fitz")
    assert tx._extract_pdf_for_analysis("/tmp/a.pdf")[0] == "texte scanné"


def test_ocr_failure_keeps_a_little_native_text_with_a_warning(pdf):
    pdf.pages, pdf.ocr_error = ["court texte"], RuntimeError("OCR HS")
    text, _, warning = tx._extract_pdf_for_analysis("/tmp/a.pdf")
    assert text == "court texte" and "OCR" in warning and "relis les suggestions" in warning


def test_ocr_failure_without_any_text_asks_for_manual_entry(pdf):
    pdf.pages, pdf.ocr_error = [""], RuntimeError("OCR HS")
    text, _, warning = tx._extract_pdf_for_analysis("/tmp/a.pdf")
    assert text is None and "remplis le formulaire manuellement" in warning


def test_empty_ocr_keeps_the_native_text_silently_or_reports_an_empty_document(pdf):
    pdf.pages, pdf.ocr = ["court"], ("   ", "mistral")
    assert tx._extract_pdf_for_analysis("/tmp/a.pdf") == ("court", None, None)
    pdf.pages = [""]
    assert tx._extract_pdf_for_analysis("/tmp/a.pdf") == (None, None, "Document illisible (vide après extraction native et OCR).")


# ── lecture selon le type de fichier ────────────────────────────────────────

def test_pdf_is_the_default_type_and_the_temporary_file_is_removed(monkeypatch):
    seen = []
    monkeypatch.setattr(tx, "_extract_pdf_for_analysis", lambda path: seen.append((path, open(path, "rb").read())) or ("ok", None, None))
    assert tx._extract_text_for_analysis(b"%PDF", None) == ("ok", None, None)
    assert tx._extract_text_for_analysis(b"%PDF", "Cours.PDF") == ("ok", None, None)
    assert [s[1] for s in seen] == [b"%PDF", b"%PDF"] and all(not os.path.exists(s[0]) for s in seen)


def test_unsupported_extension_is_reported(monkeypatch):
    assert tx._extract_text_for_analysis(b"x", "archive.zip") == (None, None, "Type de fichier non couvert par l'analyse automatique (.zip).")


def test_video_is_transcribed_with_its_duration(monkeypatch):
    segments = [SimpleNamespace(text="Bonjour", end=3.2), SimpleNamespace(text="à tous", end=61.9)]
    fake_module(monkeypatch, "RAG.api.ingestion.transcription_client", transcribe_video=lambda path: segments)
    assert tx._extract_text_for_analysis(b"x", "cours.mp4") == ("Bonjour à tous", 61, None)
    assert tx._extract_text_for_analysis(b"x", "audio.mp3")[0] == "Bonjour à tous"


def test_silent_video_and_failed_transcription(monkeypatch):
    fake_module(monkeypatch, "RAG.api.ingestion.transcription_client", transcribe_video=lambda path: [])
    text, duration, warning = tx._extract_text_for_analysis(b"x", "muet.mp4")
    assert text is None and duration is None and "Aucune piste audio" in warning

    def boom(path):
        raise RuntimeError("Whisper HS")

    fake_module(monkeypatch, "RAG.api.ingestion.transcription_client", transcribe_video=boom)
    assert tx._extract_text_for_analysis(b"x", "cours.mkv") == (None, None, "Transcription de la vidéo impossible pour l'instant.")


def test_image_description_is_returned(monkeypatch):
    fake_module(monkeypatch, "RAG.api.ingestion.vision_client", caption_image=lambda raw: "Un schéma de circuit")
    fake_module(monkeypatch, "RAG.api.ingestion.ocr_client", ocr_image=lambda raw: "R = U / I")
    text, _, warning = tx._extract_text_for_analysis(b"x", "schema.png")
    assert "Un schéma de circuit" in text and warning is None


@pytest.mark.xfail(strict=False, reason="Dans la branche image, ocr_image est appelé avant d'être importé (l'import n'existe "
                   "que dans le bloc except) : NameError avalé par `except Exception: pass`, le texte de l'image n'est donc "
                   "jamais joint à la description. Le texte lu par OCR devrait apparaître avec la légende.")
def test_image_description_includes_the_text_read_by_ocr(monkeypatch):
    fake_module(monkeypatch, "RAG.api.ingestion.vision_client", caption_image=lambda raw: "Un schéma de circuit")
    fake_module(monkeypatch, "RAG.api.ingestion.ocr_client", ocr_image=lambda raw: "R = U / I")
    assert "R = U / I" in tx._extract_text_for_analysis(b"x", "schema.png")[0]


def test_image_falls_back_on_ocr_when_vision_fails(monkeypatch):
    def broken(raw):
        raise RuntimeError("Gemini HS")

    fake_module(monkeypatch, "RAG.api.ingestion.vision_client", caption_image=broken)
    fake_module(monkeypatch, "RAG.api.ingestion.ocr_client", ocr_image=lambda raw: " Texte lu ")
    text, _, warning = tx._extract_text_for_analysis(b"x", "photo.jpg")
    assert text == "Texte lu" and "Description visuelle indisponible" in warning


def test_image_unreadable_by_both_engines(monkeypatch):
    def broken(raw):
        raise RuntimeError("HS")

    fake_module(monkeypatch, "RAG.api.ingestion.vision_client", caption_image=broken)
    fake_module(monkeypatch, "RAG.api.ingestion.ocr_client", ocr_image=broken)
    assert tx._extract_text_for_analysis(b"x", "photo.webp") == (None, None, "Image illisible par l'IA de vision.")
    fake_module(monkeypatch, "RAG.api.ingestion.ocr_client", ocr_image=lambda raw: "  ")
    assert tx._extract_text_for_analysis(b"x", "photo.webp")[2] == "Image illisible par l'IA de vision."


# ── POST /teacher/analyze-content-upload ────────────────────────────────────

URL_UPLOAD = "/api/teacher/analyze-content-upload"
FULL = {"title": " Épreuve BAC 2023 ", "description": "Desc", "description_courte": "Courte", "category": "Maths",
        "level": "Tle", "difficulty": "hard", "year": 2023, "exam_type": "BAC", "tags": ["a", "", " ", "b"],
        "objectives": ["o"], "prerequisites": "pas une liste", "author": "", "publisher": "Hachette", "page_count": 120,
        "price_suggestion": "1500", "winai_score": 140, "winai_justification": " Solide. "}


@pytest.fixture
def upload(monkeypatch):
    state = SimpleNamespace(text="Contenu lu", duration=None, warning=None, download_error=None)

    def download(url):
        if state.download_error:
            raise state.download_error
        return b"bytes", "application/pdf"

    monkeypatch.setattr(tx, "_download_for_analysis", download)
    monkeypatch.setattr(tx, "_extract_text_for_analysis", lambda raw, name: (state.text, state.duration, state.warning))
    return state


def _upload(client, headers_for, **body):
    return client.post(URL_UPLOAD, json={"file_url": "https://x.test/a.pdf", **body}, headers=teacher(headers_for))


def test_upload_analysis_needs_the_teacher_role_and_a_file_url(client, headers_for, upload, monkeypatch):
    fake_json(monkeypatch, FULL)
    assert client.post(URL_UPLOAD, json={"file_url": "x"}).status_code in (401, 403)
    assert client.post(URL_UPLOAD, json={"file_url": "x"}, headers=headers_for(1, "student")).status_code == 403
    assert client.post(URL_UPLOAD, json={}, headers=teacher(headers_for)).status_code == 422


def test_upload_analysis_cleans_the_model_suggestions(client, headers_for, upload, monkeypatch):
    calls = fake_json(monkeypatch, dict(FULL))
    body = _upload(client, headers_for, content_kind="livre").json()
    s = body["suggested"]
    assert s["title"] == "Épreuve BAC 2023" and s["year"] == "2023" and s["author"] is None
    assert s["tags"] == ["a", "b"] and s["prerequisites"] == [] and s["page_count"] == 120 and s["price_suggestion"] == 1500.0
    assert body["winai_score"] == 100.0 and body["winai_justification"] == "Solide." and body["extraction_warning"] is None
    assert "auteur" in calls[0]["prompt"] and "Contenu lu" in calls[0]["prompt"] and calls[0]["max_tokens"] == 2200


@pytest.mark.parametrize("raw,score", [(-5, 0.0), ("85.5", 85.5), ("beaucoup", None), (None, None), (0, 0.0)])
def test_upload_score_is_bounded_to_zero_one_hundred_or_dropped(client, headers_for, upload, monkeypatch, raw, score):
    fake_json(monkeypatch, {"winai_score": raw})
    assert _upload(client, headers_for).json()["winai_score"] == score


@pytest.mark.parametrize("raw,expected", [(0, None), (-3, None), ("12", None), (12.7, 12), (True, 1)])
def test_upload_page_count_must_be_a_positive_number(client, headers_for, upload, monkeypatch, raw, expected):
    fake_json(monkeypatch, {"page_count": raw})
    assert _upload(client, headers_for).json()["suggested"]["page_count"] == expected


def test_upload_price_that_is_not_a_number_is_dropped(client, headers_for, upload, monkeypatch):
    fake_json(monkeypatch, {"price_suggestion": "gratuit"})
    assert _upload(client, headers_for).json()["suggested"]["price_suggestion"] is None


def test_upload_closed_lists_are_put_in_the_prompt(client, headers_for, upload, monkeypatch):
    calls = fake_json(monkeypatch, {})
    _upload(client, headers_for, allowed_categories=["Maths", "SVT"], allowed_levels=["3e", "Tle"], content_kind="inconnu")
    prompt = calls[0]["prompt"]
    assert "liste fermée" in prompt and "Maths, SVT" in prompt and "3e, Tle" in prompt
    assert "année/session" in prompt                                   # type inconnu : repère de l'épreuve
    calls.clear()
    _upload(client, headers_for)
    assert "liste fermée" not in calls[0]["prompt"]


def test_upload_unreadable_file_never_blocks_the_form_and_never_calls_the_model(client, headers_for, upload, monkeypatch):
    calls = fake_json(monkeypatch, FULL)
    upload.text, upload.duration, upload.warning = None, 42, "Aucune piste audio exploitable"
    body = _upload(client, headers_for).json()
    assert body["suggested"]["duration_seconds"] == 42 and body["suggested"]["title"] is None
    assert body["extraction_warning"] == "Aucune piste audio exploitable" and body["winai_score"] is None and calls == []
    upload.warning = None
    assert "remplis le formulaire manuellement" in _upload(client, headers_for).json()["extraction_warning"]


def test_upload_model_failure_keeps_the_extraction_warning_and_duration(client, headers_for, upload, monkeypatch):
    fake_json(monkeypatch, None)
    upload.duration, upload.warning = 90, "Document scanné"
    body = _upload(client, headers_for).json()
    assert body["suggested"]["duration_seconds"] == 90 and body["extraction_warning"] == "Document scanné"
    upload.warning = None
    assert "n'a pas pu produire de suggestion" in _upload(client, headers_for).json()["extraction_warning"]


def test_upload_download_errors_are_returned_as_http_errors(client, headers_for, upload, monkeypatch):
    fake_json(monkeypatch, FULL)
    upload.download_error = HTTPException(status_code=413, detail="Fichier trop volumineux")
    r = _upload(client, headers_for)
    assert r.status_code == 413 and r.json()["detail"] == "Fichier trop volumineux"


# ── POST /teacher/evaluate-course ───────────────────────────────────────────

URL_EVAL = "/api/teacher/evaluate-course"
COURSE = {"title": "Algèbre", "short_description": "Résumé", "description": "Longue", "level": "Tle", "category": "Maths",
          "objectives": ["o1", "o2"], "requirements": ["r1"], "has_final_exam": True,
          "sections": [{"title": "Bases", "lessons": [{"title": "Vecteurs", "lesson_type": "video", "excerpt": "e" * 800},
                                                       {"title": "Matrices"}]}]}


def test_evaluation_needs_the_teacher_role_and_a_title(client, headers_for, monkeypatch):
    fake_json(monkeypatch, {"winai_score": 70})
    assert client.post(URL_EVAL, json=COURSE).status_code in (401, 403)
    assert client.post(URL_EVAL, json=COURSE, headers=headers_for(1, "student")).status_code == 403
    assert client.post(URL_EVAL, json={}, headers=teacher(headers_for)).status_code == 422


def test_evaluation_prompt_lists_the_real_structure(client, headers_for, monkeypatch):
    calls = fake_json(monkeypatch, {"winai_score": 72.5, "winai_justification": " Bien structuré. "})
    body = client.post(URL_EVAL, json=COURSE, headers=teacher(headers_for)).json()
    assert body == {"winai_score": 72.5, "winai_justification": "Bien structuré."}
    prompt = calls[0]["prompt"]
    for line in ("Titre : Algèbre", "Niveau : Tle", "Matière : Maths", "Objectifs : o1 ; o2", "Prérequis : r1",
                 "Examen final : oui", "Section 1 : Bases", "  - Vecteurs (video)", "  - Matrices"):
        assert line in prompt
    assert "e" * 600 in prompt and "e" * 601 not in prompt and calls[0]["max_tokens"] == 500


def test_evaluation_text_is_capped_at_twelve_thousand_characters(client, headers_for, monkeypatch):
    calls = fake_json(monkeypatch, {})
    sections = [{"title": f"S{i}", "lessons": [{"title": "L", "excerpt": "z" * 600}]} for i in range(40)]
    client.post(URL_EVAL, json={"title": "Gros", "sections": sections}, headers=teacher(headers_for))
    assert "Section 10" in calls[0]["prompt"] and "Section 40" not in calls[0]["prompt"]
    assert "Examen final : non" in calls[0]["prompt"]


@pytest.mark.parametrize("raw,score", [(150, 100.0), (-1, 0.0), ("55", 55.0), ("nul", None), (None, None)])
def test_evaluation_score_is_bounded_or_dropped(client, headers_for, monkeypatch, raw, score):
    fake_json(monkeypatch, {"winai_score": raw})
    assert client.post(URL_EVAL, json=COURSE, headers=teacher(headers_for)).json()["winai_score"] == score


@pytest.mark.parametrize("answer", [None, [], "texte"])
def test_evaluation_failure_is_advisory_never_an_error(client, headers_for, monkeypatch, answer):
    fake_json(monkeypatch, answer)
    r = client.post(URL_EVAL, json=COURSE, headers=teacher(headers_for))
    assert r.status_code == 200 and r.json() == {"winai_score": None, "winai_justification": None}


def test_evaluation_blank_justification_becomes_null(client, headers_for, monkeypatch):
    fake_json(monkeypatch, {"winai_score": 50, "winai_justification": "   "})
    assert client.post(URL_EVAL, json=COURSE, headers=teacher(headers_for)).json()["winai_justification"] is None
