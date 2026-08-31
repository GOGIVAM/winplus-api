"""
WinAI  Endpoints IA pour le compte Administrateur.

- POST /admin/anomaly-detection          → Détection d'anomalies plateforme
- GET  /admin/anomalies/active           → Liste des anomalies non résolues
- POST /admin/anomaly/{id}/resolve       → Marquer une anomalie résolue
- POST /admin/content-health-check       → Analyse qualité d'un contenu
- POST /admin/growth-insights            → Insights de croissance hebdomadaires
- POST /admin/revenue-forecast           → Prévision revenus 30/60/90 jours
- POST /admin/moderate-content           → Modération de post forum
- GET  /admin/moderation/pending         → Posts forum en attente de modération
- POST /admin/moderation/{id}/resolve    → Approuver/rejeter un post modéré
"""

import json
import logging
from datetime import datetime, timedelta, date, timezone
from typing import Any, Dict, List, Optional

from fastapi import APIRouter, Depends, HTTPException, Query
from pydantic import BaseModel
from sqlalchemy import Column, Integer, String, Text, DateTime, Boolean, Numeric, create_engine, func
from sqlalchemy.ext.declarative import declarative_base
from sqlalchemy.orm import sessionmaker
import os

from auth import verify_token, require_role, UserTokenData
from database import Database, DailyScore, Enrollment, Subject, User, ChatMessage, LearningHistory
from services.deepseek_client import get_deepseek_client

logger = logging.getLogger(__name__)
admin_router = APIRouter()

# ─────────────────────────────────────────────────────────────────────────────
# Local SQLAlchemy models (Python-managed tables)
# ─────────────────────────────────────────────────────────────────────────────

_ABase = declarative_base()

class PlatformAnomalyModel(_ABase):
    __tablename__ = 'PlatformAnomalies'
    Id          = Column(Integer, primary_key=True, autoincrement=True)
    Type        = Column(String(100), nullable=False)
    Severity    = Column(String(20), nullable=False)   # low | medium | high | critical
    Description = Column(Text, nullable=False)
    DetectedAt  = Column(DateTime, nullable=False, default=datetime.utcnow)
    ResolvedAt  = Column(DateTime, nullable=True)
    Resolution  = Column(Text, nullable=True)
    Metadata    = Column(Text, nullable=True)          # JSON blob for extra details


class ForumModerationModel(_ABase):
    __tablename__ = 'ForumModerationQueue'
    Id           = Column(Integer, primary_key=True, autoincrement=True)
    PostId       = Column(Integer, nullable=False, index=True)
    ThreadId     = Column(Integer, nullable=False)
    ContentText  = Column(Text, nullable=False)
    AuthorId     = Column(Integer, nullable=True)
    Verdict      = Column(String(30), nullable=False)   # safe|spam|inappropriate|off_topic|needs_review
    Confidence   = Column(Numeric(4, 2), nullable=True)
    Reason       = Column(Text, nullable=True)
    Status       = Column(String(20), nullable=False, default='pending')  # pending|approved|rejected
    CreatedAt    = Column(DateTime, nullable=False, default=datetime.utcnow)
    ResolvedAt   = Column(DateTime, nullable=True)
    AdminNote    = Column(Text, nullable=True)


def _get_admin_engine():
    db = Database()
    return db.engine


def _init_admin_tables():
    try:
        engine = _get_admin_engine()
        _ABase.metadata.create_all(engine, checkfirst=True)
    except Exception as e:
        logger.warning(f"Could not init admin tables: {e}")


_admin_session_factory: Optional[Any] = None


def _admin_session():
    global _admin_session_factory
    if _admin_session_factory is None:
        engine = _get_admin_engine()
        _ABase.metadata.create_all(engine, checkfirst=True)
        _admin_session_factory = sessionmaker(bind=engine)
    return _admin_session_factory()


# ─────────────────────────────────────────────────────────────────────────────
# Helpers
# ─────────────────────────────────────────────────────────────────────────────

_db = Database()


