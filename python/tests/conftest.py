"""Fixtures partagées des tests du service Python WinAI : jetons JWT signés avec le secret de test,
bases SQLite en mémoire remplaçant PostgreSQL. Rien ne touche le réseau."""

import os
import time

os.environ.setdefault("JWT_SECRET_KEY", "test-secret-key-for-winai-python-tests-0123456789")

import jwt  # noqa: E402
import pytest  # noqa: E402
from sqlalchemy import create_engine  # noqa: E402
from sqlalchemy.dialects.postgresql import ARRAY  # noqa: E402
from sqlalchemy.ext.compiler import compiles  # noqa: E402
from sqlalchemy.orm import sessionmaker  # noqa: E402
from sqlalchemy.pool import StaticPool  # noqa: E402


@compiles(ARRAY, "sqlite")
def _array_as_text(type_, compiler, **kw):
    # les colonnes ARRAY de PostgreSQL (table Users) n'existent pas sous SQLite
    return "TEXT"


@pytest.fixture
def token_for():
    """token_for(user_id=7, role="student", **claims) -> jeton utilisateur valide (en-tête Bearer compris)."""
    import auth

    def make(user_id=7, role="student", **claims):
        now = int(time.time())
        payload = {"user_id": user_id, "email": f"u{user_id}@example.test", "role": role,
                   "iss": auth.JWT_ISSUER, "aud": auth.JWT_AUDIENCE, "iat": now, "nbf": now, "exp": now + 600}
        payload.update(claims)
        return jwt.encode(payload, auth.JWT_SECRET, algorithm="HS256")

    return make


@pytest.fixture
def headers_for(token_for):
    """headers_for(user_id, role) -> {"Authorization": "Bearer ..."}"""

    def make(user_id=7, role="student", **claims):
        return {"Authorization": "Bearer " + token_for(user_id, role, **claims)}

    return make


@pytest.fixture
def service_headers():
    """service_headers(scope) -> en-tête d'un jeton technique .NET (audience de service, token_use=service)."""
    import auth

    def make(scope="ai.task", **claims):
        now = int(time.time())
        payload = {"sub": "service:winplus-dotnet", "token_use": "service", "scope": scope,
                   "iss": auth.JWT_ISSUER, "aud": auth.SERVICE_AUDIENCE, "iat": now, "nbf": now, "exp": now + 300}
        payload.update(claims)
        return {"Authorization": "Bearer " + jwt.encode(payload, auth.JWT_SECRET, algorithm="HS256")}

    return make


class SqliteDatabase:
    """Remplace database.Database : mêmes attributs engine et SessionLocal, mais sur SQLite en mémoire."""

    def __init__(self, models):
        import database as dbm

        self.engine = create_engine("sqlite://", connect_args={"check_same_thread": False}, poolclass=StaticPool)
        dbm.Base.metadata.create_all(self.engine, tables=[m.__table__ for m in models])
        self.SessionLocal = sessionmaker(bind=self.engine, autoflush=False)

    def get_session(self):
        return self.SessionLocal()

    def add(self, *rows):
        session = self.SessionLocal()
        try:
            session.add_all(rows)
            session.commit()
        finally:
            session.close()

    def all(self, model):
        session = self.SessionLocal()
        try:
            return session.query(model).all()
        finally:
            session.close()


@pytest.fixture
def sqlite_database(monkeypatch):
    """sqlite_database(module, models) -> SqliteDatabase ; module.Database() renvoie cette même base."""

    def make(module, models):
        db = SqliteDatabase(models)
        monkeypatch.setattr(module, "Database", lambda: db)
        return db

    return make
