"""Seuil natif/scanné de RAG.shared.pdf_utils, recalibré le 2026-10-21 :
une page A4 de texte tapé doit être lue directement, sans OCR."""

import fitz
import pytest

from RAG.shared.pdf_utils import extract_native_text


def _pdf(tmp_path, chars_per_page: int, pages: int = 2) -> str:
    doc = fitz.open()
    line = "Exercice de mathematiques : calculer la derivee de f(x) = 3x^2 + 2x. "
    for _ in range(pages):
        page = doc.new_page(width=595, height=842)  # A4
        text = (line * (chars_per_page // len(line) + 1))[:chars_per_page]
        page.insert_textbox(fitz.Rect(20, 20, 575, 822), text, fontsize=6)
    path = tmp_path / "doc.pdf"
    doc.save(str(path))
    doc.close()
    return str(path)


@pytest.mark.parametrize("chars", [400, 2500])
def test_typed_pages_are_native(tmp_path, chars):
    assert extract_native_text(_pdf(tmp_path, chars)).is_native


def test_blank_pages_go_to_ocr(tmp_path):
    assert not extract_native_text(_pdf(tmp_path, 0)).is_native
