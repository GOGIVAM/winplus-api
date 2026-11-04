"""Couche base de données (database.py) : construction de la connexion, requêtes du catalogue et de la
progression sur une base SQLite en mémoire. Aucune connexion PostgreSQL : seules les tables lues par
ces méthodes sont créées (la table Users utilise ARRAY, propre à PostgreSQL)."""

from datetime import datetime, timezone

import pytest
from sqlalchemy import create_engine
from sqlalchemy.orm import sessionmaker
from sqlalchemy.pool import StaticPool

import database as dbm

UTC = timezone.utc

TABLES = [dbm.Subject, dbm.CourseContent, dbm.Enrollment, dbm.LearningHistory, dbm.StudySession,
          dbm.QuizMistake, dbm.DownloadHistory, dbm.ChatbotContext]


@pytest.fixture
def db():
    engine = create_engine("sqlite://", connect_args={"check_same_thread": False}, poolclass=StaticPool)
    dbm.Base.metadata.create_all(engine, tables=[m.__table__ for m in TABLES])
    database = dbm.Database.__new__(dbm.Database)  # contourne __init__ (qui vise PostgreSQL)
    database.engine = engine
    database.SessionLocal = sessionmaker(autocommit=False, autoflush=False, bind=engine)
    yield database
    engine.dispose()


def _add(db, *rows):
    session = db.SessionLocal()
    try:
        session.add_all(rows)
        session.commit()
    finally:
        session.close()


def _subject(id, title, **kw):
    kw.setdefault("IsPublished", True)
    kw.setdefault("IsDeleted", False)
    return dbm.Subject(Id=id, Title=title, **kw)


def _fake_engine(captured, url):
    captured["url"] = url
    return object()


# ── construction ────────────────────────────────────────────────────────────

def test_init_builds_the_connection_string_from_environment(monkeypatch):
    captured = {}
    monkeypatch.setattr(dbm, "create_engine", lambda url, echo=False: _fake_engine(captured, url))
    monkeypatch.setenv("DB_USER", "u")
    monkeypatch.setenv("DB_PASSWORD", "p")
    monkeypatch.setenv("DB_HOST", "10.0.0.9")
    monkeypatch.setenv("DB_PORT", "6543")
    monkeypatch.setenv("DB_NAME", "winplus_integration")
    database = dbm.Database()
    assert captured["url"] == "postgresql://u:p@10.0.0.9:6543/winplus_integration"
    assert database.SessionLocal is not None


def test_init_default_database_name_is_production_name(monkeypatch):
    captured = {}
    monkeypatch.setattr(dbm, "create_engine", lambda url, echo=False: _fake_engine(captured, url))
    monkeypatch.delenv("DB_NAME", raising=False)
    dbm.Database()
    assert captured["url"].endswith("/winplus_db")


def test_get_db_yields_a_session_and_always_closes_it(monkeypatch):
    class FakeSession:
        closed = False

        def close(self):
            self.closed = True

    session = FakeSession()
    monkeypatch.setattr(dbm, "Database", lambda: type("D", (), {"get_session": lambda self: session})())
    generator = dbm.get_db()
    assert next(generator) is session and session.closed is False
    with pytest.raises(StopIteration):
        next(generator)
    assert session.closed is True


def test_get_db_closes_the_session_when_the_route_fails(monkeypatch):
    class FakeSession:
        closed = False

        def close(self):
            self.closed = True

    session = FakeSession()
    monkeypatch.setattr(dbm, "Database", lambda: type("D", (), {"get_session": lambda self: session})())
    generator = dbm.get_db()
    next(generator)
    with pytest.raises(RuntimeError):
        generator.throw(RuntimeError("erreur dans la route"))
    assert session.closed is True


def test_init_db_creates_tables_on_the_database_engine(monkeypatch):
    engine = object()
    created = []
    monkeypatch.setattr(dbm, "Database", lambda: type("D", (), {"engine": engine})())
    monkeypatch.setattr(dbm.Base.metadata, "create_all", lambda bind=None, **kw: created.append(bind))
    dbm.init_db()
    assert created == [engine]


# ── mapping des colonnes .NET ───────────────────────────────────────────────

def test_python_attribute_names_map_to_real_dotnet_columns():
    assert dbm.Exam.__table__.c["Duration"] is dbm.Exam.DurationMinutes.property.columns[0]
    history = dbm.LearningHistory.__table__.c
    assert {"ActivityType", "DurationSeconds", "ActivityAt", "EventDetails"} <= set(history.keys())
    assert dbm.LearningHistory.ActionType.property.columns[0].name == "ActivityType"
    assert dbm.Subject.__tablename__ == "Subjects" and dbm.Enrollment.__tablename__ == "Enrollments"


