"""
WinAI — Gestion de la mémoire conversationnelle persistante (Module 6, 6C).

Réutilise la table UserAIMemories déjà en place et déjà alimentée pour TOUS
les rôles (chatbot_routes.py : _load_user_memories / _extract_and_save_memories
ne filtrent pas par rôle — un professeur ou un répétiteur qui discute avec
WinAI accumule déjà des mémoires, exactement comme un élève). Le référentiel
demandait une table séparée "WinAI_Memoire" ; ne pas la créer évite deux
mécanismes de mémoire parallèles à maintenir pour le même chatbot.

- GET    /winai/memoire       → mémoires de l'utilisateur connecté + suggestion
                                  de reprise déterministe basée sur "unfinished_topic"
- DELETE /winai/memoire/{id}  → supprime UNE mémoire précise par son id
                                  (MemoryType seul n'identifie pas une ligne
                                  unique : plusieurs mémoires peuvent partager
                                  un type pour un même utilisateur)
"""

import logging
from typing import List, Optional

from fastapi import APIRouter, Depends, HTTPException

from auth import verify_token, UserTokenData
from database import Database, UserAIMemory
from services.prompt_builder import _MEMORY_TYPE_LABELS

logger = logging.getLogger(__name__)

winai_memory_router = APIRouter()


def _continuation_suggestion(content: str) -> str:
    return f"On avait commencé « {content} » — tu veux continuer ?"


@winai_memory_router.get("/winai/memoire")
async def get_memories(current_user: UserTokenData = Depends(verify_token)):
    db = Database()
    session = db.SessionLocal()
    try:
        rows = (
            session.query(UserAIMemory)
            .filter(UserAIMemory.UserId == current_user.user_id)
            .order_by(UserAIMemory.UpdatedAt.desc())
            .all()
        )
        items = [
            {
                "id": r.Id,
                "type": r.MemoryType,
                "typeLabel": _MEMORY_TYPE_LABELS.get(r.MemoryType, r.MemoryType),
                "content": r.Content,
                "updatedAt": r.UpdatedAt.isoformat() if r.UpdatedAt else None,
            }
            for r in rows
            if r.MemoryType != "unfinished_topic"
        ]

        unfinished = next((r for r in rows if r.MemoryType == "unfinished_topic"), None)
        suggestion = _continuation_suggestion(unfinished.Content) if unfinished else None

        return {"memories": items, "continuationSuggestion": suggestion}
    finally:
        session.close()


@winai_memory_router.delete("/winai/memoire/{memory_id}")
async def delete_memory(memory_id: int, current_user: UserTokenData = Depends(verify_token)):
    db = Database()
    session = db.SessionLocal()
    try:
        row = session.query(UserAIMemory).filter(
            UserAIMemory.Id == memory_id,
            UserAIMemory.UserId == current_user.user_id,
        ).first()
        if not row:
            raise HTTPException(status_code=404, detail="Mémoire introuvable.")
        session.delete(row)
        session.commit()
        return {"success": True}
    finally:
        session.close()
