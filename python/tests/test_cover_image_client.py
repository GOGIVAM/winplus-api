"""Pochettes de documents (services/cover_image_client.py) : prompt construit et lecture de la
réponse du modèle d'image. Le SDK Gemini est remplacé par un faux module : aucun appel réseau."""

import sys
import types
from types import SimpleNamespace

import pytest

from services import cover_image_client as cic


# ── build_cover_prompt ──────────────────────────────────────────────────────

def test_prompt_states_kind_and_title_and_forbids_text():
    prompt = cic.build_cover_prompt("  Les fonctions affines  ", None, "cours")
    assert "Type de contenu : cours." in prompt
    assert "Sujet : Les fonctions affines." in prompt
    assert "sans aucun texte" in prompt
    assert "Contexte" not in prompt


def test_prompt_adds_context_only_for_non_blank_description():
    assert "Contexte : Révision du BEPC." in cic.build_cover_prompt("T", "  Révision du BEPC.  ", "epreuve")
    assert "Contexte" not in cic.build_cover_prompt("T", "   ", "epreuve")
    assert "Contexte : Un résumé" in cic.build_cover_prompt("T", "Un résumé", "epreuve")


def test_prompt_truncates_title_and_description():
    prompt = cic.build_cover_prompt("t" * 300, "d" * 900, "x")
    assert "t" * 200 in prompt and "t" * 201 not in prompt
    assert "d" * 600 in prompt and "d" * 601 not in prompt


# ── generate_cover_png ──────────────────────────────────────────────────────

def _install_fake_sdk(monkeypatch, generate):
    """Installe google.genai et google.genai.types factices ; generate remplace models.generate_content."""
    captured = {}

    class FakeClient:
        def __init__(self, api_key):
            captured["api_key"] = api_key
            self.models = SimpleNamespace(generate_content=lambda **kw: generate(captured, kw))

    genai = types.ModuleType("google.genai")
    genai.Client = FakeClient
    genai_types = types.ModuleType("google.genai.types")
    genai_types.GenerateContentConfig = lambda **kw: SimpleNamespace(**kw)
    genai.types = genai_types
    google = types.ModuleType("google")
    google.genai = genai
    monkeypatch.setitem(sys.modules, "google", google)
    monkeypatch.setitem(sys.modules, "google.genai", genai)
    monkeypatch.setitem(sys.modules, "google.genai.types", genai_types)
    monkeypatch.setattr(cic, "GEMINI_API_KEY", "cle-test")
    monkeypatch.setattr(cic, "COVER_IMAGE_MODEL", "modele-image-test")
    return captured


def _response(*parts_per_candidate):
    return SimpleNamespace(candidates=[
        SimpleNamespace(content=SimpleNamespace(parts=list(parts))) for parts in parts_per_candidate])


def test_missing_api_key_raises_without_importing_the_sdk(monkeypatch):
    monkeypatch.setattr(cic, "GEMINI_API_KEY", "")
    with pytest.raises(cic.CoverImageUnavailable, match="GEMINI_API_KEY"):
        cic.generate_cover_png("T", None, "cours")


def test_returns_first_inline_image(monkeypatch):
    def generate(captured, kw):
        captured["kw"] = kw
        return _response([SimpleNamespace(inline_data=None),
                          SimpleNamespace(inline_data=SimpleNamespace(data=b"\x89PNG", mime_type="image/png"))])

    captured = _install_fake_sdk(monkeypatch, generate)
    image = cic.generate_cover_png("Maths", "Algèbre", "cours")
    assert image == cic.CoverImage(png_bytes=b"\x89PNG", mime_type="image/png", model="modele-image-test")
    assert captured["api_key"] == "cle-test"
    assert captured["kw"]["model"] == "modele-image-test"
    assert captured["kw"]["contents"] == cic.build_cover_prompt("Maths", "Algèbre", "cours")
    assert captured["kw"]["config"].response_modalities == ["IMAGE"]


def test_mime_type_defaults_to_png(monkeypatch):
    _install_fake_sdk(monkeypatch, lambda c, kw: _response([SimpleNamespace(
        inline_data=SimpleNamespace(data=b"img", mime_type=None))]))
    assert cic.generate_cover_png("T", None, "x").mime_type == "image/png"


def test_provider_error_becomes_unavailable_with_model_name(monkeypatch):
    def generate(captured, kw):
        raise RuntimeError("quota dépassé")

    _install_fake_sdk(monkeypatch, generate)
    with pytest.raises(cic.CoverImageUnavailable, match="modele-image-test") as info:
        cic.generate_cover_png("T", None, "x")
    assert isinstance(info.value.__cause__, RuntimeError)


@pytest.mark.parametrize("response", [
    SimpleNamespace(candidates=None),
    SimpleNamespace(candidates=[]),
    SimpleNamespace(candidates=[SimpleNamespace(content=None)]),
    SimpleNamespace(candidates=[SimpleNamespace(content=SimpleNamespace(parts=None))]),
    SimpleNamespace(candidates=[SimpleNamespace(content=SimpleNamespace(
        parts=[SimpleNamespace(inline_data=SimpleNamespace(data=b"", mime_type="image/png"))]))]),
])
def test_response_without_image_raises(monkeypatch, response):
    _install_fake_sdk(monkeypatch, lambda c, kw: response)
    with pytest.raises(cic.CoverImageUnavailable, match="aucune image"):
        cic.generate_cover_png("T", None, "x")


def test_cover_image_is_immutable():
    image = cic.CoverImage(b"x", "image/png", "m")
    with pytest.raises(Exception):
        image.model = "autre"