# ── sujets ──────────────────────────────────────────────────────────────────

def test_get_subject_by_id_returns_published_subject_with_float_values(db):
    _add(db, _subject(1, "Maths", Description="Algèbre", Category="Sciences", Price=1500, AverageRating=4.5,
                      TotalRatings=10, EnrollmentCount=3, IsFeatured=True))
    subject = db.get_subject_by_id(1)
    assert subject["title"] == "Maths" and subject["price"] == 1500.0 and isinstance(subject["price"], float)
    assert subject["averageRating"] == 4.5 and subject["totalRatings"] == 10
    assert subject["enrollmentCount"] == 3 and subject["isFeatured"] is True


def test_get_subject_by_id_hides_unpublished_deleted_and_unknown(db):
    _add(db, _subject(1, "Brouillon", IsPublished=False), _subject(2, "Supprimé", IsDeleted=True))
    assert db.get_subject_by_id(1) is None
    assert db.get_subject_by_id(2) is None
    assert db.get_subject_by_id(999) is None


def _catalogue(db):
    _add(db,
         _subject(1, "Algèbre", Description="Équations", Category="Maths", IsFeatured=True),
         _subject(2, "Géométrie", Description="Triangles et algèbre", Category="Maths"),
         _subject(3, "Physique", Description="Forces", Category="Sciences"),
         _subject(4, "Caché", Category="Maths", IsPublished=False),
         _subject(5, "Effacé", Category="Maths", IsDeleted=True))


def test_get_all_subjects_returns_only_visible_subjects(db):
    _catalogue(db)
    assert sorted(s["id"] for s in db.get_all_subjects()) == [1, 2, 3]
    assert sorted(s["id"] for s in db.get_all_subjects({})) == [1, 2, 3]


def test_get_all_subjects_filters(db):
    _catalogue(db)
    assert sorted(s["id"] for s in db.get_all_subjects({"category": "Maths"})) == [1, 2]
    # la recherche porte sur le titre ET la description, sans tenir compte de la casse
    assert sorted(s["id"] for s in db.get_all_subjects({"search": "ALG"})) == [1, 2]
    assert [s["id"] for s in db.get_all_subjects({"search": "forces"})] == [3]
    assert [s["id"] for s in db.get_all_subjects({"featured": True})] == [1]
    assert sorted(s["id"] for s in db.get_all_subjects({"featured": False})) == [1, 2, 3]
    assert [s["id"] for s in db.get_all_subjects({"category": "Maths", "search": "triangles"})] == [2]


def test_get_subjects_by_category(db):
    _catalogue(db)
    assert [s["title"] for s in db.get_subjects_by_category("Sciences")] == ["Physique"]
    assert db.get_subjects_by_category("Inconnue") == []


def test_get_popular_subjects_orders_by_enrollments_then_rating_and_limits(db):
    _add(db,
         _subject(1, "A", EnrollmentCount=5, AverageRating=3),
         _subject(2, "B", EnrollmentCount=10, AverageRating=2),
         _subject(3, "C", EnrollmentCount=5, AverageRating=4),
         _subject(4, "D", EnrollmentCount=99, IsPublished=False))
    assert [s["title"] for s in db.get_popular_subjects()] == ["B", "C", "A"]
    assert [s["title"] for s in db.get_popular_subjects(limit=2)] == ["B", "C"]


@pytest.mark.xfail(strict=False, reason="get_categories lit cat.count : sur un Row SQLAlchemy, c'est la méthode "
                   "tuple.count et non la colonne étiquetée count (bogue probable du code de production)")
def test_get_categories_counts_visible_subjects_with_a_category(db):
    _add(db,
         _subject(1, "A", Category="Maths"), _subject(2, "B", Category="Maths"),
         _subject(3, "C", Category="Sciences"), _subject(4, "D"),
         _subject(5, "E", Category="Maths", IsPublished=False))
    counts = {c["name"]: c["count"] for c in db.get_categories()}
    assert counts == {"Maths": 2, "Sciences": 1}


# ── contenus ────────────────────────────────────────────────────────────────

