"""Analyse de performance (services/user_performance_analyzer.py) : indicateurs de régularité et de
vélocité, recommandations, parcours d'apprentissage, prédiction de réussite. Base, NLP et recommandeur
sont remplacés par de faux objets."""

import re
from types import SimpleNamespace

import pytest

from services.user_performance_analyzer import UserPerformanceAnalyzer

DATE_RE = re.compile(r"^\d{4}-\d{2}-\d{2}$")


class FakeNlp:
    def __init__(self):
        self.calls = []

    def analyze(self, text, metadata=None):
        self.calls.append((text, metadata))
        return {"difficulty_level": "moyen", "estimated_duration_minutes": 3, "difficulty_score": 0.5}


class FakeQuery:
    def __init__(self, rows, fail=False):
        self.rows, self.fail = rows, fail

    def filter(self, *conditions):
        return self

    def all(self):
        if self.fail:
            raise RuntimeError("table absente")
        return self.rows


class FakeSession:
    def __init__(self, rows=(), fail=False):
        self.rows, self.fail, self.closed = list(rows), fail, False

    def query(self, model):
        if self.fail:
            raise RuntimeError("requête impossible")
        return FakeQuery(self.rows)

    def close(self):
        self.closed = True


class FakeDb:
    def __init__(self, stats=None, enrollments=(), history=(), subjects=(), feedback_rows=(), feedback_fail=False,
                 fail=False):
        self.stats = stats
        self.enrollments = list(enrollments)
        self.history = list(history)
        self.subjects = list(subjects)
        self.session = FakeSession(feedback_rows, feedback_fail)
        self.fail = fail

    def get_user_progress_stats(self, user_id):
        if self.fail:
            raise RuntimeError("base indisponible")
        return self.stats

    def get_user_enrollments(self, user_id):
        return self.enrollments

    def get_user_learning_history(self, user_id, limit=100):
        return self.history

    def get_all_subjects(self, filters=None):
        return self.subjects

    def SessionLocal(self):
        return self.session


def _stats(**kw):
    base = {"total_enrolled_subjects": 2, "completed_subjects": 1, "average_progress": 50.0,
            "total_learning_time_minutes": 120, "learning_frequency": 10, "strengths": ["Maths"],
            "weak_areas": ["Physique"]}
    base.update(kw)
    return base


def _enrollment(id, progress=50.0, category="Sciences", description="Un cours", title=None):
    return {"enrollment_id": id, "subject_id": id, "subject_title": title or f"Sujet {id}",
            "subject_category": category, "subject_description": description, "progress_percentage": progress}


def _h(timestamp, duration=0):
    return {"timestamp": timestamp, "duration_seconds": duration}


def make(db=None, nlp=None, recommender=None):
    return UserPerformanceAnalyzer(db=db or FakeDb(_stats()), nlp_analyzer=nlp or FakeNlp(),
                                   recommender=recommender or SimpleNamespace())


# ── jours d'apprentissage, durée de session, vélocité ───────────────────────

def test_learning_days():
    a = make()
    assert a._calculate_learning_days([]) == 0
    assert a._calculate_learning_days([_h(None)]) == 0
    assert a._calculate_learning_days([_h("2026-01-01T10:00:00"), _h("2026-01-03T09:00:00Z")]) == 3
    assert a._calculate_learning_days([_h("2026-01-01T10:00:00")]) == 1


def test_avg_session_duration_in_minutes_ignores_zero_durations():
    a = make()
    assert a._calculate_avg_session_duration([]) == 0
    assert a._calculate_avg_session_duration([_h("x", 0)]) == 0
    assert a._calculate_avg_session_duration([_h("x", 600), _h("x", 1200), _h("x", 0)]) == pytest.approx(15.0)


def test_velocity_defaults_when_not_enough_data():
    a = make()
    assert a._calculate_learning_velocity([]) == 0.5
    assert a._calculate_learning_velocity([_h("2026-01-01T10:00:00")]) == 0.5
    assert a._calculate_learning_velocity([_h("2026-01-01T10:00:00"), _h(None)]) == 0.5


def test_velocity_is_actions_per_day_times_five_and_capped():
    a = make()
    four_over_two_days = [_h("2026-01-01T10:00:00"), _h("2026-01-01T11:00:00"),
                          _h("2026-01-02T10:00:00"), _h("2026-01-02T11:00:00")]
    assert a._calculate_learning_velocity(four_over_two_days) == pytest.approx(10.0)
    same_day = [_h("2026-01-01T10:00:00"), _h("2026-01-01T11:00:00")]
    assert a._calculate_learning_velocity(same_day) == pytest.approx(10.0)
    assert a._calculate_learning_velocity([_h("2026-01-01T10:00:00")] * 40) == 15


