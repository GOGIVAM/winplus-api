"""Application FastAPI (app.py) : montage des routeurs, CORS, intergiciels (compteur de tokens, langue), limite
de débit, cycle de vie, santé, catalogue public et routes IA qui vivent directement dans app.py.

L'import de `app` ne touche ni la base (le moteur SQLAlchemy est paresseux, la création des tables d'administration
est neutralisée) ni le réseau. Base, analyseurs et fournisseur d'IA sont simulés pour chaque test."""

import asyncio
import importlib
import json
from datetime import datetime
from types import SimpleNamespace

import pytest
from fastapi.testclient import TestClient

ALLOWED_ORIGINS = ["https://winplus.cm", "https://www.winplus.cm", "https://integration.winplus.cm",
                   "http://localhost:5173", "http://localhost:3000"]


@pytest.fixture(scope="module")
def app_module():
    import routes.admin_routes as admin

    patch = pytest.MonkeyPatch()
    patch.setattr(admin, "_init_admin_tables", lambda: None)       # sinon : tentative de connexion PostgreSQL
    try:
        yield importlib.import_module("app")
    finally:
        patch.undo()


@pytest.fixture(autouse=True)
def fresh_limiter(app_module):
    app_module.limiter.reset()
    yield
    app_module.limiter.reset()


@pytest.fixture
def client(app_module):
    return TestClient(app_module.app, raise_server_exceptions=False)


# ── montage ─────────────────────────────────────────────────────────────────

def _routes(app_module):
    return {(m, r.path) for r in app_module.app.routes if hasattr(r, "methods") for m in r.methods if m != "HEAD"}


@pytest.mark.parametrize("method,path", [
    ("GET", "/health"), ("GET", "/api/subjects"), ("GET", "/api/subjects/{subject_id}"), ("GET", "/api/categories"),
    ("GET", "/api/popular"), ("GET", "/api/recommendations/{subject_id}"), ("POST", "/api/analyze"),
    ("POST", "/api/recommend"), ("POST", "/api/analyze-progress"), ("GET", "/api/learning-path/{user_id}"),
    ("POST", "/api/adaptive-quiz"), ("POST", "/api/learning-style"), ("GET", "/api/success-prediction/{user_id}"),
    ("POST", "/api/admin/init-db"),
    # routeurs montés avec leur préfixe
    ("POST", "/api/chatbot/chat"), ("POST", "/api/chatbot/stream"), ("POST", "/api/chatbot/complete"),
    ("GET", "/api/chatbot/health"), ("POST", "/api/exam-coach/generate"), ("POST", "/api/study-session/generate"),
    ("POST", "/api/exam-quiz/generate"), ("POST", "/api/admin/anomaly-detection"),
    ("POST", "/api/institution/class-prediction"), ("POST", "/api/ai/generate-notification"),
    ("POST", "/api/revisions/generate-content"), ("POST", "/api/quizzes/generate-content"),
])
def test_expected_routes_are_mounted(app_module, method, path):
    assert (method, path) in _routes(app_module)


def test_the_rag_router_is_mounted_under_api(app_module):
    assert any(path.startswith("/api/rag/") for _, path in _routes(app_module))


def test_no_route_is_registered_twice(app_module):
    seen = [(m, r.path) for r in app_module.app.routes if hasattr(r, "methods") for m in r.methods if m != "HEAD"]
    duplicates = sorted({x for x in seen if seen.count(x) > 1})
    assert duplicates == [], f"routes déclarées en double : {duplicates}"


def test_app_metadata(app_module):
    assert app_module.app.title == "WinPlus IA Service" and app_module.app.version == "2.0.0"


# ── cycle de vie ────────────────────────────────────────────────────────────

def test_startup_checks_the_environment_isolation_then_builds_database_and_analyzer(app_module, monkeypatch):
    import services.winai_environment as env
    events = []
    monkeypatch.setattr(env, "assert_isolation", lambda: events.append("isolation"))
    monkeypatch.setattr(app_module, "Database", lambda: events.append("database"))
    monkeypatch.setattr(app_module, "NLPAnalyzer", lambda: events.append("nlp"))
    with TestClient(app_module.app) as started:
        assert started.get("/health").status_code == 200
    assert events == ["isolation", "database", "nlp"]


