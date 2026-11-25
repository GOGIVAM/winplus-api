"""Extraction de texte des documents personnels qui ne sont ni PDF ni vidéo
(atelier WinAI : sources téléversées par l'élève) : texte brut et Word.

Sans dépendance nouvelle : un .docx est une archive ZIP dont le texte se trouve
dans word/document.xml (paragraphes <w:p>, textes <w:t>, tabulations et sauts
de ligne). Suffisant pour l'indexation ; la mise en forme est ignorée."""

from __future__ import annotations

import re
import zipfile
from xml.etree import ElementTree

TEXT_EXTENSIONS = {".txt", ".md", ".csv"}
WORD_EXTENSIONS = {".docx"}
IMAGE_EXTENSIONS = {".png", ".jpg", ".jpeg", ".webp"}

_W = "{http://schemas.openxmlformats.org/wordprocessingml/2006/main}"


def read_text_file(path: str) -> str:
    with open(path, "rb") as f:
        raw = f.read()
    for encoding in ("utf-8-sig", "utf-16", "cp1252"):
        try:
            return raw.decode(encoding)
        except UnicodeDecodeError:
            continue
    return raw.decode("utf-8", errors="ignore")


def read_docx(path: str) -> str:
    with zipfile.ZipFile(path) as archive:
        xml = archive.read("word/document.xml")
    root = ElementTree.fromstring(xml)
    paragraphs = []
    for p in root.iter(f"{_W}p"):
        parts = []
        for node in p.iter():
            if node.tag == f"{_W}t" and node.text:
                parts.append(node.text)
            elif node.tag == f"{_W}tab":
                parts.append("\t")
            elif node.tag in (f"{_W}br", f"{_W}cr"):
                parts.append("\n")
        text = "".join(parts).strip()
        if text:
            paragraphs.append(text)
    return re.sub(r"\n{3,}", "\n\n", "\n\n".join(paragraphs))
