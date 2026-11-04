"""Authentification JWT du service WinAI (auth.py) : jetons utilisateur, rôles, jetons techniques
service à service, fenêtre de compatibilité de l'ancien algorithme. Aucun accès réseau."""

import os
import time
from datetime import datetime, timedelta, timezone

os.environ.setdefault("JWT_SECRET_KEY", "test-secret-key-for-auth-tests-0123456789-abcdef")

import jwt  # noqa: E402
import pytest  # noqa: E402
from fastapi import Depends, FastAPI  # noqa: E402
from fastapi.testclient import TestClient  # noqa: E402

import auth  # noqa: E402

UTC = timezone.utc


def _user_token(secret=None, algorithm="HS256", **overrides):
    now = int(time.time())
    payload = {"user_id": 7, "email": "eleve@example.test", "role": "student",
               "iss": auth.JWT_ISSUER, "aud": auth.JWT_AUDIENCE, "iat": now, "nbf": now, "exp": now + 600}
    payload.update(overrides)
    for key in [k for k, v in payload.items() if v is None]:
        del payload[key]
    return jwt.encode(payload, secret or auth.JWT_SECRET, algorithm=algorithm)


def _service_token(scope="ai.task", secret=None, **overrides):
    now = int(time.time())
    payload = {"sub": "service:winplus-dotnet", "token_use": "service", "scope": scope,
               "iss": auth.JWT_ISSUER, "aud": auth.SERVICE_AUDIENCE, "iat": now, "nbf": now, "exp": now + 300}
    payload.update(overrides)
    for key in [k for k, v in payload.items() if v is None]:
        del payload[key]
    return jwt.encode(payload, secret or auth.JWT_SECRET, algorithm="HS256")


def _bearer(token):
    return {"Authorization": f"Bearer {token}"}


@pytest.fixture
def client():
    app = FastAPI()

    @app.get("/user")
    async def user_route(user: auth.UserTokenData = Depends(auth.verify_token)):
        return {"user_id": user.user_id, "email": user.email, "role": user.role, "is_service": user.is_service}

    @app.get("/staff")
    async def staff_route(user: auth.UserTokenData = Depends(auth.require_role("admin", "Teacher"))):
        return {"role": user.role}

    @app.get("/both")
    async def both_route(user: auth.UserTokenData = Depends(auth.require_user_or_service("ai.task", "ai.other"))):
        return {"user_id": user.user_id, "role": user.role, "is_service": user.is_service, "scopes": user.scopes}

    @app.get("/both-teacher")
    async def both_teacher(user: auth.UserTokenData = Depends(auth.require_user_or_service("ai.task", roles=("Teacher",)))):
        return {"role": user.role}

    @app.get("/service-only")
    async def service_only(user: auth.UserTokenData = Depends(auth.require_user_or_service("ai.task", roles=()))):
        return {"is_service": user.is_service}

    return TestClient(app)


# ── verify_token ────────────────────────────────────────────────────────────

def test_valid_user_token_is_accepted(client):
    r = client.get("/user", headers=_bearer(_user_token()))
    assert r.status_code == 200
    assert r.json() == {"user_id": 7, "email": "eleve@example.test", "role": "student", "is_service": False}


def test_missing_role_defaults_to_student(client):
    r = client.get("/user", headers=_bearer(_user_token(role=None)))
    assert r.status_code == 200 and r.json()["role"] == "student"


def test_missing_authorization_header_is_rejected(client):
    assert client.get("/user").status_code in (401, 403)


def test_garbage_token_is_401(client):
    r = client.get("/user", headers=_bearer("pas.un.jeton"))
    assert r.status_code == 401 and r.headers["WWW-Authenticate"] == "Bearer"


def test_expired_token_has_a_specific_message(client):
    r = client.get("/user", headers=_bearer(_user_token(exp=int(time.time()) - 10)))
    assert r.status_code == 401 and "expired" in r.json()["detail"]


def test_wrong_signature_is_401(client):
    r = client.get("/user", headers=_bearer(_user_token(secret="un-autre-secret-de-test-0123456789-abcdef")))
    assert r.status_code == 401 and r.json()["detail"] == "Authentication token is invalid"


def test_wrong_audience_is_401(client):
    assert client.get("/user", headers=_bearer(_user_token(aud="AutreAudience"))).status_code == 401


def test_wrong_issuer_is_401(client):
    assert client.get("/user", headers=_bearer(_user_token(iss="Pirate"))).status_code == 401


def test_service_token_cannot_call_a_user_route(client):
    # audience technique distincte : verify_token la refuse
    assert client.get("/user", headers=_bearer(_service_token())).status_code == 401


def test_unsupported_algorithm_is_401(client):
    assert client.get("/user", headers=_bearer(_user_token(algorithm="HS512"))).status_code == 401


