"""Contexte partagé parent/enfant (services/parent_child_context.py) : contrôle du lien, score et tendance,
lacunes, devoirs, bulletin, veille d'examen, alertes sur seuils, tendance hebdomadaire.

Base SQLite en mémoire (aucune connexion PostgreSQL) et horloge figée au 15 mars 2026 à 12h UTC.
SQLite rend des dates sans fuseau : un écouteur de chargement remet UTC sur Assignment.DueDate pour que
la comparaison avec l'heure courante, faite par le code de production, se comporte comme sous PostgreSQL."""

from datetime import date, datetime, timedelta, timezone

import pytest
from sqlalchemy import create_engine, event
from sqlalchemy.dialects.postgresql import ARRAY
from sqlalchemy.exc import OperationalError
from sqlalchemy.ext.compiler import compiles
from sqlalchemy.orm import sessionmaker
from sqlalchemy.orm.attributes import set_committed_value
from sqlalchemy.pool import StaticPool

import database as dbm
import services.parent_child_context as pcc

UTC = timezone.utc
NOW = datetime(2026, 3, 15, 12, 0, tzinfo=UTC)


@compiles(ARRAY, "sqlite")
def _array_as_text(type_, compiler, **kw):
    return "TEXT"


class FrozenDatetime(datetime):
    @classmethod
    def now(cls, tz=None):
        return NOW if tz is None else NOW.astimezone(tz)


TABLES = [dbm.User, dbm.Subject, dbm.ParentStudentLink, dbm.DailyScore, dbm.QuizMistake, dbm.TeacherClassStudent,
          dbm.Assignment, dbm.Submission, dbm.AcademicRecord, dbm.ExamCoachPlanNet, dbm.Enrollment, dbm.QuizAttempt]


def ago(days):
    return NOW - timedelta(days=days)


def build_session(skip=()):
    engine = create_engine("sqlite://", connect_args={"check_same_thread": False}, poolclass=StaticPool)
    dbm.Base.metadata.create_all(engine, tables=[m.__table__ for m in TABLES if m not in skip])
    return sessionmaker(bind=engine, autoflush=False)()


@pytest.fixture(autouse=True)
def frozen_clock(monkeypatch):
    monkeypatch.setattr(pcc, "datetime", FrozenDatetime)


@pytest.fixture(autouse=True)
def aware_due_dates():
    def to_utc(target, context, *args):
        if target.DueDate is not None and target.DueDate.tzinfo is None:
            set_committed_value(target, "DueDate", target.DueDate.replace(tzinfo=UTC))

    # « load » pour un objet nouvellement lu, « refresh » pour un objet déjà connu de la session
    event.listen(dbm.Assignment, "load", to_utc)
    event.listen(dbm.Assignment, "refresh", to_utc)
    yield
    event.remove(dbm.Assignment, "load", to_utc)
    event.remove(dbm.Assignment, "refresh", to_utc)


@pytest.fixture
def session():
    s = build_session()
    yield s
    s.close()


def _user(id=2, first="Marie", level="Terminale"):
    return dbm.User(Id=id, Email=f"u{id}@example.test", FirstName=first, Role="student", Level=level)


def _score(id, user_id, days_ago, average, **kw):
    return dbm.DailyScore(Id=id, UserId=user_id, Date=date(2026, 1, 1), AverageScore=average,
                          CreatedAt=ago(days_ago), UpdatedAt=ago(days_ago), **kw)


def _add(session, *rows):
    session.add_all(rows)
    session.commit()


# ── lien parent-enfant ──────────────────────────────────────────────────────

def test_assert_parent_linked_accepts_an_accepted_link(session):
    _add(session, dbm.ParentStudentLink(Id=1, ParentId=1, StudentId=2, Status="accepted"))
    pcc.assert_parent_linked(session, 1, 2)


