"""
Module 13 (lot 6)  Base de contexte partagée pour le conseiller WinAI
parent, le rapport hebdomadaire et les insights inter-enfants.

Avant ce module, ces trois briques recalculaient chacune leur propre version
partielle des mêmes données (voir chatbot_routes._load_parent_children_data
et parent_extra_routes.get_children_insights, chacune limitée au score moyen
sur 30 jours). Ce module expose un seul calcul, réutilisable par les trois,
pour qu'un même indicateur affiche la même valeur partout.

Vérifie systématiquement le lien parent-enfant avant de renvoyer quoi que ce
soit (ParentStudentLink.Status == 'accepted'), conformément à l'audit : un
parent ne doit jamais pouvoir obtenir le contexte d'un enfant qui n'est pas
le sien, y compris en appelant directement les endpoints.
"""

import logging
from datetime import datetime, timedelta, timezone
from typing import Optional

from database import (
    Database, User, ParentStudentLink, DailyScore, QuizMistake,
    TeacherClassStudent, Assignment, Submission, AcademicRecord,
    ExamCoachPlanNet, Enrollment, Subject, QuizAttempt,
)

logger = logging.getLogger(__name__)


class ParentAccessDenied(Exception):
    """Levée quand l'appelant n'est pas le parent lié à l'enfant demandé."""


def assert_parent_linked(session, parent_id: int, child_id: int) -> None:
    linked = session.query(ParentStudentLink).filter(
        ParentStudentLink.ParentId == parent_id,
        ParentStudentLink.StudentId == child_id,
        ParentStudentLink.Status == "accepted",
    ).first()
    if linked is None:
        raise ParentAccessDenied(f"L'enfant {child_id} n'est pas lié au parent {parent_id}.")


def build_child_context(parent_id: int, child_id: int, session=None) -> Optional[dict]:
    """
    Vérifie le lien parent-enfant puis calcule le contexte partagé (voir
    compute_child_context). Lève ParentAccessDenied si le lien n'est pas
    vérifié. Point d'entrée pour les consommateurs qui n'ont pas déjà leur
    propre contrôle d'accès (ex. chatbot_routes, où seul le rôle parent peut
    appeler cette fonction).
    """
    own_session = session is None
    db = Database()
    session = session or db.SessionLocal()
    try:
        assert_parent_linked(session, parent_id, child_id)
        return compute_child_context(child_id, session=session)
    finally:
        if own_session:
            session.close()


