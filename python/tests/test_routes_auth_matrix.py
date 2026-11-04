"""Matrice des droits de toutes les routes du service WinAI : chaque route exige un jeton, les routes
réservées à un rôle refusent les autres rôles (403) avant même de valider le corps, et les routes de
tâche de fond .NET refusent un jeton technique hors de leur portée.

Aucune route n'est exécutée jusqu'au bout : tous les cas testés sont refusés par la dépendance
d'authentification, avant l'accès à la base ou au fournisseur d'IA. Un test de complétude échoue si une
route est ajoutée sans être déclarée ici."""

import pytest
from fastapi import FastAPI
from fastapi.routing import APIRoute
from fastapi.testclient import TestClient

from routes.admin_routes import admin_router
from routes.canal_qa_routes import canal_qa_router
from routes.chatbot_routes import chatbot_router
from routes.cover_routes import router as cover_router
from routes.exam_coach_routes import exam_coach_router
from routes.exam_quiz_routes import exam_quiz_router
from routes.goal_suggestions import router as goal_router
from routes.institution_routes import institution_router
from routes.parent_advisor_routes import parent_advisor_router
from routes.parent_alert_routes import parent_alert_router
from routes.parent_extra_routes import parent_extra_router
from routes.quiz_explain_routes import quiz_explain_router
from routes.smart_ai_routes import smart_ai_router
from routes.study_session_routes import study_session_router
from routes.teacher_extra_routes import teacher_ai_router
from routes.translation_routes import router as translation_router
from routes.tutor_interview_routes import tutor_interview_router
from routes.winai_memory_routes import winai_memory_router

ALL_ROLES = ("student", "teacher", "parent", "admin", "institution")

# Même montage que app.py
MOUNTS = [
    (chatbot_router, "/api/chatbot"), (quiz_explain_router, "/api/quiz"), (exam_coach_router, "/api/exam-coach"),
    (parent_alert_router, "/api/parent-alerts"), (parent_advisor_router, "/api/parent-advisor"),
    (study_session_router, "/api/study-session"), (exam_quiz_router, "/api/exam-quiz"),
    (parent_extra_router, "/api"), (teacher_ai_router, "/api"), (institution_router, "/api"), (admin_router, "/api"),
    (tutor_interview_router, "/api"), (smart_ai_router, "/api"), (canal_qa_router, "/api"),
    (winai_memory_router, "/api"), (goal_router, ""), (cover_router, ""), (translation_router, ""),
]


def role(*roles):
    return ("role", roles)


def service(scope, roles=None):
    return ("service", scope, roles)


ANY = ("any",)
PUBLIC = ("public",)