@pytest.mark.parametrize("parent_id,child_id,status", [
    (1, 2, "pending"), (1, 2, "rejected"), (9, 2, "accepted"), (1, 9, "accepted"),
])
def test_assert_parent_linked_refuses_everything_else(session, parent_id, child_id, status):
    _add(session, dbm.ParentStudentLink(Id=1, ParentId=1, StudentId=2, Status=status))
    with pytest.raises(pcc.ParentAccessDenied):
        pcc.assert_parent_linked(session, parent_id, child_id)


@pytest.fixture
def stub_database(monkeypatch, session):
    """build_child_context ouvre toujours un Database() : on le remplace pour ne jamais viser PostgreSQL."""
    closed = []
    original_close = session.close
    session.close = lambda: closed.append(True)
    monkeypatch.setattr(pcc, "Database", lambda: type("D", (), {"SessionLocal": lambda self: session})())
    yield closed
    session.close = original_close


def test_build_child_context_checks_the_link_first(session, stub_database):
    _add(session, _user(), dbm.ParentStudentLink(Id=1, ParentId=1, StudentId=2, Status="pending"))
    with pytest.raises(pcc.ParentAccessDenied):
        pcc.build_child_context(1, 2, session=session)


def test_build_child_context_returns_the_context_for_a_linked_parent(session, stub_database):
    _add(session, _user(), dbm.ParentStudentLink(Id=1, ParentId=1, StudentId=2, Status="accepted"))
    context = pcc.build_child_context(1, 2, session=session)
    assert context["child_id"] == 2 and context["name"] == "Marie"
    assert stub_database == []   # une session fournie par l'appelant n'est jamais fermée ici


def test_build_child_context_closes_the_session_it_opened(session, stub_database):
    _add(session, _user(), dbm.ParentStudentLink(Id=1, ParentId=1, StudentId=2, Status="accepted"))
    assert pcc.build_child_context(1, 2)["child_id"] == 2
    assert stub_database == [True]


def test_build_child_context_closes_the_session_even_when_denied(session, stub_database):
    with pytest.raises(pcc.ParentAccessDenied):
        pcc.build_child_context(1, 2)
    assert stub_database == [True]


# ── compute_child_context ───────────────────────────────────────────────────

def test_unknown_child_has_no_context(session):
    assert pcc.compute_child_context(404, session=session) is None


def test_child_without_activity_gets_defaults(session):
    _add(session, _user(first=None, level=None))
    assert pcc.compute_child_context(2, session=session) == {
        "child_id": 2, "name": "Enfant 2", "level": "", "avg_score": None, "avg_score_trend": None,
        "quiz_gaps": [], "pending_homework": [], "late_homework": [], "bulletin_extract": None,
        "exam_watch": None, "subjects": []}


def test_average_score_and_trend_use_30_day_windows(session):
    _add(session, _user(),
         _score(1, 2, 5, 50), _score(2, 2, 10, 70),      # 30 derniers jours : moyenne 60, soit 12 sur 20
         _score(3, 2, 40, 80),                           # 30 jours précédents : 80, soit 16 sur 20
         _score(4, 2, 70, 10),                           # trop ancien : ignoré
         _score(5, 3, 5, 100))                           # autre élève : ignoré
    context = pcc.compute_child_context(2, session=session)
    assert context["avg_score"] == 12.0
    assert context["avg_score_trend"] == -4.0


def test_trend_needs_both_windows(session):
    _add(session, _user(), _score(1, 2, 5, 50))
    context = pcc.compute_child_context(2, session=session)
    assert context["avg_score"] == 10.0 and context["avg_score_trend"] is None