def test_startup_is_refused_when_the_environment_targets_production(app_module, monkeypatch):
    import services.winai_environment as env

    def refuse():
        raise RuntimeError("l'intégration vise la base de production")

    monkeypatch.setattr(env, "assert_isolation", refuse)
    with pytest.raises(RuntimeError, match="production"):
        with TestClient(app_module.app):
            pass


# ── santé ───────────────────────────────────────────────────────────────────

def test_health_is_public_and_describes_the_service(client):
    r = client.get("/health")
    assert r.status_code == 200
    assert r.json() == {"status": "healthy", "service": "IA Educational Service", "version": "2.0.0",
                        "database": "PostgreSQL (ASP.NET Subjects schema)"}


def test_health_is_limited_to_thirty_requests_per_minute(client):
    assert all(client.get("/health").status_code == 200 for _ in range(30))
    r = client.get("/health")
    assert r.status_code == 429
    assert r.json() == {"success": False, "error": "rate_limit_exceeded",
                        "message": "Too many requests. Please try again later."}


def test_rate_limit_handler_answers_429_json(app_module):
    response = asyncio.run(app_module.rate_limit_exceeded_handler(None, None))
    assert response.status_code == 429
    assert json.loads(response.body)["error"] == "rate_limit_exceeded"


# ── CORS ────────────────────────────────────────────────────────────────────

@pytest.mark.parametrize("origin", ALLOWED_ORIGINS)
def test_cors_preflight_is_accepted_for_every_known_origin(client, origin):
    r = client.options("/api/chatbot/chat", headers={"Origin": origin, "Access-Control-Request-Method": "POST",
                                                     "Access-Control-Request-Headers": "authorization,content-type"})
    assert r.status_code == 200
    assert r.headers["access-control-allow-origin"] == origin
    assert r.headers["access-control-allow-credentials"] == "true"
    assert "POST" in r.headers["access-control-allow-methods"]


@pytest.mark.parametrize("origin", ["https://evil.example", "https://winplus.cm.evil.example", "http://winplus.cm",
                                    "http://localhost:8080"])
def test_cors_preflight_is_refused_for_unknown_origins(client, origin):
    r = client.options("/api/chatbot/chat", headers={"Origin": origin, "Access-Control-Request-Method": "POST"})
    assert r.status_code == 400 and "access-control-allow-origin" not in r.headers


def test_cors_headers_accompany_a_normal_response_for_a_known_origin_only(client):
    ok = client.get("/health", headers={"Origin": "https://winplus.cm"})
    other = client.get("/health", headers={"Origin": "https://evil.example"})
    assert ok.headers["access-control-allow-origin"] == "https://winplus.cm"
    assert "access-control-allow-origin" not in other.headers and other.status_code == 200


# ── intergiciels ────────────────────────────────────────────────────────────

def test_every_response_carries_the_token_counter_header(client, app_module):
    from services.token_accounting import WINAI_TOKENS_HEADER
    assert client.get("/health").headers[WINAI_TOKENS_HEADER] == "0"
    assert client.get("/api/popular/inexistant").headers[WINAI_TOKENS_HEADER] == "0"      # même sur un 404


def test_the_counter_header_reports_the_tokens_recorded_during_the_request(client, app_module, monkeypatch):
    from services.token_accounting import WINAI_TOKENS_HEADER, begin_request_usage as real_begin

    def begin():
        box, token = real_begin()
        box[0] = 1234
        return box, token

    monkeypatch.setattr(app_module, "begin_request_usage", begin)
    assert client.get("/health").headers[WINAI_TOKENS_HEADER] == "1234"


def test_the_user_locale_header_is_handed_to_the_language_context_and_released(client, app_module, monkeypatch):
    from services.language import USER_LOCALE_HEADER, begin_request_language as real_begin, end_request_language as real_end
    log = []

    def begin(value):
        log.append(("begin", value))
        return real_begin(value)

    def end(token):
        log.append(("end", None))
        return real_end(token)

    monkeypatch.setattr(app_module, "begin_request_language", begin)
    monkeypatch.setattr(app_module, "end_request_language", end)
    client.get("/health", headers={USER_LOCALE_HEADER: "en"})
    client.get("/health")
    assert log == [("begin", "en"), ("end", None), ("begin", None), ("end", None)]


