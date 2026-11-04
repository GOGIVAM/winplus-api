"""Mode évaluation (routes/exam_quiz_routes.py) : lecture des URL S3, extraction du PDF, analyse de la réponse
du modèle, route POST /generate. S3, pypdf et le fournisseur d'IA sont simulés ; base SQLite en mémoire."""

import io
import json
from types import SimpleNamespace

import pytest
from botocore.exceptions import ClientError
from fastapi import FastAPI
from fastapi.testclient import TestClient

import database as dbm
import routes.exam_quiz_routes as eq


# ── _parse_s3_url ───────────────────────────────────────────────────────────

@pytest.mark.parametrize("url,expected", [
    ("https://mybucket.s3.eu-west-1.amazonaws.com/dir/f.pdf", ("mybucket", "eu-west-1", "dir/f.pdf")),
    ("https://mybucket.s3-eu-west-1.amazonaws.com/f.pdf", ("mybucket", "eu-west-1", "f.pdf")),
    ("https://mybucket.s3.amazonaws.com/f.pdf", ("mybucket", "us-east-1", "f.pdf")),
    ("https://s3.eu-west-3.amazonaws.com/mybucket/dir/f.pdf", ("mybucket", "eu-west-3", "dir/f.pdf")),
    ("https://s3.amazonaws.com/mybucket/f.pdf", ("mybucket", "us-east-1", "f.pdf")),
    ("https://mybucket.s3.eu-west-1.amazonaws.com/a%20b.pdf", ("mybucket", "eu-west-1", "a b.pdf")),
])
def test_parse_s3_url_supported_styles(url, expected):
    assert eq._parse_s3_url(url) == expected


@pytest.mark.parametrize("url", [
    "https://example.com/f.pdf",                                # pas AWS
    "https://mybucket.s3.eu-west-1.amazonaws.com/",             # pas de clé
    "https://s3.amazonaws.com/onlybucket",                      # style chemin sans clé
    "https://autre.amazonaws.com/f.pdf",                        # service AWS qui n'est pas S3
    "",
])
def test_parse_s3_url_rejects_everything_else(url):
    assert eq._parse_s3_url(url) is None


# ── téléchargement ──────────────────────────────────────────────────────────

def test_download_uses_bucket_key_and_region_from_the_url(monkeypatch):
    captured = {}

    class FakeS3:
        def get_object(self, Bucket, Key):
            captured["object"] = (Bucket, Key)
            return {"Body": io.BytesIO(b"%PDF-fake")}

    monkeypatch.setattr(eq.boto3, "client", lambda service, region_name=None: captured.update(
        service=service, region=region_name) or FakeS3())
    assert eq._download_pdf_bytes("https://mybucket.s3.eu-west-1.amazonaws.com/dir/f.pdf") == b"%PDF-fake"
    assert captured == {"service": "s3", "region": "eu-west-1", "object": ("mybucket", "dir/f.pdf")}


def test_download_rejects_unknown_url_before_any_network_call(monkeypatch):
    monkeypatch.setattr(eq.boto3, "client", lambda *a, **k: pytest.fail("aucun appel S3 attendu"))
    with pytest.raises(ValueError, match="format S3 attendu"):
        eq._download_pdf_bytes("https://example.com/f.pdf")


# ── extraction du texte ─────────────────────────────────────────────────────

class FakePage:
    def __init__(self, text):
        self.text = text

    def extract_text(self):
        return self.text


def _reader(pages):
    return lambda stream: SimpleNamespace(pages=[FakePage(t) for t in pages])


def test_extract_pdf_text_joins_pages_and_skips_empty_ones(monkeypatch):
    monkeypatch.setattr(eq, "PdfReader", _reader(["Page un ", None, "   ", "Page deux"]))
    assert eq._extract_pdf_text(b"x") == "Page un\n\nPage deux"


def test_extract_pdf_text_stops_at_the_character_budget_and_truncates(monkeypatch):
    big = "a" * 15000
    monkeypatch.setattr(eq, "PdfReader", _reader([big, big, big]))
    text = eq._extract_pdf_text(b"x")
    assert len(text) == eq.MAX_EXTRACTED_CHARS == 24000
    assert text.count("\n\n") == 1            # la troisième page n'est jamais lue