def _deepseek_json(prompt: str, system: str, max_tokens: int = 800) -> Any:
    try:
        ds = get_deepseek_client()
        res = ds.chat(
            messages=[{"role": "user", "content": prompt}],
            system_prompt=system,
            max_tokens=max_tokens,
            temperature=0.4,
        )
        raw = res.get("content", "").strip()
        if raw.startswith("```"):
            raw = "\n".join(raw.split("\n")[1:])
        if raw.endswith("```"):
            raw = raw.rsplit("```", 1)[0].strip()
        return json.loads(raw)
    except Exception as e:
        logger.warning(f"_deepseek_json error: {e}")
        return None


def _deepseek_text(prompt: str, system: str, max_tokens: int = 400) -> str:
    try:
        ds = get_deepseek_client()
        res = ds.chat(
            messages=[{"role": "user", "content": prompt}],
            system_prompt=system,
            max_tokens=max_tokens,
            temperature=0.5,
        )
        return res.get("content", "").strip()
    except Exception as e:
        logger.warning(f"_deepseek_text error: {e}")
        return ""


# ─────────────────────────────────────────────────────────────────────────────
# 1. Détection d'Anomalies en Temps Réel
# ─────────────────────────────────────────────────────────────────────────────

class AnomalyDetectionRequest(BaseModel):
    force_refresh: bool = False


def _detect_anomalies_raw(session) -> List[Dict]:
    """Analyse les patterns de la plateforme et retourne une liste de signaux suspects."""
    signals = []
    now = datetime.utcnow()
    today = date.today()

    try:
        # Signal 1: Pic de création de comptes (dernières 24h vs moyenne 7j)
        last_24h = now - timedelta(hours=24)
        last_7d = now - timedelta(days=7)
        new_24h = session.query(func.count(User.Id)).filter(User.CreatedAt >= last_24h).scalar() or 0
        avg_daily_7d = max(
            (session.query(func.count(User.Id)).filter(User.CreatedAt >= last_7d).scalar() or 0) / 7, 1
        )
        if new_24h > avg_daily_7d * 3 and new_24h >= 10:
            signals.append({
                "type": "account_spike",
                "severity": "high" if new_24h > avg_daily_7d * 5 else "medium",
                "raw": {"new_24h": int(new_24h), "avg_daily_7d": round(avg_daily_7d, 1)},
            })

        # Signal 2: Chute soudaine des enrollments (possible problème de paiement/UX)
        enroll_24h = session.query(func.count(Enrollment.Id)).filter(
            Enrollment.EnrolledAt >= last_24h, Enrollment.IsDeleted == False
        ).scalar() or 0
        avg_enroll_daily = max(
            (session.query(func.count(Enrollment.Id)).filter(
                Enrollment.EnrolledAt >= last_7d, Enrollment.IsDeleted == False
            ).scalar() or 0) / 7, 1
        )
        if enroll_24h < avg_enroll_daily * 0.3 and avg_enroll_daily > 2:
            signals.append({
                "type": "enrollment_drop",
                "severity": "high",
                "raw": {"enroll_24h": int(enroll_24h), "avg_daily": round(avg_enroll_daily, 1)},
            })

        # Signal 3: Activité nocturne anormale (02h00-04h00 UTC)
        night_start = datetime.combine(today, __import__('datetime').time(2, 0))
        night_end   = datetime.combine(today, __import__('datetime').time(4, 0))
        night_activity = session.query(func.count(LearningHistory.Id)).filter(
            LearningHistory.Timestamp >= night_start,
            LearningHistory.Timestamp < night_end,
        ).scalar() or 0
        if night_activity > 50:
            signals.append({
                "type": "nocturnal_spike",
                "severity": "medium",
                "raw": {"night_activity": int(night_activity), "window": "02h-04h UTC"},
            })

        # Signal 4: Score moyen plateforme en chute (possible problème qualité contenu)
        today_avg = session.query(func.avg(DailyScore.AverageScore)).filter(
            DailyScore.Date == today
        ).scalar()
        yesterday_avg = session.query(func.avg(DailyScore.AverageScore)).filter(
            DailyScore.Date == (today - timedelta(days=1))
        ).scalar()
        if today_avg and yesterday_avg and float(today_avg) < float(yesterday_avg) * 0.7:
            signals.append({
                "type": "score_drop",
                "severity": "high",
                "raw": {"today_avg": round(float(today_avg), 1), "yesterday_avg": round(float(yesterday_avg), 1)},
            })

        # Signal 5: Concentration de messages WinAI sur un sujet (opportunité de contenu)
        recent_messages = session.query(ChatMessage.Content).filter(
            ChatMessage.CreatedAt >= last_7d,
            ChatMessage.IsDeleted == False,
        ).limit(500).all()
        if recent_messages:
            from collections import Counter
            words = []
            for (msg,) in recent_messages:
                if msg:
                    words.extend(w.lower() for w in msg.split() if len(w) > 5)
            freq = Counter(words).most_common(5)
            top_word, top_count = freq[0] if freq else ("", 0)
            if top_count > 30:
                signals.append({
                    "type": "topic_concentration",
                    "severity": "low",
                    "raw": {"keyword": top_word, "occurrences": top_count, "period": "7j"},
                })
    except Exception as e:
        logger.warning(f"Anomaly detection signal collection error: {e}")

    return signals