def test_unsigned_token_is_rejected(client):
    now = int(time.time())
    unsigned = jwt.encode({"user_id": 1, "iss": auth.JWT_ISSUER, "aud": auth.JWT_AUDIENCE, "exp": now + 60},
                          key=None, algorithm="none")
    assert client.get("/user", headers=_bearer(unsigned)).status_code == 401


@pytest.mark.xfail(strict=False, reason="verify_token lève HTTPException(401) dans son try, puis `except Exception` "
                   "la convertit en 500 : un jeton sans user_id devrait donner 401 (bogue probable de auth.py)")
def test_token_without_user_id_is_401(client):
    r = client.get("/user", headers=_bearer(_user_token(user_id=None)))
    assert r.status_code == 401


# ── ancien algorithme XML-dsig et fenêtre de compatibilité ──────────────────

def test_legacy_algorithm_is_accepted_only_while_window_is_open(client, monkeypatch):
    legacy = _user_token(algorithm=auth.XMLDSIG_HMAC_SHA256)
    monkeypatch.setattr(auth, "LEGACY_ALG_DEADLINE", datetime.now(UTC) + timedelta(hours=1))
    assert client.get("/user", headers=_bearer(legacy)).status_code == 200
    monkeypatch.setattr(auth, "LEGACY_ALG_DEADLINE", datetime.now(UTC) - timedelta(seconds=1))
    assert client.get("/user", headers=_bearer(legacy)).status_code == 401
    # HS256 reste accepté après la fermeture
    assert client.get("/user", headers=_bearer(_user_token())).status_code == 200


def test_accepted_algorithms_follow_the_deadline(monkeypatch):
    monkeypatch.setattr(auth, "LEGACY_ALG_DEADLINE", datetime.now(UTC) + timedelta(days=1))
    assert auth.accepted_algorithms() == ["HS256", auth.XMLDSIG_HMAC_SHA256]
    monkeypatch.setattr(auth, "LEGACY_ALG_DEADLINE", datetime.now(UTC) - timedelta(days=1))
    assert auth.accepted_algorithms() == ["HS256"]


class _Frozen(datetime):
    current = datetime(2026, 3, 1, 12, 0, tzinfo=UTC)

    @classmethod
    def now(cls, tz=None):
        return cls.current


@pytest.fixture
def deadline_file(tmp_path, monkeypatch):
    path = tmp_path / "deadline"
    monkeypatch.setattr(auth, "_DEADLINE_FILE", str(path))
    monkeypatch.setattr(auth, "datetime", _Frozen)
    monkeypatch.setattr(auth, "LEGACY_ALG_WINDOW_HOURS", 48)
    _Frozen.current = datetime(2026, 3, 1, 12, 0, tzinfo=UTC)
    return path


def test_first_run_writes_a_deadline_48_hours_ahead(deadline_file):
    deadline = auth._resolve_legacy_deadline()
    assert deadline == datetime(2026, 3, 3, 12, 0, tzinfo=UTC)
    assert datetime.fromisoformat(deadline_file.read_text().strip()) == deadline


def test_restart_does_not_extend_the_window(deadline_file):
    first = auth._resolve_legacy_deadline()
    _Frozen.current = datetime(2026, 3, 2, 8, 0, tzinfo=UTC)  # redémarrage 20 h plus tard
    assert auth._resolve_legacy_deadline() == first


def test_corrupt_deadline_file_is_replaced(deadline_file):
    deadline_file.write_text("pas une date")
    assert auth._resolve_legacy_deadline() == datetime(2026, 3, 3, 12, 0, tzinfo=UTC)
    assert datetime.fromisoformat(deadline_file.read_text().strip()) == datetime(2026, 3, 3, 12, 0, tzinfo=UTC)


def test_zero_window_disables_the_tolerance(deadline_file, monkeypatch):
    monkeypatch.setattr(auth, "LEGACY_ALG_WINDOW_HOURS", 0)
    deadline = auth._resolve_legacy_deadline()
    assert deadline < _Frozen.current
    assert not deadline_file.exists()


def test_unwritable_deadline_location_does_not_block_startup(tmp_path, monkeypatch):
    blocker = tmp_path / "fichier"
    blocker.write_text("x")
    monkeypatch.setattr(auth, "_DEADLINE_FILE", str(blocker / "sous" / "deadline"))
    monkeypatch.setattr(auth, "datetime", _Frozen)
    monkeypatch.setattr(auth, "LEGACY_ALG_WINDOW_HOURS", 48)
    _Frozen.current = datetime(2026, 3, 1, 12, 0, tzinfo=UTC)
    assert auth._resolve_legacy_deadline() == datetime(2026, 3, 3, 12, 0, tzinfo=UTC)


# ── require_role ────────────────────────────────────────────────────────────

