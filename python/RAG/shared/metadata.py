"""Construction et versioning des identifiants documentaires (Phase 1, §1.9)."""

from __future__ import annotations

from datetime import date


def build_doc_id(doc_type: str, code: str, version: int, effective_date: date | None = None) -> str:
    """Schéma : {type}_{code}_v{version}_{date}. Ex: EPREUVE_BACC2026_v1_20260901."""
    d = (effective_date or date.today()).strftime("%Y%m%d")
    return f"{doc_type}_{code}_v{version}_{d}"