# ── régularité ──────────────────────────────────────────────────────────────

@pytest.mark.parametrize("days,expected", [
    (["2026-01-01", "2026-01-02", "2026-01-03", "2026-01-04"], "very_consistent"),                  # 4/4
    (["2026-01-01", "2026-01-02", "2026-01-03", "2026-01-03", "2026-01-03"], "consistent"),        # 3/5 = 0.6
    (["2026-01-01", "2026-01-01", "2026-01-02", "2026-01-02"], "moderate"),                         # 2/4 = 0.5
    (["2026-01-01"] * 10, "intermittent"),                                                          # 1/10
])
def test_learning_pattern(days, expected):
    a = make()
    assert a._analyze_learning_pattern([_h(d + "T10:00:00") for d in days]) == expected


def test_learning_pattern_without_usable_data_is_inconsistent():
    a = make()
    assert a._analyze_learning_pattern([]) == "inconsistent"
    assert a._analyze_learning_pattern([_h(None), _h("")]) == "inconsistent"


# ── dates de complétion ─────────────────────────────────────────────────────

def test_estimate_completion_date_special_cases_and_format():
    a = make()
    assert a._estimate_completion_date(0, 0, 0.0, 1.0) == "N/A"
    assert a._estimate_completion_date(3, 3, 100.0, 1.0) == "Complété"
    assert DATE_RE.match(a._estimate_completion_date(1, 3, 40.0, 2.0))


def test_estimate_completion_date_is_later_when_velocity_is_lower():
    a = make()
    slow = a._estimate_completion_date(1, 3, 10.0, 0.5)    # 180 jours
    fast = a._estimate_completion_date(1, 3, 10.0, 15.0)   # 6 jours
    assert slow > fast


def test_remaining_days():
    a = make()
    assert a._calculate_remaining_days("Complété") == 0
    assert a._calculate_remaining_days("N/A") == 0
    assert a._calculate_remaining_days("pas une date") == 0
    assert a._calculate_remaining_days("2000-01-01") == 0
    assert a._calculate_remaining_days("2999-01-01") > 100000


# ── complexité, suites, recommandations, temps d'étude ──────────────────────

def test_avg_complexity_maps_progress_ranges():
    a = make()
    assert a._calculate_avg_complexity([]) == 2
    assert a._calculate_avg_complexity([_enrollment(1, 10), _enrollment(2, 50), _enrollment(3, 90)]) == 2.0
    assert a._calculate_avg_complexity([_enrollment(1, 39.9)]) == 1
    assert a._calculate_avg_complexity([_enrollment(1, 40)]) == 2     # 40 est la borne du palier moyen
    assert a._calculate_avg_complexity([_enrollment(1, 70)]) == 3


def test_recommended_next_subjects():
    a = make()
    enrollments = [_enrollment(1, category="Maths"), _enrollment(2, category="Maths"), _enrollment(3, category="Lettres")]
    assert a._get_recommended_next_subjects(enrollments, {}) == ["Sujets avancés en Maths", "Certifications connexes"]
    assert a._get_recommended_next_subjects([], {}) == ["Explorer de nouveaux domaines", "Cas d'étude pratiques"]
    assert a._get_recommended_next_subjects([_enrollment(1, category=None)], {}) == \
        ["Explorer de nouveaux domaines", "Cas d'étude pratiques"]


def test_generate_recommendations_default_message():
    a = make()
    stats = {"total_learning_time_minutes": 120, "learning_frequency": 10}
    assert a._generate_recommendations(stats, 50, 2, []) == ["Continuez votre apprentissage régulier"]


def test_generate_recommendations_for_a_struggling_student():
    a = make()
    stats = {"total_learning_time_minutes": 30, "learning_frequency": 2}
    recs = a._generate_recommendations(stats, 10, 0.5, ["Maths", "Physique", "SVT"])
    assert any("Accélérez" in r for r in recs)
    assert "Renforcez Maths, Physique - ce sont vos points faibles" in recs     # deux premières lacunes seulement
    assert any("fréquence d'étude" in r for r in recs)
    assert any("temps d'étude quotidien" in r for r in recs)
    assert any("régulièrement" in r for r in recs)