def compute_child_context(child_id: int, session=None) -> Optional[dict]:
    """
    Calcule le contexte partagé d'un enfant, SANS vérifier de lien
    parent-enfant : score moyen et tendance vs. la période précédente,
    lacunes de quiz récentes, devoirs en cours/en retard, extrait de
    bulletin, statut de veille d'examen, matières réellement inscrites.

    Réservé aux appelants qui ont déjà fait leur propre contrôle d'accès
    (ex. parent_extra_routes._assert_can_access_child, qui autorise aussi
    l'enfant lui-même et l'administrateur, un périmètre plus large que le
    simple lien parent-enfant de build_child_context ci-dessus). N'appelle
    jamais cette fonction sans un contrôle d'accès préalable.

    Retourne None si l'enfant n'existe pas.
    """
    own_session = session is None
    db = Database()
    session = session or db.SessionLocal()
    try:
        child = session.query(User).filter(User.Id == child_id).first()
        if not child:
            return None

        now = datetime.now(timezone.utc)
        cutoff_30 = now - timedelta(days=30)
        cutoff_60 = now - timedelta(days=60)

        # Score moyen (30j) et tendance vs. les 30 jours précédents (même
        # fenêtre que WeeklyParentReportService côté .NET, pour que le
        # dashboard email et le chat affichent le même chiffre).
        try:
            recent_scores = session.query(DailyScore).filter(
                DailyScore.UserId == child_id, DailyScore.CreatedAt >= cutoff_30
            ).all()
            previous_scores = session.query(DailyScore).filter(
                DailyScore.UserId == child_id,
                DailyScore.CreatedAt >= cutoff_60,
                DailyScore.CreatedAt < cutoff_30,
            ).all()
        except Exception:
            session.rollback()
            recent_scores, previous_scores = [], []

        avg_score = (
            round(sum(float(s.AverageScore) for s in recent_scores) / len(recent_scores) * 20 / 100, 1)
            if recent_scores else None
        )
        avg_score_previous = (
            round(sum(float(s.AverageScore) for s in previous_scores) / len(previous_scores) * 20 / 100, 1)
            if previous_scores else None
        )
        trend = None
        if avg_score is not None and avg_score_previous is not None:
            trend = round(avg_score - avg_score_previous, 1)

        # Lacunes de quiz récentes (non résolues), mêmes critères que l'élève
        # lui-même (QuizMistake, voir _load_quiz_mistakes).
        try:
            mistakes = (
                session.query(QuizMistake)
                .filter(QuizMistake.UserId == child_id, QuizMistake.IsResolved == False)
                .order_by(QuizMistake.CreatedAt.desc())
                .limit(5)
                .all()
            )
            quiz_gaps = [m.Subject or "Général" for m in mistakes]
        except Exception:
            session.rollback()
            quiz_gaps = []

        # Devoirs en cours / en retard : toutes les classes de l'enfant,
        # devoirs sans soumission correspondante.
        pending_homework, late_homework = [], []
        try:
            class_ids = [
                row[0] for row in
                session.query(TeacherClassStudent.TeacherClassId)
                .filter(TeacherClassStudent.StudentId == child_id).all()
            ]
            if class_ids:
                assignments = (
                    session.query(Assignment)
                    .filter(Assignment.TeacherClassId.in_(class_ids))
                    .order_by(Assignment.DueDate.asc())
                    .limit(30)
                    .all()
                )
                submitted_ids = {
                    s.AssignmentId for s in
                    session.query(Submission.AssignmentId)
                    .filter(Submission.StudentId == child_id).all()
                }
                for a in assignments:
                    if a.Id in submitted_ids:
                        continue
                    # "id" ajouté au lot 6.1 (14.1) : nécessaire à
                    # detect_threshold_alerts ci-dessous pour construire une clé
                    # de déduplication par devoir précis (DedupKey côté
                    # ParentAlert) plutôt que par titre/date, ambigu en cas de
                    # devoirs homonymes. Ajout additif : les consommateurs
                    # existants (chat parent) ne lisaient que "title"/"due_date".
                    entry = {"id": a.Id, "title": a.Title, "due_date": a.DueDate.date().isoformat() if a.DueDate else None}
                    if a.DueDate and a.DueDate < now:
                        late_homework.append(entry)
                    else:
                        pending_homework.append(entry)
        except Exception:
            session.rollback()

        # Extrait de bulletin le plus récent.
        bulletin_extract = None
        try:
            record = (
                session.query(AcademicRecord)
                .filter(AcademicRecord.StudentId == child_id)
                .order_by(AcademicRecord.SchoolYear.desc())
                .first()
            )
            if record:
                bulletin_extract = {
                    "school_year": record.SchoolYear,
                    "average_grade": float(record.AverageGrade),
                }
        except Exception:
            session.rollback()

        # Statut de veille d'examen (déjà exploité par le chat, voir
        # _load_parent_children_data  réutilisé ici pour unifier le calcul).
        exam_watch = None
        try:
            watch_plan = (
                session.query(ExamCoachPlanNet)
                .filter(
                    ExamCoachPlanNet.UserId == child_id,
                    ExamCoachPlanNet.IsActive == True,
                    ExamCoachPlanNet.ParentWatchModeActivatedAt.isnot(None),
                )
                .first()
            )
            if watch_plan is not None:
                exam_watch = {
                    "exam_type": watch_plan.ExamType,
                    "exam_date": watch_plan.ExamDate.date().isoformat(),
                }
        except Exception:
            session.rollback()

        # Matières réellement inscrites  aucun endpoint de génération ne
        # les utilisait nulle part (constat Module 16) ; exposées ici pour
        # que le chat parent cesse d'avoir un champ "matières" toujours vide.
        subjects: list = []
        try:
            subjects = [
                title for (title,) in
                session.query(Subject.Title)
                .join(Enrollment, Enrollment.SubjectId == Subject.Id)
                .filter(Enrollment.UserId == child_id, Enrollment.IsDeleted == False)
                .limit(6)
                .all()
            ]
        except Exception:
            session.rollback()

        return {
            "child_id": child_id,
            "name": child.FirstName or f"Enfant {child_id}",
            "level": child.Level or "",
            "avg_score": avg_score,
            "avg_score_trend": trend,
            "quiz_gaps": quiz_gaps,
            "pending_homework": pending_homework,
            "late_homework": late_homework,
            "bulletin_extract": bulletin_extract,
            "exam_watch": exam_watch,
            "subjects": subjects,
        }
    except ParentAccessDenied:
        raise
    except Exception as e:
        logger.warning(f"Could not build shared parent/child context for child {child_id}: {e}")
        return None
    finally:
        if own_session:
            session.close()


