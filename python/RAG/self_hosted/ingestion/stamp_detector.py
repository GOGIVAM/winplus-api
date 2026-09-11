"""
Détection de tampons administratifs par transformée de Hough (Phase 1,
§1.3) — traitement d'image classique (OpenCV), sans inférence neuronale :
hors du débat PyTorch, au même titre que Qdrant ou rank_bm25.
"""

from __future__ import annotations

import cv2
import numpy as np

# Superficie minimale (en pixels²) pour qu'une forme circulaire détectée
# soit considérée comme un tampon plutôt qu'un artefact de numérisation.
# Calibré en Phase 0 sur un échantillon représentatif.
MIN_STAMP_AREA_PX = 2000


def has_stamp(image_bytes: bytes) -> bool:
    arr = np.frombuffer(image_bytes, dtype=np.uint8)
    img = cv2.imdecode(arr, cv2.IMREAD_GRAYSCALE)
    if img is None:
        return False

    img = cv2.medianBlur(img, 5)
    circles = cv2.HoughCircles(
        img,
        cv2.HOUGH_GRADIENT,
        dp=1.2,
        minDist=img.shape[0] / 8,
        param1=100,
        param2=40,
        minRadius=15,
        maxRadius=min(img.shape) // 2,
    )
    if circles is None:
        return False

    for circle in circles[0]:
        radius = circle[2]
        area = np.pi * radius * radius
        if area >= MIN_STAMP_AREA_PX:
            return True
    return False