def test_generate_recommendations_for_a_strong_student():
    a = make()
    stats = {"total_learning_time_minutes": 600, "learning_frequency": 50}
    recs = a._generate_recommendations(stats, 90, 8, [])
    assert recs == ["Excellent ! Vous approchez de la fin - continuez l'effort",
                    "Rythme excellent ! Maintenez cette dynamique"]


@pytest.mark.parametrize("velocity,expected", [
    (0.5, "60-90 minutes par jour"), (1, "45-60 minutes par jour"), (2.9, "45-60 minutes par jour"),
    (3, "30-45 minutes par jour"), (5, "20-30 minutes par jour (vous êtes très efficace)"),
])
def test_recommend_study_time(velocity, expected):
    assert make()._recommend_study_time(velocity) == expected


# ── statistiques de score ───────────────────────────────────────────────────

def _scores(*values):
    return [{"date": f"2026-01-{i + 1:02d}", "score": v, "quizCount": 1} for i, v in enumerate(values)]


def test_score_stats_cases():
    a = make()
    assert a._compute_score_stats([]) == (None, None)
    assert a._compute_score_stats(_scores(10, 15, 20)) == (15.0, None)          # moins de 4 points : pas de tendance
    assert a._compute_score_stats(_scores(10, 10, 20, 20)) == (15.0, "up")
    assert a._compute_score_stats(_scores(20, 20, 10, 10)) == (15.0, "down")
    assert a._compute_score_stats(_scores(14, 14, 15, 15)) == (14.5, "stable")
    assert a._compute_score_stats(_scores(10, 10, 12, 12)) == (11.0, "stable")  # écart de 2 exactement : stable


# ── historique de scores en base ────────────────────────────────────────────

def test_score_history_reads_daily_scores():
    import datetime as dt
    rows = [SimpleNamespace(Date=dt.date(2026, 1, 2), AverageScore=12.34, QuizCount=3)]
    db = FakeDb(_stats(), feedback_rows=rows)
    result = make(db)._get_score_history(1)
    assert result == [{"date": "2026-01-02", "score": 12.3, "quizCount": 3}]
    assert db.session.closed is True


def test_score_history_is_empty_when_the_table_is_unavailable():
    db = FakeDb(_stats(), feedback_fail=True)
    assert make(db)._get_score_history(1) == []
    assert db.session.closed is True


# ── analyze_user_progress ───────────────────────────────────────────────────

def test_analyze_user_progress_builds_the_full_report():
    history = [_h("2026-01-01T10:00:00", 600), _h("2026-01-03T10:00:00", 1200)]
    nlp = FakeNlp()
    a = make(FakeDb(_stats(), [_enrollment(1), _enrollment(2)], history), nlp)
    a._get_score_history = lambda user_id, days=90: [{"date": "2026-01-01", "score": 12.0, "quizCount": 1}]

    report = a.analyze_user_progress(42)

    assert report["success"] is True and report["user_id"] == 42
    assert report["strong_areas"] == ["Maths"] and report["weak_areas"] == ["Physique"]
    overview = report["overview"]
    assert overview["completion_rate"] == 50.0 and overview["enrolled_days"] == 3
    assert overview["total_learning_time_hours"] == 2.0
    assert overview["average_session_duration_minutes"] == 15.0
    assert overview["avg_score"] == 12.0 and overview["trend"] is None
    assert report["analysis"]["learning_velocity"] == pytest.approx(3.33)
    assert report["analysis"]["learning_pattern"] == "very_consistent"
    assert report["analysis"]["subject_difficulty_analysis"][1] == {
        "title": "Sujet 1", "difficulty": "moyen", "estimated_duration": 3, "complexity_score": 0.5}
    assert report["projections"]["recommended_daily_target"] == 25.0
    assert DATE_RE.match(report["projections"]["estimated_completion_date"])
    assert report["recommendations"] == ["Renforcez Physique - ce sont vos points faibles"]
    assert nlp.calls[0][1] == {"subject_id": 1, "category": "Sciences"}


def test_analyze_user_progress_analyses_at_most_five_subjects_with_a_description():
    nlp = FakeNlp()
    enrollments = [_enrollment(i) for i in range(1, 8)]
    enrollments[0]["subject_description"] = None
    a = make(FakeDb(_stats(), enrollments), nlp)
    a._get_score_history = lambda user_id, days=90: []
    report = a.analyze_user_progress(1)
    # les cinq premiers sujets sont examinés, celui sans description est ignoré
    assert sorted(report["analysis"]["subject_difficulty_analysis"]) == [2, 3, 4, 5]
    assert len(nlp.calls) == 4