# ═══════════════════════════════════════════════════════════════════════════
# Partie 14.1 (décision produit du 2026-10-08)  Proactivité du conseiller
# parent par alertes ciblées sur seuils.
#
# Jusqu'ici, compute_child_context ci-dessus n'était consommé qu'à la demande
# (chat parent, rapport hebdomadaire ouverts par le parent)  jamais de
# détection spontanée d'un franchissement de seuil. Ce qui suit ajoute cette
# détection, réutilisant explicitement les mêmes données déjà calculées par
# compute_child_context plutôt que d'en recalculer une troisième version.
#
# Seuils retenus (documentés dans le rapport, pas de nouvel arbitrage produit
# nécessaire  cf. décision 14.1, "à définir à l'implémentation") :
#   - SCORE_DROP_THRESHOLD  = 2.0 points /20 : baisse de la moyenne glissante
#     30j vs les 30j précédents (même indicateur que le chat/dashboard, pour
#     qu'une alerte renvoie toujours au même chiffre que ce que le parent
#     peut vérifier). Severity "High" si la baisse atteint 4 points.
#   - LATE_HOMEWORK_MIN_DAYS = 3 jours de retard avant alerte (en dessous, le
#     retard est encore probablement en cours de rattrapage  pas de bruit
#     pour un jour de retard isolé).
#   - EXAM_WATCH_MAX_DAYS   = 5 jours avant l'examen suivi, ET aucune activité
#     récente (DailyScore ou QuizAttempt) dans les RECENT_ACTIVITY_DAYS = 3
#     derniers jours, toutes matières confondues. Limite assumée : ExamType
#     (texte libre) n'est pas fiablement rattachable à un Subject.Id précis
#     dans le schéma actuel, donc l'activité récente est vérifiée tous sujets
#     confondus plutôt que strictement sur la matière de l'examen (cf. rapport
#     final, section "restes").
#
# Non-répétition (exigée par la décision) : chaque alerte porte une
# dedup_key stable pour le même évènement (mois civil pour la baisse de
# score, id du devoir pour un retard, date d'examen pour la veille), que
# l'appelant (.NET, ParentAdvisorThresholdService) utilise pour ne jamais
# réinsérer deux fois la même ligne dans ParentAlerts (colonne DedupKey,
# voir SQL_AddParentAlertDedupKey.sql).
# ═══════════════════════════════════════════════════════════════════════════

SCORE_DROP_THRESHOLD = 2.0        # points sur 20
SCORE_DROP_HIGH_THRESHOLD = 4.0   # points sur 20 → severity High
LATE_HOMEWORK_MIN_DAYS = 3
EXAM_WATCH_MAX_DAYS = 5
RECENT_ACTIVITY_DAYS = 3