@admin_router.post("/admin/anomaly-detection")
async def detect_anomalies(
    body: AnomalyDetectionRequest,
    current_user: UserTokenData = Depends(require_role("admin")),
):
    db_session = _db.SessionLocal()
    admin_sess = _admin_session()
    try:
        signals = _detect_anomalies_raw(db_session)

        if not signals:
            return {"anomalies": [], "scanned_at": datetime.utcnow().isoformat()}

        # Let DeepSeek interpret signals as anomaly descriptions
        prompt = (
            f"La plateforme éducative WinPlus a détecté ces signaux statistiques :\n"
            f"{json.dumps(signals, ensure_ascii=False, indent=2)}\n\n"
            "Génère une anomalie JSON pour chaque signal (EXACTEMENT un objet par signal) :\n"
            '[{"type":"account_spike","severity":"high","description":"Description analytique précise en français","action_suggestion":"Action recommandée"}]\n'
            "Les types existants : account_spike, enrollment_drop, nocturnal_spike, score_drop, topic_concentration."
        )
        system = (
            "Tu es WinAI, l'auditeur IA de la plateforme WinPlus. "
            "Tu transformes des signaux statistiques bruts en anomalies compréhensibles pour un admin. "
            "Réponds UNIQUEMENT en JSON valide, un tableau d'objets."
        )
        interpreted = _deepseek_json(prompt, system, max_tokens=1000)
        if not isinstance(interpreted, list):
            interpreted = [
                {
                    "type": s["type"],
                    "severity": s["severity"],
                    "description": f"Signal détecté : {s['type']}  données : {json.dumps(s['raw'], ensure_ascii=False)}",
                    "action_suggestion": "Investiguer manuellement.",
                }
                for s in signals
            ]

        # Persist anomalies (deduplicate by type in last 24h)
        stored_ids = []
        for a in interpreted:
            existing = admin_sess.query(PlatformAnomalyModel).filter(
                PlatformAnomalyModel.Type == a.get("type", "unknown"),
                PlatformAnomalyModel.DetectedAt >= datetime.utcnow() - timedelta(hours=24),
                PlatformAnomalyModel.ResolvedAt.is_(None),
            ).first()
            if not existing:
                anomaly = PlatformAnomalyModel(
                    Type=a.get("type", "unknown"),
                    Severity=a.get("severity", "medium"),
                    Description=a.get("description", ""),
                    DetectedAt=datetime.utcnow(),
                    Metadata=json.dumps({"action_suggestion": a.get("action_suggestion", "")}, ensure_ascii=False),
                )
                admin_sess.add(anomaly)
                admin_sess.flush()
                stored_ids.append(anomaly.Id)
        admin_sess.commit()

        return {
            "anomalies": interpreted,
            "scanned_at": datetime.utcnow().isoformat(),
            "new_count": len(stored_ids),
        }
    finally:
        db_session.close()
        admin_sess.close()