def test_analyze_user_progress_without_enrollment():
    a = make(FakeDb(_stats(), []))
    a._get_score_history = lambda user_id, days=90: []
    assert a.analyze_user_progress(9) == {"success": False, "user_id": 9,
                                          "message": "Aucun enrollment trouvé pour cet utilisateur"}


def test_analyze_user_progress_reports_errors_instead_of_raising():
    result = make(FakeDb(fail=True)).analyze_user_progress(9)
    assert result == {"success": False, "error": "base indisponible", "user_id": 9}


# ── recommandations de sujets ───────────────────────────────────────────────

def _catalogue_subject(id, rating, **kw):
    return {"id": id, "title": f"S{id}", "category": "Sciences", "description": "d", "price": 0,
            "averageRating": rating, "enrollmentCount": 1, **kw}


def test_personalized_recommendations_exclude_enrolled_and_rejected_and_sort_by_score():
    db = FakeDb(_stats(), enrollments=[_enrollment(1)],
                subjects=[_catalogue_subject(1, 5.0), _catalogue_subject(2, 3.456), _catalogue_subject(3, 4.0),
                          _catalogue_subject(4, 4.5)],
                feedback_rows=[SimpleNamespace(SubjectId=4)])
    result = make(db).get_personalized_recommendations(7, limit=5)
    assert [r["subject_id"] for r in result] == [3, 2]
    assert result[1]["score"] == 3.46 and result[0]["difficulty_level"] == "moyen"
    assert db.session.closed is True


def test_personalized_recommendations_tolerate_a_failing_feedback_table():
    db = FakeDb(_stats(), subjects=[_catalogue_subject(1, 4.0)], feedback_fail=True)
    assert [r["subject_id"] for r in make(db).get_personalized_recommendations(7)] == [1]


def test_personalized_recommendations_only_examine_twice_the_limit():
    nlp = FakeNlp()
    db = FakeDb(_stats(), subjects=[_catalogue_subject(i, 5.0 - i) for i in range(1, 6)])
    result = make(db, nlp).get_personalized_recommendations(7, limit=1)
    assert len(nlp.calls) == 2 and [r["subject_id"] for r in result] == [1]


def test_personalized_recommendations_use_the_recommender_score_when_available():
    recommender = SimpleNamespace(get_similarity_score=lambda subject_id, category, user_categories: 10 - subject_id)
    db = FakeDb(_stats(), subjects=[_catalogue_subject(1, 5.0), _catalogue_subject(2, 1.0)])
    result = make(db, recommender=recommender).get_personalized_recommendations(7)
    assert [(r["subject_id"], r["score"]) for r in result] == [(1, 9), (2, 8)]


def test_personalized_recommendations_empty_when_nothing_is_available():
    assert make(FakeDb(_stats(), subjects=[])).get_personalized_recommendations(7) == []


# ── parcours d'apprentissage ────────────────────────────────────────────────

def test_learning_path_requires_some_activity():
    db = FakeDb(_stats(learning_frequency=0), enrollments=[])
    result = make(db).generate_learning_path(1)
    assert result["success"] is False and "Pas encore assez d'activité" in result["error"]


def test_learning_path_with_weak_and_strong_areas_has_three_phases():
    db = FakeDb(_stats(weak_areas=["Physique", "Chimie", "SVT"], strengths=["Maths"]),
                enrollments=[_enrollment(1, category="Maths")])
    path = make(db).generate_learning_path(1)      # historique vide : vélocité par défaut 0.5
    assert path["success"] is True and path["learning_velocity"] == 0.5
    phases = path["phases"]
    assert [p["phase"] for p in phases] == [1, 2, 3]
    assert [p["duration_days"] for p in phases] == [28, 42, 56]
    assert path["total_duration_days"] == 126
    assert phases[0]["focus_areas"] == ["Physique", "Chimie"] and phases[0]["difficulty"] == "facile"
    assert phases[1]["focus_areas"] == ["Maths"]
    assert phases[2]["focus_areas"] == ["Sujets avancés en Maths", "Certifications connexes"]
    assert path["recommendations"]["daily_study_time"] == "60-90 minutes par jour"
    assert path["recommendations"]["growth_areas"] == ["Maths"]
    assert DATE_RE.match(path["estimated_end_date"])


