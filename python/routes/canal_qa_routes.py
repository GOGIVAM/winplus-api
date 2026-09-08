"""
WinAI — Q&A automatique du canal de formation (Module 7, 3C).

- POST /winai/canal-qa → répond à une question élève à partir du contenu des
  leçons de la formation, avec un score de confiance auto-évalué. Sous le
  seuil de confiance, renvoie {"action": "tag_professor"} plutôt qu'une
  réponse inventée.

Ce endpoint est un calcul sans état : la persistance (journal des
interactions WinAI, messages du canal) reste côté .NET
(CourseChannelController), comme le reste des endpoints WinAI proxifiés
depuis le backend C#.
"""

import json
import logging
from typing import List, Optional

from fastapi import APIRouter, Depends
from pydantic import BaseModel

from auth import verify_token, UserTokenData
from services.deepseek_client import get_deepseek_client

logger = logging.getLogger(__name__)

canal_qa_router = APIRouter()

CONFIDENCE_THRESHOLD = 0.70


class CanalQaRequest(BaseModel):
    question: str
    course_title: Optional[str] = None
    lessons_context: List[str] = []


class CanalQaResponse(BaseModel):
    action: str  # "answered" | "tag_professor"
    answer: Optional[str] = None
    confidence: float = 0.0


def _build_prompt(body: CanalQaRequest) -> tuple[str, str]:
    context = "\n\n".join(body.lessons_context)[:6000]  # garde-fou taille de prompt
    system = (
        "Tu es WinAI, assistant pédagogique pour la formation "
        f"« {body.course_title or 'cette formation'} » sur WinPlus. "
        "Réponds UNIQUEMENT à partir du contenu des leçons fourni ci-dessous. "
        "Si le contenu ne permet pas de répondre avec certitude, ne devine pas — "
        "signale une confiance basse plutôt que d'inventer une réponse. "
        "Réponds en JSON strict : "
        '{"answer": "réponse claire en 2-4 phrases, ou null si tu n\'es pas sûr", '
        '"confidence": 0.0 à 1.0 (ta confiance réelle que cette réponse est correcte '
        "et bien couverte par le contenu des leçons)}."
        f"\n\nContenu des leçons :\n{context}"
    )
    return body.question, system


@canal_qa_router.post("/winai/canal-qa", response_model=CanalQaResponse)
async def canal_qa(
    body: CanalQaRequest,
    current_user: UserTokenData = Depends(verify_token),
):
    question, system = _build_prompt(body)

    try:
        ds = get_deepseek_client()
        res = ds.chat(
            messages=[{"role": "user", "content": question}],
            system_prompt=system,
            max_tokens=400,
            temperature=0.3,  # réponse factuelle, pas créative — la confiance doit être fiable
        )
        raw = res.get("content", "").strip()
        if raw.startswith("```"):
            raw = "\n".join(raw.split("\n")[1:])
        if raw.endswith("```"):
            raw = raw.rsplit("```", 1)[0].strip()
        parsed = json.loads(raw)
    except Exception as e:
        logger.warning(f"canal_qa: échec de génération/parsing — {e}")
        return CanalQaResponse(action="tag_professor", confidence=0.0)

    confidence = float(parsed.get("confidence", 0.0) or 0.0)
    answer = parsed.get("answer")

    if confidence < CONFIDENCE_THRESHOLD or not answer:
        return CanalQaResponse(action="tag_professor", confidence=confidence)

    return CanalQaResponse(action="answered", answer=str(answer), confidence=confidence)