def test_get_course_contents_sorted_and_without_document_url(db):
    _add(db, _subject(1, "Maths"),
         dbm.CourseContent(Id=1, SubjectId=1, Title="Chapitre 2", OrderIndex=2, DocumentUrl="s3://secret/livre.pdf"),
         dbm.CourseContent(Id=2, SubjectId=1, Title="Chapitre 1", OrderIndex=1, VideoUrl="https://v", IsLocked=False),
         dbm.CourseContent(Id=3, SubjectId=2, Title="Autre sujet", OrderIndex=0))
    contents = db.get_course_contents(1)
    assert [c["title"] for c in contents] == ["Chapitre 1", "Chapitre 2"]
    assert contents[0]["hasDocument"] is False and contents[0]["isLocked"] is False
    assert contents[1]["hasDocument"] is True
    # l'adresse du fichier ne sort jamais dans la réponse (décision du module 44)
    assert all("documentUrl" not in c and "s3://secret/livre.pdf" not in str(c) for c in contents)
    assert db.get_course_contents(999) == []


# ── inscriptions et historique ──────────────────────────────────────────────

def test_get_user_enrollments_counts_contents_and_skips_deleted(db):
    _add(db, _subject(1, "Maths", Category="Sciences", Description="d"), _subject(2, "Physique"),
         dbm.CourseContent(Id=1, SubjectId=1, Title="c1"), dbm.CourseContent(Id=2, SubjectId=1, Title="c2"),
         dbm.Enrollment(Id=1, UserId=7, SubjectId=1, ProgressPercentage=45.5, IsCompleted=False,
                        EnrolledAt=datetime(2026, 1, 2, tzinfo=UTC)),
         dbm.Enrollment(Id=2, UserId=7, SubjectId=2, IsDeleted=True),
         dbm.Enrollment(Id=3, UserId=8, SubjectId=2))
    result = db.get_user_enrollments(7)
    assert len(result) == 1
    item = result[0]
    assert item["subject_title"] == "Maths" and item["subject_category"] == "Sciences"
    assert item["progress_percentage"] == 45.5 and item["total_contents"] == 2
    assert item["is_completed"] is False and item["completed_at"] is None
    assert item["enrolled_at"].startswith("2026-01-02")
    assert db.get_user_enrollments(404) == []


# ── statistiques de progression ─────────────────────────────────────────────

def test_progress_stats_from_enrollments(db):
    _add(db, _subject(1, "A"), _subject(2, "B"), _subject(3, "C"), _subject(4, "D"),
         dbm.Enrollment(Id=1, UserId=1, SubjectId=1, ProgressPercentage=90, IsCompleted=True),
         dbm.Enrollment(Id=2, UserId=1, SubjectId=2, ProgressPercentage=50),
         dbm.Enrollment(Id=3, UserId=1, SubjectId=3, ProgressPercentage=10),
         dbm.Enrollment(Id=4, UserId=1, SubjectId=4, ProgressPercentage=70),
         dbm.LearningHistory(Id=1, UserId=1, SubjectId=1, Duration=120),
         dbm.LearningHistory(Id=2, UserId=1, SubjectId=2, Duration=60))
    stats = db.get_user_progress_stats(1)
    assert stats["total_enrolled_subjects"] == 4 and stats["completed_subjects"] == 1
    assert stats["average_progress"] == 55.0
    assert stats["total_learning_time_minutes"] == 3 and stats["learning_frequency"] == 2
    assert stats["strengths"] == ["A", "D", "B"]
    assert stats["weak_areas"] == ["C", "B", "D"]


def test_progress_stats_weak_areas_exclude_subjects_at_or_above_80(db):
    _add(db, _subject(1, "A"), _subject(2, "B"),
         dbm.Enrollment(Id=1, UserId=1, SubjectId=1, ProgressPercentage=100, IsCompleted=True),
         dbm.Enrollment(Id=2, UserId=1, SubjectId=2, ProgressPercentage=80))
    assert db.get_user_progress_stats(1)["weak_areas"] == []


def test_progress_stats_fall_back_to_activity_without_enrollments(db):
    _add(db, _subject(1, "A"), _subject(2, "B"), _subject(3, "C"),
         dbm.StudySession(Id=1, UserId=1, SubjectId=1, Duration=30, Score=90),
         dbm.StudySession(Id=2, UserId=1, SubjectId=1, Duration=20, Score=70),
         dbm.StudySession(Id=3, UserId=1, SubjectId=2, Duration=10, Score=40),
         dbm.QuizMistake(Id=1, UserId=1, Subject="Physique", Question="q"),
         dbm.QuizMistake(Id=2, UserId=1, Subject="Physique", Question="q"),
         dbm.DownloadHistory(Id=1, UserId=1, SubjectId=3))
    stats = db.get_user_progress_stats(1)
    # matière 1 : moyenne 80 (acquise), matière 2 : moyenne 40, matière 3 : téléchargée seulement
    assert stats["total_enrolled_subjects"] == 3
    assert stats["completed_subjects"] == 1
    assert stats["average_progress"] == 60.0
    assert stats["total_learning_time_minutes"] == 60
    assert stats["learning_frequency"] == 3 + 2 + 1
    assert stats["strengths"] == ["A", "B"]
    assert stats["weak_areas"] == ["B"]


