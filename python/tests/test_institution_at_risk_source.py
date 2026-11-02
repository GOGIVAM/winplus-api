"""Garde-fou : la route des élèves à risque ne doit appeler que des fonctions qui existent.

`_db.func_avg(...)` n'a jamais existé sur la classe Database : GET /api/institution/at-risk-students/{id}
répondait 500 (AttributeError) pour tout compte établissement ou administrateur. La moyenne se calcule avec
sqlalchemy.func.avg, déjà importé dans le module.
"""
import ast
import inspect
from pathlib import Path

SOURCE = Path(__file__).resolve().parents[1] / "routes" / "institution_routes.py"

# Attributs posés dans Database.__init__ (donc absents de la classe, présents sur l'instance).
INSTANCE_ATTRIBUTES = {"engine", "SessionLocal"}


def _attributes_used_on_db(tree: ast.AST) -> set[str]:
    names: set[str] = set()
    for node in ast.walk(tree):
        if isinstance(node, ast.Attribute) and isinstance(node.value, ast.Name) and node.value.id == "_db":
            names.add(node.attr)
    return names


def test_instance_attributes_are_really_set_in_init():
    from database import Database

    init_source = inspect.getsource(Database.__init__)
    for attr in INSTANCE_ATTRIBUTES:
        assert f"self.{attr}" in init_source, f"{attr} n'est plus défini dans Database.__init__"


def test_no_unknown_database_member_is_used():
    from database import Database

    tree = ast.parse(SOURCE.read_text(encoding="utf-8"))
    unknown = sorted(n for n in _attributes_used_on_db(tree) if not hasattr(Database, n) and n not in INSTANCE_ATTRIBUTES)
    assert unknown == [], f"membres utilisés sur _db mais absents de Database : {unknown}"


def test_average_uses_sqlalchemy_func():
    text = SOURCE.read_text(encoding="utf-8")
    assert "func_avg" not in text
    assert "func.avg(DailyScore.AverageScore)" in text