def test_extract_pdf_text_reads_at_most_forty_pages(monkeypatch):
    monkeypatch.setattr(eq, "PdfReader", _reader([f"p{i}" for i in range(50)]))
    text = eq._extract_pdf_text(b"x")
    assert "p39" in text and "p40" not in text


# ── génération de questions ─────────────────────────────────────────────────

class FakeDeepSeek:
    def __init__(self, content):
        self.content, self.calls = content, []

    def chat(self, messages, system_prompt=None, max_tokens=None, temperature=None, json_mode=False):
        self.calls.append({"messages": messages, "system": system_prompt, "max_tokens": max_tokens,
                           "temperature": temperature})
        return {"content": self.content}


def _question(id="q1", options=("A) 1", "B) 2", "C) 3", "D) 4"), correct="B) 2", **kw):
    q = {"id": id, "question": "Combien ?", "options": list(options), "correctAnswer": correct,
         "explanation": "Parce que."}
    q.update(kw)
    return q


@pytest.fixture
def fake_model(monkeypatch):
    holder = {}

    def install(content):
        holder["client"] = FakeDeepSeek(content)
        monkeypatch.setattr(eq, "get_deepseek_client", lambda: holder["client"])
        return holder["client"]

    return install


def test_questions_are_parsed_from_the_model_output(fake_model):
    client = fake_model("Voici :\n" + json.dumps([_question(statement="  Énoncé commun  ")]) + "\nFin.")
    questions = eq._generate_questions_from_text("contenu", "BEPC 2023", "Maths")
    assert len(questions) == 1
    q = questions[0]
    assert (q.id, q.question, q.correctAnswer, q.explanation) == ("q1", "Combien ?", "B) 2", "Parce que.")
    assert q.statement == "Énoncé commun" and len(q.options) == 4
    call = client.calls[0]
    assert "BEPC 2023" in call["messages"][0]["content"] and "(matière : Maths)" in call["messages"][0]["content"]
    assert call["max_tokens"] == 6000 and call["temperature"] == 0.3


def test_questions_whose_answer_is_not_among_the_options_are_dropped(fake_model):
    fake_model(json.dumps([_question(correct="E) 5"), _question("q2"), _question("q3", options=[])]))
    assert [q.id for q in eq._generate_questions_from_text("c", "t", None)] == ["q2"]


def test_missing_id_gets_a_sequential_one_and_blank_statement_becomes_none(fake_model):
    fake_model(json.dumps([_question(id=None, statement="   "), _question(id="", statement=None)]))
    questions = eq._generate_questions_from_text("c", "t", None)
    assert [q.id for q in questions] == ["q1", "q2"]
    assert [q.statement for q in questions] == [None, None]


def test_question_count_is_capped(fake_model):
    fake_model(json.dumps([_question(f"q{i}") for i in range(80)]))
    assert len(eq._generate_questions_from_text("c", "t", None)) == eq.MAX_QUESTIONS == 60


@pytest.mark.parametrize("content", ["", "aucun tableau ici", "[pas du json]", '[{"options": "A", "correctAnswer": "A"}]'])
def test_unusable_model_output_gives_no_question(fake_model, content):
    fake_model(content)
    assert eq._generate_questions_from_text("c", "t", None) == []


def test_previous_questions_are_mentioned_but_limited(fake_model):
    client = fake_model("[]")
    eq._generate_questions_from_text("c", "t", None, [f"ancienne {i} " + "x" * 300 for i in range(20)])
    prompt = client.calls[0]["messages"][0]["content"]
    assert "ancienne 14" in prompt and "ancienne 15" not in prompt
    assert "x" * 200 in prompt and "x" * 201 not in prompt


# ── route ───────────────────────────────────────────────────────────────────

URL = "/generate"
LONG_TEXT = "Texte d'épreuve. " * 30     # plus de 200 caractères