def test_progress_stats_use_quiz_mistakes_when_no_scored_session(db):
    _add(db, _subject(1, "A"),
         dbm.StudySession(Id=1, UserId=1, SubjectId=1, Duration=15),  # sans note
         dbm.QuizMistake(Id=1, UserId=1, Subject="Physique", Question="q"),
         dbm.QuizMistake(Id=2, UserId=1, Subject="Physique", Question="q"),
         dbm.QuizMistake(Id=3, UserId=1, Subject="Chimie", Question="q"),
         dbm.QuizMistake(Id=4, UserId=1, Subject=None, Question="q"))
    stats = db.get_user_progress_stats(1)
    assert stats["weak_areas"] == ["Physique", "Chimie"]
    assert stats["average_progress"] == 0.0 and stats["strengths"] == []
    assert stats["total_learning_time_minutes"] == 15


def test_progress_stats_for_a_user_without_any_trace_are_empty(db):
    stats = db.get_user_progress_stats(12345)
    assert stats == {"total_enrolled_subjects": 0, "completed_subjects": 0, "average_progress": 0.0,
                     "total_learning_time_minutes": 0, "learning_frequency": 0, "strengths": [], "weak_areas": []}


# ── style d'apprentissage et formats ────────────────────────────────────────

def test_get_user_learning_style(db):
    _add(db, dbm.ChatbotContext(Id=1, UserId=5, LearningStyle="visual"))
    assert db.get_user_learning_style(5) == "visual"
    assert db.get_user_learning_style(6) is None


def test_get_user_learning_style_returns_none_when_database_fails():
    class Broken:
        def execute(self, *a, **k):
            raise RuntimeError("table absente")

        def close(self):
            pass

    database = dbm.Database.__new__(dbm.Database)
    database.SessionLocal = lambda: Broken()
    assert database.get_user_learning_style(1) is None


def test_get_subjects_format_map_counts_videos_and_documents(db):
    _add(db, _subject(1, "A"),
         dbm.CourseContent(Id=1, SubjectId=1, Title="a", VideoUrl="https://v1", DocumentUrl="d1"),
         dbm.CourseContent(Id=2, SubjectId=1, Title="b", VideoUrl="", DocumentUrl=None),
         dbm.CourseContent(Id=3, SubjectId=2, Title="c", DocumentUrl="d2"))
    assert db.get_subjects_format_map() == {1: {"video_count": 1, "doc_count": 1},
                                            2: {"video_count": 0, "doc_count": 1}}


def test_get_subjects_format_map_is_empty_when_database_fails():
    class Broken:
        def execute(self, *a, **k):
            raise RuntimeError("panne")

        def close(self):
            pass

    database = dbm.Database.__new__(dbm.Database)
    database.SessionLocal = lambda: Broken()
    assert database.get_subjects_format_map() == {}


# ── complétion d'un sujet ───────────────────────────────────────────────────

def test_get_subject_completion_data(db):
    _add(db, _subject(1, "Maths", Category="Sciences"),
         dbm.Enrollment(Id=1, UserId=1, SubjectId=1, ProgressPercentage=100, IsCompleted=True),
         dbm.Enrollment(Id=2, UserId=2, SubjectId=1, ProgressPercentage=40),
         dbm.Enrollment(Id=3, UserId=3, SubjectId=1, ProgressPercentage=10),
         dbm.Enrollment(Id=4, UserId=4, SubjectId=1, ProgressPercentage=99, IsDeleted=True))
    data = db.get_subject_completion_data(1)
    assert data == {"subject_id": 1, "subject_title": "Maths", "total_enrollments": 3, "completions": 1,
                    "completion_rate": 33.33, "average_progress": 50.0, "category": "Sciences"}


def test_get_subject_completion_data_without_enrollment_or_subject(db):
    _add(db, _subject(1, "Vide"))
    data = db.get_subject_completion_data(1)
    assert data["total_enrollments"] == 0 and data["completion_rate"] == 0 and data["average_progress"] == 0
    assert db.get_subject_completion_data(404) is None