ROUTES = {
    # administration
    ("POST", "/api/admin/anomaly-detection"): role("admin"),
    ("GET", "/api/admin/anomalies/active"): role("admin"),
    ("POST", "/api/admin/anomaly/1/resolve"): role("admin"),
    ("POST", "/api/admin/content-health-check"): role("admin"),
    ("POST", "/api/admin/growth-insights"): role("admin"),
    ("POST", "/api/admin/revenue-forecast"): role("admin"),
    ("POST", "/api/admin/moderate-content"): ANY,
    ("POST", "/api/admin/moderate-message"): ANY,
    ("GET", "/api/admin/moderation/pending"): role("admin"),
    ("POST", "/api/admin/moderation/1/resolve"): role("admin"),
    ("POST", "/api/admin/tutor-interview/guide"): service("ai.tutor-interview", ("admin",)),
    ("POST", "/api/admin/tutor-interview/report"): service("ai.tutor-interview", ("admin",)),
    # chat et pédagogie de l'élève
    ("GET", "/api/chatbot/health"): PUBLIC,
    ("POST", "/api/chatbot/chat"): service("ai.parent-report"),
    ("POST", "/api/chatbot/stream"): ANY,
    ("POST", "/api/chatbot/complete"): ANY,
    ("POST", "/api/winai/canal-qa"): ANY,
    ("GET", "/api/winai/memoire"): ANY,
    ("DELETE", "/api/winai/memoire/1"): ANY,
    ("POST", "/api/ai/cover/generate"): ANY,
    ("POST", "/api/ai/goal-suggestions"): ANY,
    ("POST", "/api/quiz/explain-error"): ANY,
    ("POST", "/api/exam-quiz/generate"): ANY,
    ("POST", "/api/exam-coach/generate"): ANY,
    ("POST", "/api/exam-coach/recalibrate"): ANY,
    ("GET", "/api/exam-coach/today/1"): ANY,
    ("POST", "/api/exam-coach/predict-grade"): ANY,
    ("GET", "/api/exam-coach/micro-intervention/1"): ANY,
    ("POST", "/api/study-session/generate"): ANY,
    ("POST", "/api/study-session/phase2/quiz"): ANY,
    ("POST", "/api/study-session/phase3/summary"): ANY,
    ("POST", "/api/study-session/complete"): ANY,
    ("GET", "/api/study-session/history/1"): ANY,
    ("POST", "/api/ai/generate-notification"): service("ai.notification"),
    ("POST", "/api/ai/summarize-notifications"): ANY,
    ("POST", "/api/ai/content-fit-analysis"): ANY,
    ("POST", "/api/revisions/generate-content"): ANY,
    ("POST", "/api/quizzes/generate-content"): ANY,
    ("POST", "/api/winai/translate-batch"): service("ai.content-translation", ()),
    # parents
    ("GET", "/api/parent-alerts/1"): role("parent", "admin"),
    ("GET", "/api/parent/children-insights"): role("parent", "admin"),
    ("POST", "/api/parent-advisor/threshold-alerts"): service("ai.parent-advisor-thresholds", ()),
    ("POST", "/api/parent-advisor/weekly-trend"): service("ai.parent-weekly-trend", ()),
    # établissements
    ("POST", "/api/institution/class-prediction"): role("institution", "admin"),
    ("GET", "/api/institution/benchmark/1"): role("institution", "admin"),
    ("GET", "/api/institution/at-risk-students/1"): role("institution", "admin"),
    ("POST", "/api/institution/action-plan"): service("ai.institution-report", ("institution", "admin")),
    ("POST", "/api/institution/appreciation"): service("ai.institution-report", ("institution", "admin")),
    # professeurs et répétiteurs
    ("POST", "/api/ai/generate-quiz-questions"): role("teacher", "admin"),
    ("POST", "/api/ai/optimize-title"): role("teacher", "admin"),
    ("POST", "/api/ai/generate-description"): role("teacher", "admin"),
    ("POST", "/api/teacher/class-analysis"): role("teacher", "admin"),
    ("GET", "/api/teacher/content-impact/1"): role("teacher", "admin"),
    ("POST", "/api/teacher/generate-correction"): role("teacher", "admin"),
    ("POST", "/api/teacher/predict-popularity"): role("teacher", "admin"),
    ("POST", "/api/teacher/analyze-submission"): role("teacher", "admin"),
    ("POST", "/api/teacher/suggest-tutor-rate"): role("teacher", "admin"),
    ("POST", "/api/teacher/analyze-tutor-profile"): role("teacher", "admin"),
    ("GET", "/api/teacher/recommended-purchases"): role("teacher", "admin"),
    ("GET", "/api/teacher/editorial-watch"): role("teacher", "admin"),
    ("POST", "/api/teacher/session-summary"): role("teacher", "admin"),
    ("POST", "/api/teacher/student-revision-sheet"): role("teacher", "admin"),
    ("POST", "/api/teacher/session-material"): role("teacher", "admin"),
    ("POST", "/api/teacher/coaching-report"): service("ai.coaching-report", ("teacher", "admin")),
    ("POST", "/api/messaging/quick-replies"): ANY,
    ("POST", "/api/messaging/generate-reply"): ANY,
    ("POST", "/api/messaging/parent-report"): role("teacher", "admin"),
    ("POST", "/api/teacher/inactivity-relaunch-message"): role("teacher", "admin"),
    ("POST", "/api/winai/detection-decrochage"): service("ai.decrochage", ()),
    ("POST", "/api/teacher/generate-syllabus"): role("teacher", "admin"),
    ("POST", "/api/teacher/evaluate-course"): role("teacher", "admin"),
    ("POST", "/api/teacher/analyze-content-upload"): role("teacher", "admin"),
}