def test_language_context_is_released_even_when_the_route_fails(client, app_module, monkeypatch):
    log = []
    monkeypatch.setattr(app_module, "end_request_language", lambda token: log.append("end"))
    monkeypatch.setattr(app_module, "db", SimpleNamespace(get_all_subjects=lambda f: (_ for _ in ()).throw(RuntimeError("HS"))))
    assert client.get("/api/subjects").status_code == 500
    assert log == ["end"]


# ── catalogue public ────────────────────────────────────────────────────────

class FakeCatalog:
    def __init__(self, error=None):
        self.error, self.filters = error, []
        self.subjects = [{"id": i, "title": f"S{i}"} for i in range(1, 8)]

    def _check(self):
        if self.error:
            raise self.error

    def get_all_subjects(self, filters):
        self._check()
        self.filters.append(filters)
        return list(self.subjects)

    def get_subject_by_id(self, subject_id):
        self._check()
        return {"id": subject_id, "title": "Maths"} if subject_id == 1 else None

    def get_course_contents(self, subject_id):
        return [{"id": 10}, {"id": 11}]

    def get_categories(self):
        self._check()
        return [{"name": "Maths", "count": 3}]

    def get_popular_subjects(self, limit):
        self._check()
        return self.subjects[:limit]


@pytest.fixture
def catalog(app_module, monkeypatch):
    fake = FakeCatalog()
    monkeypatch.setattr(app_module, "db", fake)
    return fake


def test_subjects_are_public_and_paginated_after_filtering(client, catalog):
    r = client.get("/api/subjects", params={"skip": 2, "limit": 3})
    assert r.status_code == 200
    assert r.json() == {"success": True, "count": 3, "data": catalog.subjects[2:5]}


def test_subjects_filters_are_forwarded_including_a_false_featured_flag(client, catalog):
    client.get("/api/subjects", params={"category": "Maths", "search": "bac", "featured": "false"})
    client.get("/api/subjects")
    assert catalog.filters == [{"category": "Maths", "search": "bac", "featured": False}, {}]


@pytest.mark.parametrize("params", [{"limit": 0}, {"limit": 101}, {"skip": -1}, {"limit": "x"}, {"featured": "peut-etre"}])
def test_subjects_reject_invalid_pagination(client, catalog, params):
    assert client.get("/api/subjects", params=params).status_code == 422


def test_subjects_beyond_the_end_give_an_empty_page(client, catalog):
    assert client.get("/api/subjects", params={"skip": 50}).json() == {"success": True, "count": 0, "data": []}


def test_subject_detail_includes_its_contents(client, catalog):
    body = client.get("/api/subjects/1").json()
    assert body["success"] is True and body["data"]["contents"] == [{"id": 10}, {"id": 11}]


def test_unknown_subject_is_404_and_a_non_numeric_id_is_422(client, catalog):
    r = client.get("/api/subjects/99")
    assert r.status_code == 404 and r.json()["detail"] == "Subject not found"
    assert client.get("/api/subjects/abc").status_code == 422


def test_categories_and_popular(client, catalog):
    assert client.get("/api/categories").json() == {"success": True, "data": [{"name": "Maths", "count": 3}]}
    assert len(client.get("/api/popular", params={"limit": 4}).json()["data"]) == 4
    assert len(client.get("/api/popular").json()["data"]) == 7             # défaut 10, mais 7 existent
    assert client.get("/api/popular", params={"limit": 51}).status_code == 422


@pytest.mark.parametrize("path", ["/api/subjects", "/api/subjects/1", "/api/categories", "/api/popular"])
def test_catalog_database_failure_is_a_500_carrying_the_message(client, app_module, monkeypatch, path):
    monkeypatch.setattr(app_module, "db", FakeCatalog(error=RuntimeError("connexion perdue")))
    r = client.get(path)
    assert r.status_code == 500 and "connexion perdue" in r.json()["detail"]