def test_quiz_gaps_are_the_five_latest_unresolved_mistakes(session):
    mistakes = [dbm.QuizMistake(Id=i, UserId=2, Subject=f"M{i}", Question="q", CreatedAt=ago(i)) for i in range(1, 7)]
    mistakes[2].Subject = None   # sans matière : « Général »
    _add(session, _user(), *mistakes,
         dbm.QuizMistake(Id=10, UserId=2, Subject="Résolue", Question="q", CreatedAt=ago(0.5), IsResolved=True),
         dbm.QuizMistake(Id=11, UserId=3, Subject="Autre élève", Question="q", CreatedAt=ago(0.2)))
    assert pcc.compute_child_context(2, session=session)["quiz_gaps"] == ["M1", "M2", "Général", "M4", "M5"]


def test_homework_is_split_into_late_and_pending_and_skips_submitted(session):
    _add(session, _user(),
         dbm.TeacherClassStudent(Id=1, TeacherClassId=10, StudentId=2),
         dbm.TeacherClassStudent(Id=2, TeacherClassId=11, StudentId=3),
         dbm.Assignment(Id=1, TeacherId=9, TeacherClassId=10, Title="Devoir A", DueDate=ago(5)),
         dbm.Assignment(Id=2, TeacherId=9, TeacherClassId=10, Title="Rendu", DueDate=ago(2)),
         dbm.Assignment(Id=3, TeacherId=9, TeacherClassId=10, Title="Devoir C", DueDate=NOW + timedelta(days=3)),
         dbm.Assignment(Id=4, TeacherId=9, TeacherClassId=11, Title="Autre classe", DueDate=ago(5)),
         dbm.Submission(Id=1, AssignmentId=2, StudentId=2))
    context = pcc.compute_child_context(2, session=session)
    assert context["late_homework"] == [{"id": 1, "title": "Devoir A", "due_date": "2026-03-10"}]
    assert context["pending_homework"] == [{"id": 3, "title": "Devoir C", "due_date": "2026-03-18"}]


def test_homework_without_due_date_counts_as_pending(session):
    _add(session, _user(), dbm.TeacherClassStudent(Id=1, TeacherClassId=10, StudentId=2),
         dbm.Assignment(Id=1, TeacherId=9, TeacherClassId=10, Title="Libre", DueDate=None))
    context = pcc.compute_child_context(2, session=session)
    assert context["pending_homework"] == [{"id": 1, "title": "Libre", "due_date": None}]
    assert context["late_homework"] == []


def test_latest_bulletin_is_returned(session):
    _add(session, _user(),
         dbm.AcademicRecord(Id=1, StudentId=2, SchoolYear="2024-2025", AverageGrade=11.5),
         dbm.AcademicRecord(Id=2, StudentId=2, SchoolYear="2025-2026", AverageGrade=13.25),
         dbm.AcademicRecord(Id=3, StudentId=3, SchoolYear="2026-2027", AverageGrade=19))
    assert pcc.compute_child_context(2, session=session)["bulletin_extract"] == {
        "school_year": "2025-2026", "average_grade": 13.25}


def test_exam_watch_only_for_active_plans_with_parent_mode_on(session):
    _add(session, _user(),
         dbm.ExamCoachPlanNet(Id=1, UserId=2, ExamType="CEP", ExamDate=ago(-9), IsActive=False,
                              ParentWatchModeActivatedAt=ago(3)),
         dbm.ExamCoachPlanNet(Id=2, UserId=2, ExamType="BAC", ExamDate=ago(-30), IsActive=True,
                              ParentWatchModeActivatedAt=None))
    assert pcc.compute_child_context(2, session=session)["exam_watch"] is None
    _add(session, dbm.ExamCoachPlanNet(Id=3, UserId=2, ExamType="BEPC", ExamDate=ago(-4), IsActive=True,
                                       ParentWatchModeActivatedAt=ago(3)))
    assert pcc.compute_child_context(2, session=session)["exam_watch"] == {
        "exam_type": "BEPC", "exam_date": "2026-03-19"}