def _has_recent_activity(session, child_id: int, days: int, now: datetime) -> bool:
    """Toute activité récente (score journalier ou tentative de quiz), tous
    sujets confondus  voir limite documentée ci-dessus sur l'absence de
    rattachement fiable ExamType -> Subject.Id."""
    cutoff = now - timedelta(days=days)
    try:
        if session.query(DailyScore.Id).filter(
            DailyScore.UserId == child_id, DailyScore.CreatedAt >= cutoff
        ).first():
            return True
        if session.query(QuizAttempt.Id).filter(
            QuizAttempt.UserId == child_id, QuizAttempt.CompletedAt >= cutoff
        ).first():
            return True
        return False
    except Exception:
        session.rollback()
        return False


def detect_threshold_alerts(child_id: int, session=None) -> list:
    """
    Calcule les franchissements de seuil pour un enfant, à partir du même
    contexte partagé que le chat/rapport (compute_child_context). Ne vérifie
    AUCUN lien parent-enfant (même contrat que compute_child_context) : réservé
    à un appelant qui l'a déjà fait  voir routes/parent_advisor_routes.py,
    où le lien est filtré en amont côté .NET (ParentStudentLink.Status =
    'accepted') avant même l'appel à cette fonction.

    Retourne une liste de dicts {type, severity, content, dedup_key,
    detected_at}, jamais persistée ici : la persistance (ParentAlerts) et la
    notification (NtfyService, lot 1 module 22) sont de la responsabilité de
    l'appelant .NET, pour rester dans le même schéma que
    CourseInactivityAlertService/detection-decrochage (Python détecte/score,
    .NET persiste et notifie).
    """
    own_session = session is None
    db = Database()
    session = session or db.SessionLocal()
    try:
        ctx = compute_child_context(child_id, session=session)
        if ctx is None:
            return []

        now = datetime.now(timezone.utc)
        name = ctx["name"]
        alerts: list = []

        # ── Seuil 1 : baisse de score moyenne (30j vs 30j précédents) ───────
        trend = ctx.get("avg_score_trend")
        if trend is not None and trend <= -SCORE_DROP_THRESHOLD:
            severity = "High" if trend <= -SCORE_DROP_HIGH_THRESHOLD else "Medium"
            alerts.append({
                "type": "BaisseScoreSeuil",
                "severity": severity,
                "content": (
                    f"Le score moyen de {name} a baissé de {abs(trend):.1f} points sur 20 "
                    "ces 30 derniers jours par rapport aux 30 jours précédents."
                ),
                "detected_at": now.isoformat(),
                "dedup_key": f"scoredrop:{now:%Y-%m}",
            })

        # ── Seuil 2 : devoir en retard depuis au moins N jours ───────────────
        for hw in ctx.get("late_homework", []):
            if not hw.get("due_date") or not hw.get("id"):
                continue
            try:
                due = datetime.fromisoformat(hw["due_date"]).replace(tzinfo=timezone.utc)
            except ValueError:
                continue
            days_late = (now - due).days
            if days_late >= LATE_HOMEWORK_MIN_DAYS:
                alerts.append({
                    "type": "DevoirRetardProlonge",
                    "severity": "High" if days_late >= 7 else "Medium",
                    "content": (
                        f"Le devoir « {hw['title']} » de {name} est en retard depuis {days_late} jour(s)."
                    ),
                    "detected_at": now.isoformat(),
                    "dedup_key": f"homework:{hw['id']}",
                })

        # ── Seuil 3 : examen imminent sans révision récente ──────────────────
        watch = ctx.get("exam_watch")
        if watch and watch.get("exam_date"):
            try:
                exam_date = datetime.fromisoformat(watch["exam_date"]).replace(tzinfo=timezone.utc)
            except ValueError:
                exam_date = None
            if exam_date is not None:
                days_to_exam = (exam_date - now).days
                if 0 <= days_to_exam <= EXAM_WATCH_MAX_DAYS and not _has_recent_activity(
                    session, child_id, RECENT_ACTIVITY_DAYS, now
                ):
                    alerts.append({
                        "type": "ExamenImminentSansRevision",
                        "severity": "High" if days_to_exam <= 2 else "Medium",
                        "content": (
                            f"L'examen de {name} ({watch.get('exam_type') or 'à venir'}) approche "
                            f"(dans {days_to_exam} jour(s)) sans activité de révision récente."
                        ),
                        "detected_at": now.isoformat(),
                        "dedup_key": f"examwatch:{watch['exam_date']}",
                    })

        return alerts
    except Exception as e:
        logger.warning(f"detect_threshold_alerts failed for child {child_id}: {e}")
        return []
    finally:
        if own_session:
            session.close()