# ── routes protégées ────────────────────────────────────────────────────────

PROTECTED = [("get", "/api/recommendations/1"), ("post", "/api/analyze"), ("post", "/api/recommend?user_id=1"),
             ("post", "/api/analyze-progress?user_id=1"), ("get", "/api/learning-path/1"), ("post", "/api/adaptive-quiz"),
             ("post", "/api/learning-style"), ("get", "/api/success-prediction/1"), ("post", "/api/admin/init-db")]


@pytest.mark.parametrize("method,path", PROTECTED)
def test_protected_routes_refuse_anonymous_calls(client, method, path):
    assert getattr(client, method)(path).status_code in (401, 403)


def test_init_db_is_admin_only_and_reports_failures(client, app_module, monkeypatch, headers_for):
    calls = []
    monkeypatch.setattr(app_module, "init_db", lambda: calls.append("init"))
    assert client.post("/api/admin/init-db", headers=headers_for(1, "teacher")).status_code == 403
    assert calls == []
    r = client.post("/api/admin/init-db", headers=headers_for(1, "admin"))
    assert r.json() == {"success": True, "message": "Database initialized successfully"} and calls == ["init"]

    def boom():
        raise RuntimeError("schéma verrouillé")

    monkeypatch.setattr(app_module, "init_db", boom)
    r = client.post("/api/admin/init-db", headers=headers_for(1, "admin"))
    assert r.status_code == 500 and "schéma verrouillé" in r.json()["detail"]


# ── recommandations ─────────────────────────────────────────────────────────

def test_similar_subject_recommendations_come_from_published_subjects_only(client, app_module, monkeypatch,
                                                                         headers_for, sqlite_database):
    import database as dbm
    db = sqlite_database(app_module, [dbm.Subject])
    db.add(dbm.Subject(Id=2, Title="Physique", Category="Sciences", Price=500, AverageRating=4.5, EnrollmentCount=9,
                       IsPublished=True),
           dbm.Subject(Id=3, Title="Brouillon", IsPublished=False),
           dbm.Subject(Id=4, Title="Supprimé", IsPublished=True, IsDeleted=True))
    asked = []
    monkeypatch.setattr(app_module, "db", db)
    monkeypatch.setattr(app_module, "recommender", SimpleNamespace(
        get_similar_subjects=lambda sid, limit: asked.append((sid, limit)) or [2, 3, 4]))
    body = client.get("/api/recommendations/1", params={"limit": 7}, headers=headers_for(5)).json()
    assert asked == [(1, 7)] and body["count"] == 1 and body["data_source"] == "content_based"
    assert body["recommendations"][0]["title"] == "Physique" and body["recommendations"][0]["price"] == 500.0


def test_no_similar_subject_gives_an_empty_list(client, app_module, monkeypatch, headers_for):
    monkeypatch.setattr(app_module, "recommender", SimpleNamespace(get_similar_subjects=lambda sid, limit: []))
    body = client.get("/api/recommendations/1", headers=headers_for(5)).json()
    assert body == {"success": True, "count": 0, "recommendations": [], "data_source": "content_based"}


def test_recommendation_limit_is_validated_and_failures_are_500(client, app_module, monkeypatch, headers_for):
    assert client.get("/api/recommendations/1", params={"limit": 0}, headers=headers_for(5)).status_code == 422
    assert client.get("/api/recommendations/1", params={"limit": 21}, headers=headers_for(5)).status_code == 422

    def boom(sid, limit):
        raise RuntimeError("modèle HS")

    monkeypatch.setattr(app_module, "recommender", SimpleNamespace(get_similar_subjects=boom))
    assert client.get("/api/recommendations/1", headers=headers_for(5)).status_code == 500


