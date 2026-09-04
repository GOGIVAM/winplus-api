"""
WinAI  Mode évaluation
POST /api/exam-quiz/generate  Génère un quiz chronométré à partir du
contenu RÉEL du PDF d'une épreuve (et non plus seulement de sa matière).

Le fichier est téléchargé depuis S3 (Exam.DocumentUrl, format standard
https://{bucket}.s3.{region}.amazonaws.com/{key}), son texte est extrait
via pypdf, puis DeepSeek génère des questions à choix multiples basées
strictement sur ce texte. Le .NET (QuizService.GetOrCreateExamQuizAsync)
persiste ensuite le résultat comme Quiz.ExamId = examId.
"""

import io
import json
import logging
import re
from typing import List, Optional
from urllib.parse import unquote, urlparse

import boto3
from botocore.exceptions import BotoCoreError, ClientError
from fastapi import APIRouter, Depends, HTTPException
from pydantic import BaseModel
from pypdf import PdfReader

from auth import verify_token, UserTokenData
from database import Database, Exam
from services.deepseek_client import get_deepseek_client

logger = logging.getLogger(__name__)

exam_quiz_router = APIRouter()

# Assez de contenu pour couvrir une épreuve type sans dépasser la fenêtre du
# modèle ni gonfler inutilement le coût de génération.
MAX_PDF_PAGES = 20
MAX_EXTRACTED_CHARS = 14000
QUESTION_COUNT = 8


class GenerateExamQuizRequest(BaseModel):
    exam_id: int
    document_url: str
    title: str
    category: Optional[str] = None


class QuizOptionQuestion(BaseModel):
    id: str
    question: str
    options: List[str]
    correctAnswer: str
    explanation: str


def _parse_s3_url(document_url: str) -> Optional[tuple[str, str, str]]:
    """https://{bucket}.s3.{region}.amazonaws.com/{key} → (bucket, region, key)."""
    try:
        parsed = urlparse(document_url)
        match = re.match(r"^([^.]+)\.s3\.([^.]+)\.amazonaws\.com$", parsed.netloc)
        if not match:
            return None
        bucket, region = match.group(1), match.group(2)
        key = unquote(parsed.path.lstrip("/"))
        if not key:
            return None
        return bucket, region, key
    except Exception:
        return None


def _download_pdf_bytes(document_url: str) -> bytes:
    parsed = _parse_s3_url(document_url)
    if not parsed:
        raise ValueError("URL de document non reconnue (format S3 attendu).")
    bucket, region, key = parsed
    # Pas d'identifiants explicites : boto3 utilise la chaîne de credentials
    # par défaut (rôle IAM en production, variables AWS_* en local), la même
    # que celle déjà utilisée côté .NET pour ce bucket.
    s3 = boto3.client("s3", region_name=region)
    obj = s3.get_object(Bucket=bucket, Key=key)
    return obj["Body"].read()


def _extract_pdf_text(pdf_bytes: bytes) -> str:
    reader = PdfReader(io.BytesIO(pdf_bytes))
    chunks = []
    total_len = 0
    for page in reader.pages[:MAX_PDF_PAGES]:
        text = (page.extract_text() or "").strip()
        if not text:
            continue
        chunks.append(text)
        total_len += len(text)
        if total_len >= MAX_EXTRACTED_CHARS:
            break
    return "\n\n".join(chunks)[:MAX_EXTRACTED_CHARS]


def _generate_questions_from_text(content: str, title: str, category: Optional[str]) -> List[QuizOptionQuestion]:
    prompt = (
        f"Voici le contenu extrait d'une épreuve intitulée « {title} »"
        f"{f' (matière : {category})' if category else ''} :\n\n"
        f"---\n{content}\n---\n\n"
        f"Génère exactement {QUESTION_COUNT} questions à choix multiples qui évaluent la compréhension "
        f"des exercices et notions PRÉSENTS DANS CE TEXTE, pas des questions génériques sur la matière. "
        f"Base-toi uniquement sur le contenu ci-dessus. Mélange les niveaux de difficulté. "
        f'Format JSON strict, un tableau : '
        f'[{{"id":"q1","question":"...","options":["A) ...","B) ...","C) ...","D) ..."],'
        f'"correctAnswer":"B) ...","explanation":"..."}}] '
        f"correctAnswer doit correspondre EXACTEMENT à une des chaînes de options. "
        f"Réponds UNIQUEMENT avec le tableau JSON, sans texte autour ni balises de code."
    )
    system = (
        "Tu es WinAI, expert en évaluation pédagogique pour les examens camerounais. "
        "Tu génères des QCM fidèles au contenu fourni, jamais des questions inventées hors sujet."
    )

    client = get_deepseek_client()
    result = client.chat(
        messages=[{"role": "user", "content": prompt}],
        system_prompt=system,
        max_tokens=2200,
        temperature=0.4,
    )
    raw = result.get("content", "").strip()

    questions: List[QuizOptionQuestion] = []
    try:
        match = re.search(r"\[.*\]", raw, re.DOTALL)
        parsed = json.loads(match.group()) if match else []
        for i, q in enumerate(parsed[:QUESTION_COUNT]):
            options = [str(o) for o in q.get("options", [])]
            correct = str(q.get("correctAnswer", ""))
            if not options or correct not in options:
                continue
            questions.append(QuizOptionQuestion(
                id=str(q.get("id") or f"q{i + 1}"),
                question=str(q.get("question", "")).strip(),
                options=options,
                correctAnswer=correct,
                explanation=str(q.get("explanation", "")).strip(),
            ))
    except Exception as e:
        logger.warning(f"[exam-quiz] parse error: {e}")

    return questions


@exam_quiz_router.post("/generate")
async def generate_exam_quiz(
    body: GenerateExamQuizRequest,
    current_user: UserTokenData = Depends(verify_token),
):
    db_obj = Database()
    session = db_obj.SessionLocal()
    try:
        exam = session.query(Exam).filter(Exam.Id == body.exam_id, Exam.IsDeleted == False).first()  # noqa: E712
        if not exam:
            raise HTTPException(status_code=404, detail="Épreuve introuvable.")

        document_url = exam.DocumentUrl or body.document_url
        if not document_url:
            raise HTTPException(status_code=422, detail="Cette épreuve n'a pas de fichier PDF associé.")

        try:
            pdf_bytes = _download_pdf_bytes(document_url)
        except (BotoCoreError, ClientError, ValueError) as e:
            logger.error(f"[exam-quiz] download error for exam {body.exam_id}: {e}")
            raise HTTPException(status_code=422, detail="Le fichier de l'épreuve n'a pas pu être récupéré.")

        text = _extract_pdf_text(pdf_bytes)
        if len(text.strip()) < 200:
            # PDF scanné (image sans couche texte) ou vide : aucune base fiable
            # pour générer des questions fidèles au contenu.
            raise HTTPException(
                status_code=422,
                detail="Le contenu de cette épreuve n'a pas pu être lu (PDF scanné ou vide)."
            )

        questions = _generate_questions_from_text(text, exam.Title or body.title, exam.Category or body.category)
        if not questions:
            raise HTTPException(status_code=422, detail="La génération des questions a échoué. Réessayez.")

        return {
            "success": True,
            "data": {"questions": [q.model_dump() for q in questions]},
        }
    except HTTPException:
        raise
    except Exception as exc:
        logger.error(f"[exam-quiz] generate error: {exc}")
        raise HTTPException(status_code=500, detail=str(exc))
    finally:
        session.close()