# ═══════════════════════════════════════════════════════════════════════════
# Écart technique (rapport lot 6)  WeeklyParentReportService (.NET) recalculait
# en parallèle le même indicateur hebdomadaire (score moyen 7j vs 7j
# précédents, nombre de quiz) que ce module partagé, avec le risque de
# divergence que cela entraîne. compute_weekly_trend() expose EXACTEMENT la
# même fenêtre (7 jours) et la même formule que l'ancien code C#
# (WeeklyParentReportService.SendWeeklyReportAsync) pour que le format de
# l'e-mail hebdomadaire et les chiffres envoyés ne changent pas  seul le lieu
# du calcul change (ici, au lieu d'une requête EF dupliquée côté .NET).
#
# Délibérément distincte de compute_child_context (fenêtre 30j, chat/alerte) :
# les deux fenêtres servent des usages différents (chat = tendance de fond,
# email hebdo = variation semaine à semaine) et unifier les fenêtres aurait
# changé les chiffres déjà envoyés aux parents chaque lundi, hors périmètre de
# cette tâche (voir rapport, "si l'effort est disproportionné par rapport au
# gain").
# ═══════════════════════════════════════════════════════════════════════════

def compute_weekly_trend(child_id: int, session=None) -> dict:
    """Score moyen 7j / 7j précédents + nombre de quiz (7j), identique à
    l'ancien calcul direct de WeeklyParentReportService."""
    own_session = session is None
    db = Database()
    session = session or db.SessionLocal()
    try:
        now = datetime.now(timezone.utc)
        cutoff_7 = now - timedelta(days=7)
        cutoff_14 = now - timedelta(days=14)

        scores = session.query(DailyScore).filter(
            DailyScore.UserId == child_id, DailyScore.CreatedAt >= cutoff_7
        ).all()
        prev_scores = session.query(DailyScore).filter(
            DailyScore.UserId == child_id,
            DailyScore.CreatedAt >= cutoff_14,
            DailyScore.CreatedAt < cutoff_7,
        ).all()
        week_avg = (sum(float(s.AverageScore) for s in scores) / len(scores)) if scores else 0.0
        prev_avg = (sum(float(s.AverageScore) for s in prev_scores) / len(prev_scores)) if prev_scores else 0.0

        quiz_count = session.query(QuizAttempt.Id).filter(
            QuizAttempt.UserId == child_id, QuizAttempt.CompletedAt >= cutoff_7
        ).count()

        return {
            "child_id": child_id,
            "week_avg": round(week_avg, 2),
            "prev_avg": round(prev_avg, 2),
            "quiz_count": quiz_count,
            "delta": round(week_avg - prev_avg, 2),
        }
    except Exception as e:
        # Deuxième vérification (revue croisée) : un retour à 0.0/0 silencieux
        # ici serait pris pour une vraie valeur par WeeklyParentReportService,
        # qui n'a alors plus aucun moyen de distinguer "élève réellement sans
        # activité" de "échec de calcul"  et ne retomberait jamais sur son
        # repli EF direct pour CET enfant precis. On relève l'exception : c'est
        # l'appelant (routes/parent_advisor_routes.py) qui omet l'enfant du
        # résultat, pour que .NET utilise son repli EF uniquement pour lui.
        logger.warning(f"compute_weekly_trend failed for child {child_id}: {e}")
        raise
    finally:
        if own_session:
            session.close()