def test_require_role_is_case_insensitive_both_ways(client):
    assert client.get("/staff", headers=_bearer(_user_token(role="ADMIN"))).status_code == 200
    assert client.get("/staff", headers=_bearer(_user_token(role="teacher"))).status_code == 200


def test_require_role_forbids_other_roles_and_lists_required_ones(client):
    r = client.get("/staff", headers=_bearer(_user_token(role="student")))
    assert r.status_code == 403
    assert "admin" in r.json()["detail"] and "Teacher" in r.json()["detail"]


def test_require_role_without_token_is_not_a_403_for_a_role(client):
    assert client.get("/staff").status_code in (401, 403)
    assert client.get("/staff", headers=_bearer("x.y.z")).status_code == 401


# ── require_user_or_service ─────────────────────────────────────────────────

def test_service_token_with_allowed_scope(client):
    r = client.get("/both", headers=_bearer(_service_token(scope="ai.task")))
    assert r.status_code == 200
    assert r.json() == {"user_id": 0, "role": "service", "is_service": True, "scopes": ["ai.task"]}


def test_service_token_with_several_scopes_needs_only_one_match(client):
    r = client.get("/both", headers=_bearer(_service_token(scope="autre ai.other")))
    assert r.status_code == 200 and r.json()["scopes"] == ["autre", "ai.other"]


def test_service_token_outside_its_scope_is_403(client):
    r = client.get("/both", headers=_bearer(_service_token(scope="ai.interdit")))
    assert r.status_code == 403 and "scope" in r.json()["detail"]


def test_service_token_without_scope_claim_is_403(client):
    assert client.get("/both", headers=_bearer(_service_token(scope=""))).status_code == 403


def test_expired_service_token_is_401(client):
    r = client.get("/both", headers=_bearer(_service_token(exp=int(time.time()) - 5)))
    assert r.status_code == 401 and r.json()["detail"] == "Service token expired"


def test_service_token_signed_with_another_secret_is_401(client):
    r = client.get("/both", headers=_bearer(_service_token(secret="autre-secret-de-test-0123456789-abcdefgh")))
    assert r.status_code == 401 and r.json()["detail"] == "Service token is invalid"


def test_service_audience_token_without_service_use_is_401(client):
    assert client.get("/both", headers=_bearer(_service_token(token_use="user"))).status_code == 401
    assert client.get("/both", headers=_bearer(_service_token(token_use=None))).status_code == 401


def test_service_token_with_audience_list_is_routed_to_service_validation(client):
    token = _service_token(aud=[auth.SERVICE_AUDIENCE, "Autre"])
    # jwt.decode(audience=SERVICE_AUDIENCE) accepte une liste qui contient l'audience attendue
    assert client.get("/both", headers=_bearer(token)).status_code == 200


def test_user_token_is_accepted_on_a_mixed_route(client):
    r = client.get("/both", headers=_bearer(_user_token()))
    assert r.status_code == 200 and r.json()["is_service"] is False and r.json()["user_id"] == 7


def test_mixed_route_with_roles_filters_user_tokens_only(client):
    assert client.get("/both-teacher", headers=_bearer(_user_token(role="teacher"))).status_code == 200
    r = client.get("/both-teacher", headers=_bearer(_user_token(role="student")))
    assert r.status_code == 403
    # le jeton technique, lui, passe par son scope
    assert client.get("/both-teacher", headers=_bearer(_service_token())).status_code == 200


def test_empty_roles_tuple_reserves_the_route_for_background_tasks(client):
    assert client.get("/service-only", headers=_bearer(_user_token(role="admin"))).status_code == 403
    assert client.get("/service-only", headers=_bearer(_service_token())).json() == {"is_service": True}


def test_peek_audience():
    assert auth._peek_audience(_service_token()) == auth.SERVICE_AUDIENCE
    assert auth._peek_audience(_user_token()) == auth.JWT_AUDIENCE
    assert auth._peek_audience("pas un jeton") is None
    assert auth._peek_audience(_service_token(aud=["Autre", auth.SERVICE_AUDIENCE])) == auth.SERVICE_AUDIENCE
    assert auth._peek_audience(_service_token(aud=["Autre", "Encore"])) == "Autre"
    assert auth._peek_audience(_service_token(aud=[])) is None


def test_user_token_data_defaults():
    data = auth.UserTokenData(3, "a@b.c", "parent")
    assert (data.user_id, data.email, data.role, data.is_service, data.scopes) == (3, "a@b.c", "parent", False, [])


def test_constants_match_the_dotnet_contract():
    assert auth.JWT_ALGORITHM == "HS256"
    assert auth.SERVICE_AUDIENCE == "WinPlusAIService" and auth.SERVICE_TOKEN_USE == "service"
    assert auth.XMLDSIG_HMAC_SHA256 == "http://www.w3.org/2001/04/xmldsig-more#hmac-sha256"