@pytest.fixture
def setup(monkeypatch, sqlite_database):
    db = sqlite_database(eq, [dbm.Exam])
    db.add(dbm.Exam(Id=1, Title="BEPC 2023", ExamType="BEPC", Category="Maths", Year=2023,
                    DocumentUrl="https://b.s3.eu-west-1.amazonaws.com/e.pdf"),
           dbm.Exam(Id=2, Title="Supprimé", ExamType="BEPC", Category="Maths", Year=2022, IsDeleted=True),
           dbm.Exam(Id=3, Title="Sans fichier", ExamType="CEP", Category="Français", Year=2021, DocumentUrl=None))
    state = {"downloads": [], "text": LONG_TEXT, "questions": None}

    def fake_download(url):
        state["downloads"].append(url)
        return b"%PDF"

    monkeypatch.setattr(eq, "_download_pdf_bytes", fake_download)
    monkeypatch.setattr(eq, "_extract_pdf_text", lambda pdf: state["text"])

    def fake_generate(content, title, category, recent=None):
        state["generated"] = (content, title, category)
        return state["questions"] if state["questions"] is not None else [
            eq.QuizOptionQuestion(id="q1", question="Q", options=["A", "B"], correctAnswer="A", explanation="E")]

    monkeypatch.setattr(eq, "_generate_questions_from_text", fake_generate)
    app = FastAPI()
    app.include_router(eq.exam_quiz_router)
    client = TestClient(app)
    client.state = state
    return client


def _body(exam_id=1, url="https://b.s3.eu-west-1.amazonaws.com/e.pdf"):
    return {"exam_id": exam_id, "document_url": url, "title": "Titre du client"}


def test_requires_authentication(setup):
    assert setup.post(URL, json=_body()).status_code in (401, 403)


def test_body_is_validated(setup, headers_for):
    assert setup.post(URL, json={"exam_id": 1}, headers=headers_for()).status_code == 422


def test_generates_a_quiz_from_the_stored_document(setup, headers_for):
    r = setup.post(URL, json=_body(), headers=headers_for())
    assert r.status_code == 200
    assert r.json() == {"success": True, "data": {"questions": [
        {"id": "q1", "question": "Q", "options": ["A", "B"], "correctAnswer": "A", "explanation": "E",
         "statement": None}]}}
    # titre, matière et fichier viennent de la base, pas du client
    assert setup.state["generated"] == (LONG_TEXT, "BEPC 2023", "Maths")
    assert setup.state["downloads"] == ["https://b.s3.eu-west-1.amazonaws.com/e.pdf"]


def test_missing_or_deleted_exam_is_404(setup, headers_for):
    assert setup.post(URL, json=_body(999), headers=headers_for()).status_code == 404
    r = setup.post(URL, json=_body(2), headers=headers_for())
    assert r.status_code == 404 and r.json()["detail"] == "Épreuve introuvable."


def test_exam_without_any_document_is_422(setup, headers_for):
    r = setup.post(URL, json=_body(3, url=""), headers=headers_for())
    assert r.status_code == 422 and "pas de fichier PDF" in r.json()["detail"]


def test_client_url_is_used_when_the_exam_has_none(setup, headers_for):
    r = setup.post(URL, json=_body(3, url="https://b.s3.amazonaws.com/autre.pdf"), headers=headers_for())
    assert r.status_code == 200
    assert setup.state["downloads"] == ["https://b.s3.amazonaws.com/autre.pdf"]


def test_download_failures_are_422(monkeypatch, setup, headers_for):
    for error in (ValueError("URL non reconnue"),
                  ClientError({"Error": {"Code": "NoSuchKey", "Message": "absent"}}, "GetObject")):
        def failing(url, error=error):
            raise error

        monkeypatch.setattr(eq, "_download_pdf_bytes", failing)
        r = setup.post(URL, json=_body(), headers=headers_for())
        assert r.status_code == 422 and "n'a pas pu être récupéré" in r.json()["detail"]


def test_scanned_or_empty_pdf_is_422(setup, headers_for):
    setup.state["text"] = "x" * 199
    r = setup.post(URL, json=_body(), headers=headers_for())
    assert r.status_code == 422 and "PDF scanné ou vide" in r.json()["detail"]
    setup.state["text"] = " " * 500
    assert setup.post(URL, json=_body(), headers=headers_for()).status_code == 422


def test_no_question_generated_is_422(setup, headers_for):
    setup.state["questions"] = []
    r = setup.post(URL, json=_body(), headers=headers_for())
    assert r.status_code == 422 and "génération des questions a échoué" in r.json()["detail"]


def test_unexpected_error_is_500_with_the_message(monkeypatch, setup, headers_for):
    def boom(pdf):
        raise RuntimeError("pypdf a planté")

    monkeypatch.setattr(eq, "_extract_pdf_text", boom)
    r = setup.post(URL, json=_body(), headers=headers_for())
    assert r.status_code == 500 and r.json()["detail"] == "pypdf a planté"