def test_learning_path_without_weak_or_strong_areas_keeps_only_mastery_phase():
    db = FakeDb(_stats(weak_areas=[], strengths=[], learning_frequency=3), enrollments=[])
    path = make(db).generate_learning_path(1)
    assert [p["phase"] for p in path["phases"]] == [3]
    assert path["phases"][0]["focus_areas"] == ["Explorer de nouveaux domaines", "Cas d'étude pratiques"]


def test_learning_path_faster_learner_gets_shorter_phases():
    history = [_h("2026-01-01T10:00:00")] * 10    # 10 actions le même jour : vélocité plafonnée à 15
    db = FakeDb(_stats(), enrollments=[_enrollment(1)], history=history)
    path = make(db).generate_learning_path(1)
    assert path["learning_velocity"] == 15
    assert [p["duration_days"] for p in path["phases"]] == [int(14 / 15), int(21 / 15), int(28 / 15)]
    assert path["recommendations"]["daily_study_time"].startswith("20-30 minutes")


def test_learning_path_reports_errors():
    assert make(FakeDb(fail=True)).generate_learning_path(1) == {"success": False, "error": "base indisponible"}


# ── prédiction de réussite ──────────────────────────────────────────────────

def _analysis(completion=50.0, velocity=1.0, pattern="moderate", hours=5.0, weak=(), strengths=()):
    return {"success": True,
            "overview": {"completion_rate": completion, "total_learning_time_hours": hours,
                         "total_enrolled_subjects": 2},
            "analysis": {"learning_velocity": velocity, "learning_pattern": pattern,
                         "weak_areas": list(weak), "strengths": list(strengths)}}


def _predict(**kw):
    a = make()
    a.analyze_user_progress = lambda user_id: _analysis(**kw)
    return a.predict_success(3)


def test_predict_success_high_probability_with_positive_factors():
    result = _predict(completion=80, velocity=15, pattern="very_consistent", hours=20, strengths=["Maths"])
    # 80 * 0.4 + 30 + 20 + 10 = 92
    assert result["success"] is True and result["probability"] == 92 and result["level"] == "high"
    labels = [f["label"] for f in result["positive_factors"]]
    assert labels == ["Taux de complétion élevé", "Rythme d'apprentissage soutenu",
                      "Régularité d'étude excellente", "Volume de travail solide", "Points forts identifiés"]
    assert result["negative_factors"] == []
    assert result["context"]["enrolled_subjects"] == 2 and result["user_id"] == 3


def test_predict_success_is_capped_at_95():
    assert _predict(completion=100, velocity=30, pattern="very_consistent", hours=100)["probability"] == 95


def test_predict_success_medium_level():
    result = _predict(completion=60, velocity=3, pattern="consistent", hours=10)
    # 24 + 6 + 15 + 5 = 50
    assert result["probability"] == 50 and result["level"] == "medium"


def test_predict_success_low_probability_lists_negative_factors_and_two_gaps():
    result = _predict(completion=10, velocity=0.5, pattern="inconsistent", hours=0, weak=["Maths", "Physique", "SVT"])
    assert result["probability"] == 8 and result["level"] == "low"
    labels = [f["label"] for f in result["negative_factors"]]
    assert labels[:4] == ["Taux de complétion insuffisant", "Progression trop lente",
                          "Irrégularité dans les révisions", "Temps d'étude insuffisant"]
    assert labels[4:] == ["Lacune détectée : Maths", "Lacune détectée : Physique"]
    assert result["positive_factors"] == []
    assert all(f["impact"] == "negative" for f in result["negative_factors"])


def test_predict_success_unknown_pattern_counts_as_medium_weight():
    # 0 + 0 + 50 * 0.2 + 0 = 10
    assert _predict(completion=0, velocity=0, pattern="autre", hours=0)["probability"] == 10


def test_predict_success_fails_when_analysis_fails():
    a = make()
    a.analyze_user_progress = lambda user_id: {"success": False}
    result = a.predict_success(1)
    assert result["success"] is False and result["error"].startswith("Analyse impossible")


def test_predict_success_reports_unexpected_errors():
    a = make()

    def boom(user_id):
        raise RuntimeError("panne")

    a.analyze_user_progress = boom
    assert a.predict_success(1) == {"success": False, "error": "panne"}
