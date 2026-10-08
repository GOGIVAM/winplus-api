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
    ExamCoachPlanNet, Enrollment, Subject,
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
                    entry = {"title": a.Title, "due_date": a.DueDate.date().isoformat() if a.DueDate else None}
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