@admin_router.get("/admin/anomalies/active")
async def get_active_anomalies(
    current_user: UserTokenData = Depends(require_role("admin")),
):
    admin_sess = _admin_session()
    try:
        rows = admin_sess.query(PlatformAnomalyModel).filter(
            PlatformAnomalyModel.ResolvedAt.is_(None)
        ).order_by(PlatformAnomalyModel.DetectedAt.desc()).limit(50).all()

        SEVERITY_ORDER = {"critical": 0, "high": 1, "medium": 2, "low": 3}
        result = sorted([
            {
                "id": r.Id,
                "type": r.Type,
                "severity": r.Severity,
                "description": r.Description,
                "detected_at": r.DetectedAt.isoformat() if r.DetectedAt else None,
                "action_suggestion": json.loads(r.Metadata or "{}").get("action_suggestion", ""),
            }
            for r in rows
        ], key=lambda x: SEVERITY_ORDER.get(x["severity"], 9))

        return {"anomalies": result, "total": len(result)}
    finally:
        admin_sess.close()


@admin_router.post("/admin/anomaly/{anomaly_id}/resolve")
async def resolve_anomaly(
    anomaly_id: int,
    resolution: str = Query(..., description="Note de résolution"),
    current_user: UserTokenData = Depends(require_role("admin")),
):
    admin_sess = _admin_session()
    try:
        row = admin_sess.query(PlatformAnomalyModel).filter(PlatformAnomalyModel.Id == anomaly_id).first()
        if not row:
            raise HTTPException(status_code=404, detail="Anomaly not found")
        row.ResolvedAt = datetime.utcnow()
        row.Resolution = resolution
        admin_sess.commit()
        return {"success": True}
    finally:
        admin_sess.close()


# ─────────────────────────────────────────────────────────────────────────────
# 2. Analyse de Santé du Contenu
# ─────────────────────────────────────────────────────────────────────────────

class ContentHealthRequest(BaseModel):
    content_id: int
    pdf_text: Optional[str] = None
    metadata: Optional[Dict] = None


@admin_router.post("/admin/content-health-check")
async def content_health_check(
    body: ContentHealthRequest,
    current_user: UserTokenData = Depends(require_role("admin")),
):
    meta = body.metadata or {}
    title = meta.get("title", f"Contenu #{body.content_id}")
    content_type = meta.get("type", "epreuve")
    subject = meta.get("subject", "")
    has_pdf = bool(body.pdf_text and len(body.pdf_text) > 100)
    pdf_excerpt = (body.pdf_text or "")[:1500]

    prompt = (
        f"Analyse ce contenu éducatif pour la plateforme WinPlus :\n"
        f"Titre : {title}\nType : {content_type}\nMatière : {subject}\n"
        f"Texte extrait (extrait) : {pdf_excerpt if has_pdf else 'Non disponible'}\n\n"
        "Retourne UN SEUL objet JSON strict avec ces champs exacts :\n"
        '{"quality_score": 78, "issues": [{"type": "missing_answer_key", "severity": "high", "detail": "..."}], '
        '"recommendation": "approve", "auto_moderation_note": "..."}\n'
        "recommendation doit être : approve | request_revision | reject\n"
        "severity des issues : high | medium | low\n"
        "Types d'issues possibles : missing_answer_key, image_quality_low, incomplete_content, off_topic, "
        "duplicate_content, poor_formatting, missing_metadata, copyright_concern\n"
        "quality_score entre 0 et 100. approve si >= 70 et aucun issue high."
    )
    system = (
        "Tu es WinAI, le modérateur de contenu éducatif de WinPlus. "
        "Tu analyses les épreuves, corrections et livres avant publication. "
        "Réponds UNIQUEMENT avec un objet JSON valide, aucun texte avant ou après."
    )

    result = _deepseek_json(prompt, system, max_tokens=700)
    if not isinstance(result, dict) or "quality_score" not in result:
        result = {
            "quality_score": 65 if has_pdf else 40,
            "issues": [] if has_pdf else [{"type": "incomplete_content", "severity": "high", "detail": "Aucun fichier PDF fourni."}],
            "recommendation": "request_revision" if not has_pdf else "approve",
            "auto_moderation_note": "Analyse automatique non disponible  révision manuelle recommandée.",
        }

    # Ensure recommendation matches quality_score
    qs = result.get("quality_score", 0)
    high_issues = [i for i in result.get("issues", []) if i.get("severity") == "high"]
    if qs >= 70 and not high_issues:
        result["recommendation"] = "approve"
    elif qs < 30 or any(i.get("type") == "copyright_concern" for i in result.get("issues", [])):
        result["recommendation"] = "reject"
    else:
        result["recommendation"] = result.get("recommendation", "request_revision")

    return {**result, "content_id": body.content_id}


