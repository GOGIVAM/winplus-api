"""
Extraction PDF native (Phase 1, §1.2) — commune aux deux moteurs : ni l'un ni
l'autre n'a de raison de repasser par l'OCR/une API payante pour un PDF dont
la couche texte est déjà encodée. PyMuPDF (fitz) n'est ni un framework ML ni
une API — c'est une lecture directe du PDF selon ISO 32000, donc hors du
débat "PyTorch uniquement" comme des autres arbitrages de ce module.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import List

import fitz  # PyMuPDF

# Ratio caractères-extractibles / surface-de-page en-dessous duquel un
# document est considéré comme scanné (pas de couche texte fiable) et doit
# être routé vers la chaîne OCR. Calibré empiriquement (Phase 0) ; à ajuster
# sur un échantillon réel du corpus WinPlus.
NATIVE_TEXT_RATIO_THRESHOLD = 0.015


@dataclass
class PageText:
    page_number: int  # 1-indexé
    text: str
    char_count: int
    page_area: float


@dataclass
class NativeExtraction:
    pages: List[PageText] = field(default_factory=list)
    is_native: bool = True

    @property
    def full_text(self) -> str:
        return "\n\n".join(p.text for p in self.pages if p.text.strip())


def extract_native_text(pdf_path: str) -> NativeExtraction:
    """Extraction directe de la couche texte + test de classification
    natif/scanné (Phase 1, §1.2 et arbre de décision §1.1)."""
    doc = fitz.open(pdf_path)
    pages: List[PageText] = []
    total_chars = 0
    total_area = 0.0

    try:
        for i, page in enumerate(doc, start=1):
            text = page.get_text("text") or ""
            rect = page.rect
            area = float(rect.width * rect.height)
            pages.append(PageText(page_number=i, text=text, char_count=len(text.strip()), page_area=area))
            total_chars += len(text.strip())
            total_area += area
    finally:
        doc.close()

    ratio = (total_chars / total_area) if total_area else 0.0
    is_native = ratio >= NATIVE_TEXT_RATIO_THRESHOLD
    return NativeExtraction(pages=pages, is_native=is_native)


def render_page_image(pdf_path: str, page_number: int, dpi: int = 200):
    """Rendu d'une page en image (bytes PNG) pour la chaîne OCR ou la
    détection de tampons. page_number est 1-indexé."""
    doc = fitz.open(pdf_path)
    try:
        page = doc[page_number - 1]
        pix = page.get_pixmap(dpi=dpi)
        return pix.tobytes("png")
    finally:
        doc.close()


def page_count(pdf_path: str) -> int:
    doc = fitz.open(pdf_path)
    try:
        return doc.page_count
    finally:
        doc.close()