def test_subjects_are_the_active_enrollments_limited_to_six(session):
    subjects = [dbm.Subject(Id=i, Title=f"Matière {i}") for i in range(1, 9)]
    enrollments = [dbm.Enrollment(Id=i, UserId=2, SubjectId=i) for i in range(1, 8)]
    enrollments.append(dbm.Enrollment(Id=20, UserId=2, SubjectId=8, IsDeleted=True))
    enrollments.append(dbm.Enrollment(Id=21, UserId=3, SubjectId=8))
    _add(session, _user(), *subjects, *enrollments)
    result = pcc.compute_child_context(2, session=session)["subjects"]
    assert len(result) == 6 and set(result) <= {f"Matière {i}" for i in range(1, 8)}


def test_a_failing_section_does_not_break_the_others():
    # la table des erreurs de quiz est absente : seule cette section retombe sur sa valeur par défaut
    s = build_session(skip=(dbm.QuizMistake,))
    _add(s, _user(), _score(1, 2, 5, 50))
    context = pcc.compute_child_context(2, session=s)
    assert context["quiz_gaps"] == [] and context["name"] == "Marie" and context["avg_score"] == 10.0
    s.close()


# ── alertes sur seuils ──────────────────────────────────────────────────────

def _alert_types(session):
    return {a["type"]: a for a in pcc.detect_threshold_alerts(2, session=session)}


def test_no_alert_for_an_unknown_child(session):
    assert pcc.detect_threshold_alerts(404, session=session) == []


def test_no_alert_for_a_calm_child(session):
    _add(session, _user(), _score(1, 2, 5, 60), _score(2, 2, 40, 60))
    assert pcc.detect_threshold_alerts(2, session=session) == []


def test_score_drop_of_four_points_is_high(session):
    _add(session, _user(), _score(1, 2, 5, 60), _score(2, 2, 40, 80))   # 12 contre 16
    alert = _alert_types(session)["BaisseScoreSeuil"]
    assert alert["severity"] == "High"
    assert alert["dedup_key"] == "scoredrop:2026-03"
    assert alert["detected_at"] == NOW.isoformat()
    assert "Marie a baissé de 4.0 points sur 20" in alert["content"]


def test_score_drop_of_exactly_two_points_is_medium_and_smaller_is_ignored(session):
    _add(session, _user(), _score(1, 2, 5, 60), _score(2, 2, 40, 70))   # 12 contre 14
    assert _alert_types(session)["BaisseScoreSeuil"]["severity"] == "Medium"
    session.query(dbm.DailyScore).filter(dbm.DailyScore.Id == 2).update({"AverageScore": 69})   # 12 contre 13,8
    session.commit()
    assert "BaisseScoreSeuil" not in _alert_types(session)


def test_late_homework_alert_needs_three_days_and_turns_high_at_seven(session):
    _add(session, _user(), dbm.TeacherClassStudent(Id=1, TeacherClassId=10, StudentId=2),
         dbm.Assignment(Id=1, TeacherId=9, TeacherClassId=10, Title="Court", DueDate=ago(2)),       # 2 jours : silence
         dbm.Assignment(Id=2, TeacherId=9, TeacherClassId=10, Title="Moyen", DueDate=ago(5)),       # 5 jours
         dbm.Assignment(Id=3, TeacherId=9, TeacherClassId=10, Title="Long", DueDate=ago(14)))       # 14 jours
    alerts = {a["dedup_key"]: a for a in pcc.detect_threshold_alerts(2, session=session)}
    assert set(alerts) == {"homework:2", "homework:3"}
    assert alerts["homework:2"]["severity"] == "Medium" and alerts["homework:3"]["severity"] == "High"
    assert alerts["homework:2"]["type"] == "DevoirRetardProlonge"
    assert alerts["homework:2"]["content"] == "Le devoir « Moyen » de Marie est en retard depuis 5 jour(s)."


def _watch(session, days_ahead):
    _add(session, dbm.ExamCoachPlanNet(Id=1, UserId=2, ExamType="BEPC", ExamDate=ago(-days_ahead), IsActive=True,
                                       ParentWatchModeActivatedAt=ago(1)))