# ─────────────────────────────────────────────────────────────────────────────
# 3. Insights de Croissance WinAI
# ─────────────────────────────────────────────────────────────────────────────

class GrowthInsightsRequest(BaseModel):
    period_days: int = 7
    stats: Optional[Dict] = None


@admin_router.post("/admin/growth-insights")
async def growth_insights(
    body: GrowthInsightsRequest,
    current_user: UserTokenData = Depends(require_role("admin")),
):
    # Compute real platform stats if not provided
    db_session = _db.SessionLocal()
    try:
        now = datetime.utcnow()
        cutoff = now - timedelta(days=body.period_days)
        prev_cutoff = now - timedelta(days=body.period_days * 2)

        stats = body.stats or {}

        if not stats:
            new_users = session_scalar(db_session, func.count(User.Id), User.CreatedAt >= cutoff) or 0
            total_users = session_scalar(db_session, func.count(User.Id)) or 0
            new_enrollments = session_scalar(
                db_session, func.count(Enrollment.Id),
                Enrollment.EnrolledAt >= cutoff, Enrollment.IsDeleted == False
            ) or 0
            avg_score = session_scalar(db_session, func.avg(DailyScore.AverageScore), DailyScore.Date >= cutoff.date()) or 0
            prev_avg  = session_scalar(db_session, func.avg(DailyScore.AverageScore),
                                       DailyScore.Date >= prev_cutoff.date(), DailyScore.Date < cutoff.date()) or 0

            top_subjects = (
                db_session.query(Subject.Title, Subject.Category, func.count(Enrollment.Id).label("cnt"))
                .join(Enrollment, Enrollment.SubjectId == Subject.Id)
                .filter(Enrollment.EnrolledAt >= cutoff, Enrollment.IsDeleted == False)
                .group_by(Subject.Id, Subject.Title, Subject.Category)
                .order_by(func.count(Enrollment.Id).desc())
                .limit(5)
                .all()
            )

            stats = {
                "period_days": body.period_days,
                "new_users": int(new_users),
                "total_users": int(total_users),
                "new_enrollments": int(new_enrollments),
                "avg_score": round(float(avg_score), 1),
                "prev_avg_score": round(float(prev_avg), 1),
                "score_trend": round(float(avg_score) - float(prev_avg), 1),
                "top_subjects": [{"title": r.Title, "category": r.Category, "enrollments": r.cnt} for r in top_subjects],
            }

        prompt = (
            f"La plateforme éducative WinPlus ({stats.get('total_users', '?')} utilisateurs) "
            f"sur les {stats.get('period_days', 7)} derniers jours :\n"
            f"{json.dumps(stats, ensure_ascii=False, indent=2)}\n\n"
            "Génère 4 insights de croissance actionnables en JSON (tableau d'objets) :\n"
            '[{"insight": "Texte analytique précis avec chiffres", "category": "revenue|retention|acquisition|content|geography", '
            '"action": "Action concrète recommandée", "priority": "high|medium|low"}]\n'
            "Les insights doivent : citer des chiffres précis, être spécifiques à l'Afrique/Cameroun, "
            "mentionner des leviers de croissance concrets. Vocabulaire : conversion, rétention, churn, cohorte."
        )
        system = (
            "Tu es WinAI, l'analyste de croissance de WinPlus. "
            "Tu fournis des insights stratégiques basés sur les données réelles de la plateforme. "
            "Réponds UNIQUEMENT en JSON valide, tableau de 4 objets."
        )

        insights_raw = _deepseek_json(prompt, system, max_tokens=1200)
        if not isinstance(insights_raw, list) or len(insights_raw) == 0:
            insights_raw = [
                {
                    "insight": f"{stats.get('new_enrollments', 0)} nouveaux enrollments en {stats.get('period_days', 7)} jours  croissance à analyser.",
                    "category": "acquisition",
                    "action": "Identifier les sources d'acquisition les plus performantes.",
                    "priority": "high",
                },
                {
                    "insight": f"Score moyen plateforme : {stats.get('avg_score', 0)}%  objectif 70%.",
                    "category": "content",
                    "action": "Renforcer les contenus pour les matières sous-performantes.",
                    "priority": "medium",
                },
                {
                    "insight": "Taux de rétention à analyser  indicateur clé de santé plateforme.",
                    "category": "retention",
                    "action": "Mettre en place des emails de réengagement à J+7 et J+30.",
                    "priority": "high",
                },
                {
                    "insight": "Opportunité de croissance sur la géographie sous-exploitée.",
                    "category": "geography",
                    "action": "Lancer une campagne marketing ciblée sur Douala.",
                    "priority": "medium",
                },
            ]

        return {
            "insights": insights_raw[:5],
            "generated_at": datetime.utcnow().isoformat(),
            "period_days": body.period_days,
            "data_context": stats,
        }
    finally:
        db_session.close()


