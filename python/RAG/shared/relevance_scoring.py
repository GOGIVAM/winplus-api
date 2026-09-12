"""
Score de pertinence composite calculé à l'ingestion (topo validé avec
l'utilisateur : "au moment où [un document] est enregistré on lui assigne
un score de pertinence basé sur la connaissance actuelle").

Combine trois signaux indépendants (décision utilisateur : "on peut
combiner les 3") :
  - novelty   : le document apporte-t-il une information nouvelle par
    rapport à ce qui est déjà indexé dans le même périmètre (évite les
    quasi-doublons) ?
  - quality   : le contenu est-il pédagogiquement substantiel, jugé par
    DeepSeek (évite d'indexer des pages de garde, sommaires vides, etc.) ?
  - topic_fit : le contenu correspond-il au sujet/catégorie déclaré à
    l'upload (repère un document mal classé) ?

Le score composite (`ChunkMetadata.relevance_score`) sert à prioriser le
retrieval (les moteurs `engine/pipeline.py` des deux modules l'utilisent
comme boost du score de rerank) et à signaler, via les `warnings`
d'IngestResult, un document probablement mal classé ou de faible valeur —
sans jamais bloquer son indexation : un souci de scoring (clé API absente,
collection Qdrant pas encore créée, etc.) se neutralise silencieusement
plutôt que de faire échouer l'ingestion.
"""

from __future__ import annotations

import logging
import math
from typing import Callable, List, Optional, Tuple

logger = logging.getLogger(__name__)

_QUALITY_JUDGE_PROMPT = (
    "Tu es un évaluateur de contenu pédagogique. Note l'extrait ci-dessous "
    "de 0 à 1 selon sa valeur pédagogique substantielle (définitions, "
    "explications, exercices, exemples) par opposition à du contenu vide "
    "ou non informatif (page de garde, sommaire, mentions légales, texte "
    "tronqué).\n\nRéponds UNIQUEMENT avec un nombre entre 0 et 1 (ex: 0.85), "
    "rien d'autre.\n\nExtrait :\n"
)

EmbedFn = Callable[[List[str]], List[List[float]]]


def _cosine(a: List[float], b: List[float]) -> float:
    dot = sum(x * y for x, y in zip(a, b))
    norm_a = math.sqrt(sum(x * x for x in a))
    norm_b = math.sqrt(sum(y * y for y in b))
    if norm_a == 0 or norm_b == 0:
        return 0.0
    return dot / (norm_a * norm_b)


def resolve_topic_label(subject_id: Optional[int], category: Optional[str]) -> Optional[str]:
    """Le libellé comparé au contenu pour le score `topic_fit`. `category`
    (déjà transmis à l'ingestion par l'appelant .NET) est préféré, plus
    spécifique qu'un titre de matière générique ; à défaut, on résout le
    titre du Subject depuis la base (même process, `database.py` déjà
    utilisé par tout le reste de l'app FastAPI)."""
    if category:
        return category
    if subject_id is None:
        return None
    try:
        from database import Database, Subject

        db = Database()
        session = db.SessionLocal()
        try:
            title = session.query(Subject.Title).filter(Subject.Id == subject_id).scalar()
            return title
        finally:
            session.close()
    except Exception as e:
        logger.debug(f"[RAG relevance] Résolution du libellé de sujet échouée : {e}")
        return None


def _quality_score_llm(text_sample: str) -> Optional[float]:
    """None si le jugement échoue (clé API absente, etc.) — dégradation
    silencieuse, ne bloque jamais l'ingestion."""
    try:
        from services.deepseek_client import get_deepseek_client

        client = get_deepseek_client()
        result = client.chat(
            messages=[{"role": "user", "content": text_sample[:2000]}],
            system_prompt=_QUALITY_JUDGE_PROMPT,
            max_tokens=10,
            temperature=0.0,
        )
        if not result.get("success"):
            return None
        raw = result.get("content", "").strip()
        score = float(raw.split()[0])
        return max(0.0, min(1.0, score))
    except Exception as e:
        logger.debug(f"[RAG relevance] Jugement qualité LLM indisponible : {e}")
        return None


def _novelty_score(sample_vector: List[float], collection: str, filters: dict) -> Optional[float]:
    """1 - similarité cosinus avec le voisin le plus proche déjà indexé
    dans le même périmètre (subject_id/course_id/owner_user_id, status
    actif). None si le calcul échoue (collection pas encore créée sur la
    toute première ingestion du périmètre, etc.)."""
    try:
        from RAG.shared.vector_store import search_dense

        hits = search_dense(collection, sample_vector, top_k=1, filters=filters)
        if not hits:
            return 1.0  # rien à comparer dans ce périmètre : contenu forcément inédit
        return max(0.0, 1.0 - hits[0].score)
    except Exception as e:
        logger.debug(f"[RAG relevance] Score de nouveauté indisponible (probablement 1ère ingestion) : {e}")
        return None


def _topic_fit_score(sample_vector: List[float], topic_label: str, embed_fn: EmbedFn) -> Optional[float]:
    try:
        topic_vector = embed_fn([topic_label])[0]
        return max(0.0, _cosine(sample_vector, topic_vector))
    except Exception as e:
        logger.debug(f"[RAG relevance] Score d'adéquation au sujet indisponible : {e}")
        return None


def compute_relevance_score(
    chunks_text: List[str],
    embed_fn: EmbedFn,
    collection: str,
    filters: dict,
    topic_label: Optional[str] = None,
) -> Tuple[float, dict]:
    """Calcule le score composite pour UN document à partir d'un
    échantillon de ses premiers chunks (un document entier partage la même
    thématique/qualité globale — inutile de ré-embedder tout le document
    une seconde fois pour le scoring). Renvoie (score, détail) où détail
    contient les trois sous-scores bruts, pour audit."""
    if not chunks_text:
        return 0.5, {"novelty": None, "quality": None, "topic_fit": None, "reason": "no_chunks"}

    sample_text = "\n".join(chunks_text[:3])[:3000]

    try:
        sample_vector = embed_fn([sample_text])[0]
    except Exception as e:
        logger.warning(f"[RAG relevance] Embedding d'échantillon échoué, score neutre par défaut : {e}")
        return 0.5, {"novelty": None, "quality": None, "topic_fit": None, "reason": str(e)}

    novelty = _novelty_score(sample_vector, collection, filters)
    quality = _quality_score_llm(sample_text)
    topic_fit = _topic_fit_score(sample_vector, topic_label, embed_fn) if topic_label else None

    # Composite (décision utilisateur : combiner les 3). topic_fit agit en
    # multiplicateur/porte : un document hors-sujet ne doit pas remonter
    # haut au retrieval même nouveau et de bonne qualité par ailleurs. Un
    # sous-score indisponible (échec silencieux d'une dépendance externe)
    # est neutralisé plutôt que de pénaliser le document pour un problème
    # d'infrastructure sans rapport avec son contenu réel.
    novelty_v = novelty if novelty is not None else 0.75
    quality_v = quality if quality is not None else 0.75
    topic_fit_v = topic_fit if topic_fit is not None else 1.0

    composite = topic_fit_v * (0.5 * novelty_v + 0.5 * quality_v)
    return round(composite, 3), {"novelty": novelty, "quality": quality, "topic_fit": topic_fit}
