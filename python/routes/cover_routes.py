"""
Pochette de document générée par WinAI (image), à la demande.

Appelé uniquement par le backend .NET (CoverController), sur action explicite
de l'administrateur ou du professeur (case « Générer une pochette » à l'upload).

    POST /api/ai/cover/generate
    { "title": "...", "description": "...", "kind": "subject" }

Réponse (200) :
    { "imageBase64": "...", "mimeType": "image/png", "model": "gemini-..." }

Erreurs :
    503 si la clé est absente, si le modèle de génération d'image n'est pas
    accessible ou s'il ne renvoie aucune image. Le backend .NET traduit alors
    l'échec en message clair et n'assigne aucune pochette.

Enregistrement dans app.py :
    from routes.cover_routes import router as cover_router
    app.include_router(cover_router)
"""

import base64
import logging
from typing import Optional

from fastapi import APIRouter, Depends, HTTPException
from pydantic import BaseModel, Field

from auth import verify_token, UserTokenData
from services.cover_image_client import CoverImageUnavailable, generate_cover_png

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/ai/cover", tags=["ai-cover"])

_ALLOWED_KINDS = {"subject", "exam", "course"}


class CoverGenerateRequest(BaseModel):
    title: str = Field(..., min_length=1, max_length=300)
    description: Optional[str] = Field(None, max_length=4000)
    kind: str = "subject"


class CoverGenerateResponse(BaseModel):
    imageBase64: str
    mimeType: str
    model: str


@router.post("/generate", response_model=CoverGenerateResponse)
def generate_cover(
    request: CoverGenerateRequest,
    current_user: UserTokenData = Depends(verify_token),
):
    # def (et non async) : l'appel SDK est bloquant, FastAPI l'exécute dans un threadpool.
    if request.kind not in _ALLOWED_KINDS:
        raise HTTPException(status_code=400, detail="Type de contenu inconnu.")
    try:
        image = generate_cover_png(request.title, request.description, request.kind)
    except CoverImageUnavailable as exc:
        logger.warning("Pochette non générée (user %s) : %s", current_user.user_id, exc)
        raise HTTPException(status_code=503, detail=str(exc))

    return CoverGenerateResponse(
        imageBase64=base64.b64encode(image.png_bytes).decode("ascii"),
        mimeType=image.mime_type,
        model=image.model,
    )