def session_scalar(session, agg, *filters):
    q = session.query(agg)
    for f in filters:
        q = q.filter(f)
    return q.scalar()


# ─────────────────────────────────────────────────────────────────────────────
# 4. Prévision de Revenus IA
# ─────────────────────────────────────────────────────────────────────────────

class RevenueForecastRequest(BaseModel):
    monthly_revenues: Optional[List[Dict]] = None   # [{"month": "2024-01", "revenue": 850000}]
    current_month_revenue: Optional[float] = None
    currency: str = "XAF"


@admin_router.post("/admin/revenue-forecast")
async def revenue_forecast(
    body: RevenueForecastRequest,
    current_user: UserTokenData = Depends(require_role("admin")),
):
    # If no revenue data provided, use enrollment counts as proxy
    db_session = _db.SessionLocal()
    try:
        history = body.monthly_revenues or []

        if not history:
            # Build from Enrollment counts per month (proxy)
            rows = (
                db_session.query(
                    func.date_trunc("month", Enrollment.EnrolledAt).label("month"),
                    func.count(Enrollment.Id).label("count"),
                )
                .filter(Enrollment.IsDeleted == False)
                .group_by(func.date_trunc("month", Enrollment.EnrolledAt))
                .order_by(func.date_trunc("month", Enrollment.EnrolledAt))
                .limit(12)
                .all()
            )
            history = [{"month": str(r.month)[:7], "enrollments": int(r.count)} for r in rows]

        prompt = (
            "Tu es l'analyste financier IA de WinPlus, une plateforme éducative camerounaise.\n"
            f"Données historiques (12 mois) : {json.dumps(history, ensure_ascii=False)}\n"
            f"Revenu du mois en cours : {body.current_month_revenue or 'non fourni'} {body.currency}\n\n"
            "Génère une prévision de revenus pour les 30, 60 et 90 prochains jours en JSON strict :\n"
            '{"forecast_30d": {"low": 850000, "mid": 1100000, "high": 1350000, "confidence": 0.72}, '
            '"forecast_60d": {"low": ..., "mid": ..., "high": ..., "confidence": ...}, '
            '"forecast_90d": {"low": ..., "mid": ..., "high": ..., "confidence": ...}, '
            '"key_drivers": ["...", "..."], '
            '"risks": ["...", "..."], '
            '"seasonality_note": "..."}\n'
            "Les valeurs en XAF. confidence entre 0 et 1. "
            "Tenir compte de la saisonnalité africaine : examens BAC en mai-juin, rentrée en septembre."
        )
        system = (
            "Tu es WinAI, le prévisionniste financier de WinPlus. "
            "Tu utilises les patterns historiques et la saisonnalité des examens africains "
            "pour générer des prévisions de revenus réalistes. "
            "Réponds UNIQUEMENT en JSON valide."
        )

        result = _deepseek_json(prompt, system, max_tokens=800)
        if not isinstance(result, dict) or "forecast_30d" not in result:
            # Fallback: simple trend extrapolation
            if history and any("revenue" in h for h in history):
                recent_revs = [h.get("revenue", 0) for h in history[-3:] if h.get("revenue")]
            else:
                recent_revs = [100] * 3
            avg = sum(recent_revs) / max(len(recent_revs), 1)
            result = {
                "forecast_30d": {"low": round(avg * 0.85), "mid": round(avg * 1.0), "high": round(avg * 1.2), "confidence": 0.5},
                "forecast_60d": {"low": round(avg * 0.9), "mid": round(avg * 1.1), "high": round(avg * 1.35), "confidence": 0.45},
                "forecast_90d": {"low": round(avg * 0.8), "mid": round(avg * 1.15), "high": round(avg * 1.5), "confidence": 0.4},
                "key_drivers": ["Examens nationaux (BAC, ENSP)", "Rentrée scolaire"],
                "risks": ["Variation de la concurrence", "Baisse d'activité en vacances"],
                "seasonality_note": "Pic prévu en période d'examens (mai-juin).",
            }

        return {**result, "generated_at": datetime.utcnow().isoformat(), "currency": body.currency}
    finally:
        db_session.close()