def test_personalised_recommendations(client, app_module, monkeypatch, headers_for):
    asked = []
    analyzer = SimpleNamespace(get_personalized_recommendations=lambda uid, limit: asked.append((uid, limit)) or [{"id": 1}])
    monkeypatch.setattr(app_module, "performance_analyzer", analyzer)
    r = client.post("/api/recommend", params={"user_id": 9, "limit": 3}, headers=headers_for(9))
    assert r.json() == {"success": True, "recommendations": [{"id": 1}], "count": 1, "data_source": "real_user_history"}
    assert asked == [(9, 3)]
    assert client.post("/api/recommend", headers=headers_for(9)).status_code == 422          # user_id requis
    assert client.post("/api/recommend", params={"user_id": 9, "limit": 21}, headers=headers_for(9)).status_code == 422


def test_personalised_recommendations_empty_and_failing(client, app_module, monkeypatch, headers_for):
    monkeypatch.setattr(app_module, "performance_analyzer", SimpleNamespace(get_personalized_recommendations=lambda u, l: []))
    body = client.post("/api/recommend", params={"user_id": 9}, headers=headers_for(9)).json()
    assert body["recommendations"] == [] and body["count"] == 0 and "Pas de recommandation" in body["message"]

    def boom(u, l):
        raise RuntimeError("analyse HS")

    monkeypatch.setattr(app_module, "performance_analyzer", SimpleNamespace(get_personalized_recommendations=boom))
    assert client.post("/api/recommend", params={"user_id": 9}, headers=headers_for(9)).status_code == 500


# ── analyses déléguées à l'analyseur ────────────────────────────────────────

def test_progress_analysis_reshapes_the_analyzer_result(client, app_module, monkeypatch, headers_for):
    overview = {"total_enrolled_subjects": 3, "completed_subjects": 1, "completion_rate": 33.3, "enrolled_days": 40,
                "total_learning_time_hours": 12.5, "average_session_duration_minutes": 25.0}
    result = {"success": True, "overview": overview, "analysis": {"weak_areas": ["Maths"]},
              "recommendations": ["Réviser"], "projections": {"estimated_completion_date": "2026-09-01"},
              "history": [{"d": 1}], "strong_areas": ["SVT"], "weak_areas": ["Maths"]}
    monkeypatch.setattr(app_module, "performance_analyzer", SimpleNamespace(analyze_user_progress=lambda uid: result))
    body = client.post("/api/analyze-progress", params={"user_id": 9}, headers=headers_for(9)).json()
    assert body["user_id"] == 9 and body["estimated_completion_date"] == "2026-09-01"
    assert body["strong_areas"] == ["SVT"] and body["weak_areas"] == ["Maths"] and body["recommendations"] == ["Réviser"]
    assert body["overview"]["total_enrolled_subjects"] == 3


def test_progress_analysis_without_data_is_404_with_the_analyzer_message(client, app_module, monkeypatch, headers_for):
    monkeypatch.setattr(app_module, "performance_analyzer",
                        SimpleNamespace(analyze_user_progress=lambda uid: {"success": False, "message": "Aucune inscription"}))
    r = client.post("/api/analyze-progress", params={"user_id": 9}, headers=headers_for(9))
    assert r.status_code == 404 and r.json()["detail"] == "Aucune inscription"


@pytest.mark.parametrize("path,method_name", [("/api/learning-path/9", "generate_learning_path"),
                                              ("/api/success-prediction/9", "predict_success")])
def test_learning_path_and_prediction_map_failures_to_404_and_exceptions_to_500(client, app_module, monkeypatch,
                                                                             headers_for, path, method_name):
    monkeypatch.setattr(app_module, "performance_analyzer",
                        SimpleNamespace(**{method_name: lambda uid: {"success": False, "error": "Données insuffisantes ici"}}))
    r = client.get(path, headers=headers_for(9))
    assert r.status_code == 404 and r.json()["detail"] == "Données insuffisantes ici"

    def boom(uid):
        raise RuntimeError("calcul impossible")

    monkeypatch.setattr(app_module, "performance_analyzer", SimpleNamespace(**{method_name: boom}))
    r = client.get(path, headers=headers_for(9))
    assert r.status_code == 500 and "calcul impossible" in r.json()["detail"]


