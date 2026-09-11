"""
Extraction structurée des tableaux par Table Transformer (Phase 1, §1.4).

Pipeline en quatre étapes fidèle au référentiel : (1) détection des régions
de tableau, (2) segmentation ligne/colonne/cellule, (3) reconstruction JSON
avec en-têtes, (4) le contenu textuel de chaque cellule est lu par GLM-OCR
plutôt que par un moteur de reconnaissance de caractères séparé — un seul
moteur de lecture visuelle pour tout le pipeline.
"""

from __future__ import annotations

import io
from dataclasses import dataclass
from typing import Dict, List

import torch
from PIL import Image

from RAG.self_hosted import config
from RAG.self_hosted.ingestion.ocr_engine import ocr_transcribe
from RAG.self_hosted.models.loader import get_table_detection_model, get_table_structure_model

DETECTION_SCORE_THRESHOLD = 0.7
STRUCTURE_SCORE_THRESHOLD = 0.6


@dataclass
class TableCell:
    row: int
    col: int
    text: str


@dataclass
class ExtractedTable:
    rows: int
    cols: int
    cells: List[TableCell]
    row_headers: List[str]
    col_headers: List[str]

    def to_json(self) -> Dict:
        return {
            "rows": self.rows,
            "cols": self.cols,
            "row_headers": self.row_headers,
            "col_headers": self.col_headers,
            "cells": [{"row": c.row, "col": c.col, "text": c.text} for c in self.cells],
        }

    def to_text(self) -> str:
        """Représentation textuelle linéarisée, utilisée pour l'embedding —
        conserve les en-têtes à côté de chaque valeur pour ne pas perdre la
        correspondance lors de la vectorisation."""
        lines = []
        for cell in self.cells:
            row_h = self.row_headers[cell.row] if cell.row < len(self.row_headers) else f"ligne {cell.row}"
            col_h = self.col_headers[cell.col] if cell.col < len(self.col_headers) else f"colonne {cell.col}"
            lines.append(f"{row_h} / {col_h} : {cell.text}")
        return "\n".join(lines)


def _detect_boxes(image: Image.Image, processor, model, threshold: float, label_names: Dict[int, str]):
    inputs = processor(images=image, return_tensors="pt").to(config.DEVICE)
    with torch.no_grad():
        outputs = model(**inputs)
    target_sizes = torch.tensor([image.size[::-1]])
    results = processor.post_process_object_detection(outputs, threshold=threshold, target_sizes=target_sizes)[0]

    boxes = []
    for score, label, box in zip(results["scores"], results["labels"], results["boxes"]):
        boxes.append(
            {
                "label": label_names.get(int(label), str(int(label))),
                "score": float(score),
                "box": [float(x) for x in box],
            }
        )
    return boxes


def detect_tables(image_bytes: bytes) -> List[List[float]]:
    """Étape 1 — bounding boxes des tableaux présents dans la page."""
    processor, model = get_table_detection_model()
    image = Image.open(io.BytesIO(image_bytes)).convert("RGB")
    boxes = _detect_boxes(
        image, processor, model, DETECTION_SCORE_THRESHOLD, {0: "table", 1: "table rotated"}
    )
    return [b["box"] for b in boxes]


def extract_table_structure(image_bytes: bytes, table_box: List[float]) -> ExtractedTable:
    """Étapes 2-3 — segmentation cellule par cellule et reconstruction de la
    structure relationnelle."""
    processor, model = get_table_structure_model()
    full_image = Image.open(io.BytesIO(image_bytes)).convert("RGB")
    x0, y0, x1, y1 = [int(v) for v in table_box]
    table_image = full_image.crop((x0, y0, x1, y1))

    label_names = {0: "table", 1: "column", 2: "row", 3: "column header", 4: "projected row header", 5: "spanning cell"}
    boxes = _detect_boxes(table_image, processor, model, STRUCTURE_SCORE_THRESHOLD, label_names)

    rows = sorted([b for b in boxes if b["label"] == "row"], key=lambda b: b["box"][1])
    cols = sorted([b for b in boxes if b["label"] == "column"], key=lambda b: b["box"][0])
    col_header_boxes = [b for b in boxes if b["label"] == "column header"]

    cells: List[TableCell] = []
    for r_idx, row in enumerate(rows):
        for c_idx, col in enumerate(cols):
            cell_box = (
                col["box"][0],
                row["box"][1],
                col["box"][2],
                row["box"][3],
            )
            cell_image = table_image.crop(cell_box)
            if cell_image.width < 3 or cell_image.height < 3:
                continue
            buf = io.BytesIO()
            cell_image.save(buf, format="PNG")
            text = ocr_transcribe(buf.getvalue()).strip()
            if text:
                cells.append(TableCell(row=r_idx, col=c_idx, text=text))

    col_headers = []
    for c_idx, col in enumerate(cols):
        header_text = ""
        for hb in col_header_boxes:
            if hb["box"][0] <= col["box"][0] < hb["box"][2]:
                crop = table_image.crop((col["box"][0], hb["box"][1], col["box"][2], hb["box"][3]))
                buf = io.BytesIO()
                crop.save(buf, format="PNG")
                header_text = ocr_transcribe(buf.getvalue()).strip()
                break
        col_headers.append(header_text or f"colonne {c_idx + 1}")

    row_headers = [
        next((c.text for c in cells if c.row == r_idx and c.col == 0), f"ligne {r_idx + 1}")
        for r_idx in range(len(rows))
    ]

    return ExtractedTable(rows=len(rows), cols=len(cols), cells=cells, row_headers=row_headers, col_headers=col_headers)