# ─────────────────────────────────────────────────────────────────────────────
# 5. Modération de Forum
# ─────────────────────────────────────────────────────────────────────────────

class ModerateContentRequest(BaseModel):
    post_id: int
    thread_id: int
    content_text: str
    author_id: Optional[int] = None
    confidence_threshold: float = 0.8


@admin_router.post("/admin/moderate-content")
async def moderate_content(
    body: ModerateContentRequest,
    current_user: UserTokenData = Depends(verify_token),
):
    admin_sess = _admin_session()
    try:
        # Check if already moderated
        existing = admin_sess.query(ForumModerationModel).filter(
            ForumModerationModel.PostId == body.post_id
        ).first()
        if existing:
            return {
                "post_id": body.post_id,
                "verdict": existing.Verdict,
                "confidence": float(existing.Confidence or 0),
                "reason": existing.Reason,
                "action": _verdict_to_action(existing.Verdict, float(existing.Confidence or 0), body.confidence_threshold),
            }

        prompt = (
            f"Classifie ce message de forum éducatif en français :\n"
            f"---\n{body.content_text[:1000]}\n---\n\n"
            "Retourne UN objet JSON strict :\n"
            '{"verdict": "safe", "confidence": 0.95, "reason": "Contenu académique pertinent."}\n'
            "verdict doit être exactement : safe | spam | inappropriate | off_topic | needs_review\n"
            "confidence entre 0.0 et 1.0. Sois strict sur spam et inappropriate."
        )
        system = (
            "Tu es WinAI, le modérateur du forum éducatif WinPlus. "
            "Tu protèges la communauté des étudiants camerounais contre les contenus inappropriés. "
            "Réponds UNIQUEMENT en JSON valide, objet unique."
        )

        result = _deepseek_json(prompt, system, max_tokens=200)
        if not isinstance(result, dict) or "verdict" not in result:
            result = {"verdict": "needs_review", "confidence": 0.5, "reason": "Modération automatique indisponible."}

        verdict = result.get("verdict", "needs_review")
        confidence = float(result.get("confidence", 0.5))
        reason = result.get("reason", "")

        # Store in moderation queue
        row = ForumModerationModel(
            PostId=body.post_id,
            ThreadId=body.thread_id,
            ContentText=body.content_text[:2000],
            AuthorId=body.author_id,
            Verdict=verdict,
            Confidence=confidence,
            Reason=reason,
            Status="pending" if verdict in ("spam", "inappropriate", "needs_review") else "auto_approved",
        )
        admin_sess.add(row)
        admin_sess.commit()

        action = _verdict_to_action(verdict, confidence, body.confidence_threshold)
        return {
            "post_id": body.post_id,
            "verdict": verdict,
            "confidence": confidence,
            "reason": reason,
            "action": action,
        }
    finally:
        admin_sess.close()