def test_nlp_analysis_delegates_and_reports_errors(client, app_module, monkeypatch, headers_for):
    seen = []
    result = {"text": "Bonjour", "difficulty_level": "facile", "complexity_score": 0.2,
              "estimated_duration_minutes": 5, "key_concepts": ["salut"], "vocabulary_complexity": {}}
    monkeypatch.setattr(app_module, "nlp_analyzer", SimpleNamespace(analyze=lambda text, meta: seen.append((text, meta)) or result))
    r = client.post("/api/analyze", json={"text": "Bonjour", "metadata": {"k": 1}}, headers=headers_for(9))
    assert r.status_code == 200 and r.json()["difficulty_level"] == "facile" and seen[0][0] == "Bonjour"
    assert client.post("/api/analyze", json={}, headers=headers_for(9)).status_code == 422

    def boom(text, meta):
        raise RuntimeError("modèle indisponible")

    monkeypatch.setattr(app_module, "nlp_analyzer", SimpleNamespace(analyze=boom))
    assert client.post("/api/analyze", json={"text": "Bonjour"}, headers=headers_for(9)).status_code == 500


# ── style d'apprentissage ───────────────────────────────────────────────────

def _answers(*tags):
    return {"answers": [{"question_id": f"q{i}", "style_tag": t} for i, t in enumerate(tags)]}


@pytest.mark.parametrize("tags,style,label", [
    (["V", "V", "A"], "visual", "Apprenant Visuel"),
    (["a", "A", "R"], "auditory", "Apprenant Auditif"),            # insensible à la casse
    (["R", "R", "K", "V"], "reading_writing", "Apprenant Lecteur/Scripteur"),
    (["K", "K", "K", "V"], "kinesthetic", "Apprenant Kinesthésique"),
])
def test_learning_style_is_the_most_frequent_tag(client, headers_for, tags, style, label):
    body = client.post("/api/learning-style", json=_answers(*tags), headers=headers_for(9)).json()
    assert body["style"] == style and body["label"] == label and len(body["winplus_tips"]) == 4
    assert sum(body["score_breakdown"].values()) == len(tags)


@pytest.mark.parametrize("tags", [["V", "A"], ["V", "A", "R", "K"], [], ["X", "Y"]])
def test_learning_style_is_mixed_on_a_tie_or_without_valid_answers(client, headers_for, tags):
    body = client.post("/api/learning-style", json=_answers(*tags), headers=headers_for(9)).json()
    assert body["style"] == "mixed" and body["label"] == "Apprenant Polyvalent"


def test_learning_style_ignores_unknown_tags_in_the_breakdown(client, headers_for):
    body = client.post("/api/learning-style", json=_answers("V", "Z", "V"), headers=headers_for(9)).json()
    assert body["score_breakdown"] == {"V": 2, "A": 0, "R": 0, "K": 0} and body["style"] == "visual"


def test_learning_style_requires_well_formed_answers(client, headers_for):
    assert client.post("/api/learning-style", json={}, headers=headers_for(9)).status_code == 422
    assert client.post("/api/learning-style", json={"answers": [{"question_id": "q"}]}, headers=headers_for(9)).status_code == 422


# ── quiz adaptatif ──────────────────────────────────────────────────────────

QUESTION = {"id": 1, "question": "2+2 ?", "options": ["3", "4", "5", "6"], "correct_answer": "4",
            "explanation": "Addition.", "difficulty": "facile", "topic": "calcul"}


class AdaptiveClient:
    def __init__(self, content, error=None):
        self.content, self.error, self.calls = content, error, []

    def chat(self, messages, max_tokens=None, temperature=None, **kw):
        self.calls.append({"prompt": messages[0]["content"], "max_tokens": max_tokens, "temperature": temperature})
        if self.error:
            raise self.error
        return {"content": self.content}


@pytest.fixture
def adaptive(app_module, monkeypatch):
    def setup(content, progress=None, error=None):
        fake = AdaptiveClient(content, error)
        monkeypatch.setattr(app_module, "get_deepseek_client", lambda: fake)
        monkeypatch.setattr(app_module, "performance_analyzer", SimpleNamespace(
            analyze_user_progress=lambda uid: progress or {"success": False}))
        return fake

    return setup


def _quiz(client, headers_for, **body):
    return client.post("/api/adaptive-quiz", json={"user_id": 9, "subject": "Maths", **body}, headers=headers_for(9))