@pytest.fixture(scope="module")
def client():
    app = FastAPI()
    for router, prefix in MOUNTS:
        app.include_router(router, prefix=prefix)
    return TestClient(app, raise_server_exceptions=False)


def _call(client, method, path, headers=None):
    kwargs = {"headers": headers or {}}
    if method in ("POST", "PUT", "PATCH"):
        kwargs["json"] = {}
    return client.request(method, path, **kwargs)


def _items(kind):
    return [(m, p, spec) for (m, p), spec in ROUTES.items() if spec[0] == kind]


# ── complétude ──────────────────────────────────────────────────────────────

def _normalize(path):
    """Remplace chaque paramètre de chemin {x} par 1, comme dans la table ci-dessus."""
    parts = []
    for part in path.split("/"):
        parts.append("1" if part.startswith("{") and part.endswith("}") else part)
    return "/".join(parts)


def test_every_mounted_route_is_declared_in_the_matrix(client):
    mounted = set()
    for route in client.app.routes:
        if isinstance(route, APIRoute):
            for method in route.methods - {"HEAD", "OPTIONS"}:
                mounted.add((method, _normalize(route.path)))
    assert mounted == set(ROUTES)


# ── tout jeton manquant ou falsifié est refusé ──────────────────────────────

PROTECTED = [(m, p) for (m, p), spec in ROUTES.items() if spec[0] != "public"]


@pytest.mark.parametrize("method,path", PROTECTED)
def test_missing_token_is_refused(client, method, path):
    assert _call(client, method, path).status_code in (401, 403)


@pytest.mark.parametrize("method,path", PROTECTED)
def test_forged_token_is_401(client, method, path):
    assert _call(client, method, path, {"Authorization": "Bearer pas.un.jeton"}).status_code == 401


def test_the_only_public_route_is_the_chatbot_health_check(client):
    assert [(m, p) for (m, p), spec in ROUTES.items() if spec[0] == "public"] == [("GET", "/api/chatbot/health")]


# ── routes réservées à des rôles ────────────────────────────────────────────

ROLE_DENIALS = [(m, p, r) for m, p, spec in _items("role") for r in ALL_ROLES if r not in spec[1]]


@pytest.mark.parametrize("method,path,user_role", ROLE_DENIALS)
def test_role_restricted_routes_refuse_other_roles(client, headers_for, method, path, user_role):
    r = _call(client, method, path, headers_for(5, user_role))
    assert r.status_code == 403, f"{method} {path} avec le rôle {user_role} : {r.status_code}"


# ── routes de tâche de fond .NET ────────────────────────────────────────────

SERVICE_ITEMS = _items("service")


@pytest.mark.parametrize("method,path,spec", SERVICE_ITEMS, ids=[f"{m} {p}" for m, p, _ in SERVICE_ITEMS])
def test_service_routes_refuse_a_token_of_another_scope(client, service_headers, method, path, spec):
    r = _call(client, method, path, service_headers("ai.autre-tache"))
    assert r.status_code == 403


@pytest.mark.parametrize("method,path,spec", SERVICE_ITEMS, ids=[f"{m} {p}" for m, p, _ in SERVICE_ITEMS])
def test_service_routes_refuse_an_expired_service_token(client, service_headers, method, path, spec):
    r = _call(client, method, path, service_headers(spec[1], exp=1))
    assert r.status_code == 401


SERVICE_USER_DENIALS = [(m, p, r) for m, p, spec in SERVICE_ITEMS if spec[2] is not None
                        for r in ALL_ROLES if r not in spec[2]]


@pytest.mark.parametrize("method,path,user_role", SERVICE_USER_DENIALS)
def test_service_routes_with_a_role_list_refuse_other_user_roles(client, headers_for, method, path, user_role):
    assert _call(client, method, path, headers_for(5, user_role)).status_code == 403


def test_user_token_cannot_reach_a_service_only_route_even_as_admin(client, headers_for):
    service_only = [(m, p) for m, p, spec in SERVICE_ITEMS if spec[2] == ()]
    assert service_only, "la table doit contenir des routes réservées à la tâche de fond"
    for method, path in service_only:
        assert _call(client, method, path, headers_for(1, "admin")).status_code == 403