def _verdict_to_action(verdict: str, confidence: float, threshold: float) -> str:
    if verdict == "safe":
        return "publish"
    if verdict in ("spam", "inappropriate") and confidence >= threshold:
        return "hold_for_review"
    if verdict in ("off_topic", "needs_review"):
        return "publish_with_flag"
    return "publish"


class ModerateMessageRequest(BaseModel):
    content: str
    author_id: int
    confidence_threshold: float = 0.8


@admin_router.post("/admin/moderate-message")
async def moderate_message(
    body: ModerateMessageRequest,
    current_user: UserTokenData = Depends(verify_token),
):
    """Modération WinAI d'un message privé (sans stockage en base)."""
    prompt = (
        f"Classifie ce message privé d'une plateforme éducative en français :\n"
        f"---\n{body.content[:800]}\n---\n\n"
        "Retourne UN objet JSON strict :\n"
        '{"verdict": "safe", "confidence": 0.95, "reason": "Message respectueux."}\n'
        "verdict doit être exactement : safe | spam | inappropriate | harassment | needs_review\n"
        "confidence entre 0.0 et 1.0. "
        "Sois strict sur harassment et inappropriate ; indulgent sur off_topic."
    )
    system = (
        "Tu es WinAI, le modérateur de la messagerie éducative WinPlus. "
        "Tu protèges les élèves et enseignants camerounais contre le harcèlement et les contenus offensants. "
        "Réponds UNIQUEMENT en JSON valide, objet unique."
    )

    try:
        result = _deepseek_json(prompt, system, max_tokens=150)
        if not isinstance(result, dict) or "verdict" not in result:
            result = {"verdict": "needs_review", "confidence": 0.5, "reason": "Analyse indisponible."}
    except Exception:
        result = {"verdict": "safe", "confidence": 1.0, "reason": "Service indisponible  message autorisé."}

    verdict = result.get("verdict", "safe")
    confidence = float(result.get("confidence", 0.5))
    reason = result.get("reason", "")
    action = _verdict_to_action(verdict, confidence, body.confidence_threshold)

    return {"verdict": verdict, "confidence": confidence, "reason": reason, "action": action}


@admin_router.get("/admin/moderation/pending")
async def get_pending_moderation(
    current_user: UserTokenData = Depends(require_role("admin")),
):
    admin_sess = _admin_session()
    try:
        rows = admin_sess.query(ForumModerationModel).filter(
            ForumModerationModel.Status == "pending"
        ).order_by(ForumModerationModel.CreatedAt.desc()).limit(100).all()

        return {
            "posts": [
                {
                    "id": r.Id,
                    "post_id": r.PostId,
                    "thread_id": r.ThreadId,
                    "content_text": r.ContentText,
                    "author_id": r.AuthorId,
                    "verdict": r.Verdict,
                    "confidence": float(r.Confidence or 0),
                    "reason": r.Reason,
                    "status": r.Status,
                    "created_at": r.CreatedAt.isoformat() if r.CreatedAt else None,
                }
                for r in rows
            ],
            "total": len(rows),
        }
    finally:
        admin_sess.close()


@admin_router.post("/admin/moderation/{moderation_id}/resolve")
async def resolve_moderation(
    moderation_id: int,
    action: str = Query(..., description="approve | reject"),
    note: Optional[str] = Query(None),
    current_user: UserTokenData = Depends(require_role("admin")),
):
    admin_sess = _admin_session()
    try:
        row = admin_sess.query(ForumModerationModel).filter(
            ForumModerationModel.Id == moderation_id
        ).first()
        if not row:
            raise HTTPException(status_code=404, detail="Not found")
        row.Status = "approved" if action == "approve" else "rejected"
        row.ResolvedAt = datetime.utcnow()
        row.AdminNote = note
        admin_sess.commit()
        return {"success": True, "post_id": row.PostId, "action": action}
    finally:
        admin_sess.close()