def test_exam_watch_alert_when_exam_is_close_and_nothing_was_revised(session):
    _add(session, _user())
    _watch(session, 4)   # le 19 mars à minuit : dans 3 jours entiers
    alert = _alert_types(session)["ExamenImminentSansRevision"]
    assert alert["severity"] == "Medium" and alert["dedup_key"] == "examwatch:2026-03-19"
    assert "(BEPC)" in alert["content"] and "dans 3 jour(s)" in alert["content"]


def test_exam_watch_alert_is_high_within_two_days(session):
    _add(session, _user())
    _watch(session, 3)   # le 18 mars à minuit : dans 2 jours entiers
    assert _alert_types(session)["ExamenImminentSansRevision"]["severity"] == "High"


@pytest.mark.parametrize("days_ahead", [10, -1])
def test_exam_watch_ignores_far_or_past_exams(session, days_ahead):
    _add(session, _user())
    _watch(session, days_ahead)
    assert "ExamenImminentSansRevision" not in _alert_types(session)


def test_recent_daily_score_silences_the_exam_alert(session):
    _add(session, _user(), _score(1, 2, 1, 60))
    _watch(session, 4)
    assert "ExamenImminentSansRevision" not in _alert_types(session)


def test_recent_quiz_attempt_silences_the_exam_alert(session):
    _add(session, _user(), dbm.QuizAttempt(Id=1, UserId=2, CompletedAt=ago(1)))
    _watch(session, 4)
    assert "ExamenImminentSansRevision" not in _alert_types(session)


def test_old_activity_does_not_silence_the_exam_alert(session):
    _add(session, _user(), _score(1, 2, 10, 60), dbm.QuizAttempt(Id=1, UserId=2, CompletedAt=ago(10)))
    _watch(session, 4)
    assert "ExamenImminentSansRevision" in _alert_types(session)


def test_has_recent_activity_is_false_when_tables_are_missing():
    s = build_session(skip=(dbm.DailyScore,))
    assert pcc._has_recent_activity(s, 2, 3, NOW) is False
    s.close()


# ── tendance hebdomadaire ───────────────────────────────────────────────────

def test_weekly_trend_compares_the_last_two_weeks(session):
    _add(session, _score(1, 2, 2, 60), _score(2, 2, 5, 80),         # cette semaine : 70
         _score(3, 2, 10, 50),                                      # semaine précédente : 50
         _score(4, 2, 20, 99),                                      # trop ancien
         dbm.QuizAttempt(Id=1, UserId=2, CompletedAt=ago(1)), dbm.QuizAttempt(Id=2, UserId=2, CompletedAt=ago(6)),
         dbm.QuizAttempt(Id=3, UserId=2, CompletedAt=ago(8)), dbm.QuizAttempt(Id=4, UserId=3, CompletedAt=ago(1)))
    assert pcc.compute_weekly_trend(2, session=session) == {
        "child_id": 2, "week_avg": 70.0, "prev_avg": 50.0, "quiz_count": 2, "delta": 20.0}


def test_weekly_trend_without_data_is_zero(session):
    assert pcc.compute_weekly_trend(2, session=session) == {
        "child_id": 2, "week_avg": 0.0, "prev_avg": 0.0, "quiz_count": 0, "delta": 0.0}


def test_weekly_trend_raises_instead_of_returning_fake_zeros():
    s = build_session(skip=(dbm.DailyScore,))
    with pytest.raises(OperationalError):
        pcc.compute_weekly_trend(2, session=s)
    s.close()


def test_thresholds_are_the_documented_ones():
    assert (pcc.SCORE_DROP_THRESHOLD, pcc.SCORE_DROP_HIGH_THRESHOLD) == (2.0, 4.0)
    assert (pcc.LATE_HOMEWORK_MIN_DAYS, pcc.EXAM_WATCH_MAX_DAYS, pcc.RECENT_ACTIVITY_DAYS) == (3, 5, 3)