def test_adaptive_quiz_returns_the_parsed_questions(client, headers_for, adaptive):
    fake = adaptive("```json\n" + json.dumps([QUESTION]) + "\n```")
    r = _quiz(client, headers_for)
    body = r.json()
    assert r.status_code == 200 and body["count"] == 1 and body["questions"][0]["correct_answer"] == "4"
    assert body["subject"] == "Maths" and body["mode"] == "consolidation" and body["weak_areas"] == []
    datetime.fromisoformat(body["generated_at"])
    assert fake.calls[0]["max_tokens"] == 2500 and "8 questions" in fake.calls[0]["prompt"]


@pytest.mark.parametrize("recent,sent_mode,expected", [
    (49, "stretch", "remediation"), (80, "remediation", "stretch"), (50, "remediation", "remediation"),
    (79, "stretch", "stretch"), (None, "bizarre", "consolidation"), (None, " STRETCH ", "stretch"), (None, None, "consolidation")])
def test_adaptive_quiz_level_follows_the_recent_score_then_the_requested_mode(client, headers_for, adaptive,
                                                                              recent, sent_mode, expected):
    fake = adaptive(json.dumps([QUESTION]))
    body = _quiz(client, headers_for, recent_score=recent, mode=sent_mode).json()
    assert body["mode"] == expected
    marker = {"remediation": "REMÉDIATION", "consolidation": "CONSOLIDATION", "stretch": "APPROFONDISSEMENT"}[expected]
    assert marker in fake.calls[0]["prompt"]


def test_adaptive_quiz_merges_front_and_server_weak_topics_without_duplicates(client, headers_for, adaptive):
    fake = adaptive(json.dumps([QUESTION]), progress={"success": True, "analysis": {"weak_areas": ["Limites", "Suites"]}})
    body = _quiz(client, headers_for, weak_topics=["Suites", "Dérivées"]).json()
    assert body["weak_areas"] == ["Suites", "Dérivées", "Limites"]
    assert "lacunes en : Suites, Dérivées, Limites" in fake.calls[0]["prompt"]


def test_adaptive_quiz_profile_lines_and_default_subject(client, headers_for, adaptive):
    fake = adaptive(json.dumps([QUESTION]))
    body = _quiz(client, headers_for, subject="  ", education_level="Lycée", grade="Tle C", attempts_count=4,
                 strong_topics=["Fractions"]).json()
    prompt = fake.calls[0]["prompt"]
    assert body["subject"] == "les matières en cours" and "niveau intermédiaire" in prompt
    for line in ("Niveau scolaire : Lycée", "Classe / série : Tle C", "Quiz déjà passés : 4", "Chapitres maîtrisés : Fractions"):
        assert line in prompt


def test_adaptive_quiz_without_profile_says_no_history(client, headers_for, adaptive):
    fake = adaptive(json.dumps([QUESTION]))
    _quiz(client, headers_for)
    assert "Aucun historique disponible." in fake.calls[0]["prompt"]


@pytest.mark.parametrize("body", [{"count": 2}, {"count": 21}, {"recent_score": 101}, {"recent_score": -1}])
def test_adaptive_quiz_validates_its_parameters(client, headers_for, adaptive, body):
    adaptive("[]")
    assert _quiz(client, headers_for, **body).status_code == 422


def test_adaptive_quiz_malformed_json_gets_a_retry_message(client, headers_for, adaptive):
    adaptive("[{casse")
    r = _quiz(client, headers_for)
    assert r.status_code == 500 and "réessayez" in r.json()["detail"]


def test_adaptive_quiz_provider_failure_is_a_500_carrying_the_message(client, headers_for, adaptive):
    adaptive("", error=TimeoutError("délai dépassé"))
    r = _quiz(client, headers_for)
    assert r.status_code == 500 and "délai dépassé" in r.json()["detail"]


def test_adaptive_quiz_is_limited_to_five_per_minute(client, headers_for, adaptive):
    adaptive(json.dumps([QUESTION]))
    assert all(_quiz(client, headers_for).status_code == 200 for _ in range(5))
    assert _quiz(client, headers_for).status_code == 429
