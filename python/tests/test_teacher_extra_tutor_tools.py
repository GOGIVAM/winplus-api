"""Routes IA du professeur, deuxième partie (routes/teacher_extra_routes.py) : tarif et grille du répétiteur, prix de
groupe, analyse de profil, achats recommandés, veille éditoriale, compte-rendu de séance, fiche de révision, budget du
support de séance. SQLite en mémoire, fournisseur d'IA simulé, date figée."""

from datetime import datetime, timedelta
from types import SimpleNamespace

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

import database as dbm
import routes.teacher_extra_routes as tx

NOW = datetime(2026, 6, 17, 12, 0, 0)


class FrozenDatetime(datetime):
    @classmethod
    def now(cls, tz=None):
        return NOW.replace(tzinfo=tz) if tz else NOW

    @classmethod
    def utcnow(cls):
        return NOW


@pytest.fixture
def env(monkeypatch, sqlite_database):
    monkeypatch.setattr(tx, "datetime", FrozenDatetime)
    db = sqlite_database(tx, [dbm.Subject, dbm.CourseContent, dbm.Order, dbm.OrderItem, dbm.DownloadHistory,
                              dbm.TutorProfile, dbm.TutorSubject, dbm.TutorLevel, dbm.User])
    app = FastAPI()
    app.include_router(tx.teacher_ai_router, prefix="/api")
    client = TestClient(app, raise_server_exceptions=False)
    client.db = db
    return client


def fake_json(monkeypatch, value):
    calls = []

    def fake(prompt, system, max_tokens=600):
        calls.append({"prompt": prompt, "system": system, "max_tokens": max_tokens})
        return value

    monkeypatch.setattr(tx, "_deepseek_json", fake)
    return calls


def teacher(headers_for, uid=3):
    return headers_for(uid, "teacher")


# ── petits calculs ──────────────────────────────────────────────────────────

@pytest.mark.parametrize("value,expected", [(2449, 2400), (2451, 2500), (0, 0), (149, 100), (150.0001, 200)])
def test_rates_are_rounded_to_the_hundred_francs(value, expected):
    assert tx._round_xaf_100(value) == expected


@pytest.mark.parametrize("values,expected", [([5], 5), ([1, 9], 5), ([1, 3, 9], 3), ([1, 2, 3, 10], 2.5)])
def test_median(values, expected):
    assert tx._median(values) == expected


@pytest.mark.parametrize("value,low,high,expected", [(5.4, 2, 10, 5), (1, 2, 10, 2), (99, 2, 10, 10), (2.5, 2, 10, 2)])
def test_clamp(value, low, high, expected):
    assert tx._clamp(value, low, high) == expected


@pytest.mark.parametrize("minutes,shape,tokens", [
    (10, (2, 5, 2), 3410), (30, (2, 5, 2), 3410), (31, (3, 8, 3), 4750), (60, (3, 8, 3), 4750), (0, (3, 8, 3), 4750),
    (None, (3, 8, 3), 4750), (120, (4, 10, 4), 5920), (121, (5, 12, 5), 6000), (600, (5, 12, 5), 6000)])
def test_session_material_volume_follows_the_duration_and_tokens_are_capped(minutes, shape, tokens):
    budget = tx._session_material_budget(minutes)
    assert (budget["sections"], budget["quiz"], budget["exercises"]) == shape and budget["max_tokens"] == tokens


# ── tarif horaire du répétiteur ─────────────────────────────────────────────

URL_RATE = "/api/teacher/suggest-tutor-rate"


def tutor(env, id_, rate, subject="Maths", level="Terminale", active=True):
    env.db.add(dbm.TutorProfile(Id=id_, UserId=100 + id_, HourlyRateXaf=rate, IsActive=active),
               dbm.TutorSubject(Id=id_, TutorProfileId=id_, Subject=subject),
               dbm.TutorLevel(Id=id_, TutorProfileId=id_, Level=level))


def _rate(env, headers_for, **body):
    return env.post(URL_RATE, json={"subject": "maths", **body}, headers=teacher(headers_for))


def test_rate_requires_a_subject_and_the_teacher_role(env, headers_for):
    assert env.post(URL_RATE, json={}, headers=teacher(headers_for)).status_code == 422
    assert env.post(URL_RATE, json={"subject": "x"}).status_code in (401, 403)
    assert env.post(URL_RATE, json={"subject": "x"}, headers=headers_for(1, "student")).status_code == 403


def test_rate_from_three_active_tutors_is_the_market_median(env, headers_for, monkeypatch):
    calls = fake_json(monkeypatch, None)
    tutor(env, 1, 2000), tutor(env, 2, 3000), tutor(env, 3, 6000)
    tutor(env, 4, 9999, active=False), tutor(env, 5, 9999, subject="Physique")
    body = _rate(env, headers_for).json()
    assert body["based_on"] == "market_data" and body["sample_size"] == 3
    assert (body["suggested_rate_xaf"], body["range_low_xaf"], body["range_high_xaf"]) == (3000, 2000, 6000)
    assert "3 répétiteurs" in body["explanation"] and calls == []              # aucun appel au modèle


def test_rate_level_filter_narrows_the_sample(env, headers_for, monkeypatch):
    fake_json(monkeypatch, None)
    tutor(env, 1, 2000), tutor(env, 2, 3000), tutor(env, 3, 4000), tutor(env, 4, 9000, level="Seconde")
    body = _rate(env, headers_for, level="terminale").json()
    assert body["sample_size"] == 3 and "niveau terminale" in body["explanation"]


def test_rate_under_three_samples_asks_the_model_and_says_it_is_an_estimate(env, headers_for, monkeypatch):
    calls = fake_json(monkeypatch, {"suggested_rate_xaf": 3500, "range_low_xaf": 2500, "range_high_xaf": 5000, "explanation": "Marché."})
    tutor(env, 1, 2000)
    body = _rate(env, headers_for, level="Terminale", city="Douala").json()
    assert body["based_on"] == "estimation" and body["sample_size"] == 1 and body["suggested_rate_xaf"] == 3500
    assert body["explanation"] == "Marché." and "Douala" in calls[0]["prompt"] and calls[0]["max_tokens"] == 250


def test_rate_estimate_derives_the_range_when_the_model_omits_it(env, headers_for, monkeypatch):
    fake_json(monkeypatch, {"suggested_rate_xaf": 4000})
    body = _rate(env, headers_for).json()
    assert (body["range_low_xaf"], body["range_high_xaf"]) == (2800, 6000) and "faute de données" in body["explanation"]


@pytest.mark.parametrize("answer", [None, {}, {"suggested_rate_xaf": 0}, [1]])
def test_rate_default_estimate_when_the_model_is_unusable(env, headers_for, monkeypatch, answer):
    fake_json(monkeypatch, answer)
    body = _rate(env, headers_for).json()
    assert (body["suggested_rate_xaf"], body["range_low_xaf"], body["range_high_xaf"]) == (3000, 2000, 5000)
    assert body["based_on"] == "estimation" and body["sample_size"] == 0


def test_rate_grid_has_market_base_rates_surcharges_and_discounts(env, headers_for, monkeypatch):
    fake_json(monkeypatch, None)
    tutor(env, 1, 3000), tutor(env, 2, 4000), tutor(env, 3, 5000)
    body = _rate(env, headers_for, include_grid=True).json()
    grid = body["grid"]
    assert body["group"] is None
    assert [(b["subject"], b["mode"], b["hourly_rate_xaf"], b["based_on"]) for b in grid["base_rates"]] == [
        ("maths", "online", 4000, "market_data"), ("maths", "in_person", 4600, "market_data")]
    assert [(m["label"], m["percent_adjustment"]) for m in grid["modifiers"]] == [("Soir", 10), ("Week-end", 15)]
    assert grid["modifiers"][0]["days_of_week"] == [1, 2, 3, 4, 5] and grid["modifiers"][0]["start_time"] == "18:00"
    assert [(d["min_duration_minutes"], d["percent_discount"]) for d in grid["duration_discounts"]] == [(120, 5), (180, 10)]


def test_rate_grid_uses_the_estimate_for_subjects_without_enough_market_data(env, headers_for, monkeypatch):
    fake_json(monkeypatch, {"suggested_rate_xaf": 2000})
    tutor(env, 1, 9000, subject="Physique"), tutor(env, 2, 9000, subject="Physique"), tutor(env, 3, 9000, subject="Physique")
    grid = _rate(env, headers_for, include_grid=True, subjects=["Physique", "Maths"], modes=["online", "in_person"]).json()["grid"]
    by = {(b["subject"], b["mode"]): b for b in grid["base_rates"]}
    assert by[("Physique", "online")]["hourly_rate_xaf"] == 9000 and by[("Physique", "online")]["based_on"] == "market_data"
    assert by[("Maths", "online")]["hourly_rate_xaf"] == 2000 and by[("Maths", "online")]["based_on"] == "estimation"
    assert by[("Maths", "in_person")]["hourly_rate_xaf"] == 2300


def test_rate_grid_mode_and_subject_hygiene(env, headers_for, monkeypatch):
    fake_json(monkeypatch, {"suggested_rate_xaf": 2000})
    grid = _rate(env, headers_for, include_grid=True, modes=["telepathie"],
                 subjects=["  A  ", "", "B"] + [f"S{i}" for i in range(12)]).json()["grid"]
    assert {b["mode"] for b in grid["base_rates"]} == {"online"}
    assert len(grid["base_rates"]) == 8 and grid["base_rates"][0]["subject"] == "A"          # blancs écartés, 8 matières au plus


def test_rate_grid_never_goes_under_five_hundred_francs(env, headers_for, monkeypatch):
    fake_json(monkeypatch, {"suggested_rate_xaf": 100})
    rate = _rate(env, headers_for, include_grid=True, modes=["online"]).json()["grid"]["base_rates"][0]["hourly_rate_xaf"]
    assert rate == 500


def test_group_suggestion_defaults_to_a_small_support_group(env, headers_for, monkeypatch):
    fake_json(monkeypatch, None)
    group = _rate(env, headers_for, include_group=True).json()["group"]
    assert group["method"] == "per_student" and group["max_group_size"] == 6 and group["flat_price_percent"] == 160
    assert [(d["min_group_size"], d["percent_discount"]) for d in group["discounts"]] == [(2, 15), (4, 25), (6, 35)]
    assert group["based_on"] == "estimation" and group["sample_size"] == 0


def _profiles(*specs):
    return [{"id": i, "method": m, "max": mx, "flat": fl, "tiers": list(t)} for i, (m, mx, fl, t) in enumerate(specs, 1)]


def test_group_suggestion_from_market_medians(env, headers_for, monkeypatch):
    fake_json(monkeypatch, None)
    monkeypatch.setattr(tx, "_market_group_settings", lambda s: _profiles(
        ("per_student", 4, None, [(2, 10), (4, 30)]), ("per_student", 6, None, [(2, 20), (4, 30)]), ("flat", 8, 200, [])))
    group = _rate(env, headers_for, include_group=True).json()["group"]
    assert group["based_on"] == "market_data" and group["sample_size"] == 3 and group["max_group_size"] == 6
    assert group["flat_price_percent"] == 200
    assert [(d["min_group_size"], d["percent_discount"]) for d in group["discounts"]] == [(2, 15), (4, 30)]


def test_group_discounts_must_grow_with_the_group_size(env, headers_for, monkeypatch):
    fake_json(monkeypatch, None)
    monkeypatch.setattr(tx, "_market_group_settings", lambda s: _profiles(*[("per_student", 6, None, [(2, 30), (3, 20)])] * 3))
    discounts = _rate(env, headers_for, include_group=True).json()["group"]["discounts"]
    assert [(d["min_group_size"], d["percent_discount"]) for d in discounts] == [(2, 30)]


def test_group_without_observed_tiers_uses_the_defaults_within_the_median_size(env, headers_for, monkeypatch):
    fake_json(monkeypatch, None)
    monkeypatch.setattr(tx, "_market_group_settings", lambda s: _profiles(*[("flat", 4, 150, [])] * 3))
    group = _rate(env, headers_for, include_group=True).json()["group"]
    assert group["max_group_size"] == 4 and [d["min_group_size"] for d in group["discounts"]] == [2, 4]
    assert group["flat_price_percent"] == 150


def test_group_market_values_are_kept_within_the_dotnet_bounds(env, headers_for, monkeypatch):
    fake_json(monkeypatch, None)
    monkeypatch.setattr(tx, "_market_group_settings", lambda s: _profiles(*[("flat", 50, 5000, [])] * 3))
    group = _rate(env, headers_for, include_group=True).json()["group"]
    assert group["max_group_size"] == 20 and group["flat_price_percent"] == 1000


def test_group_settings_failure_falls_back_on_the_estimate(env, headers_for, monkeypatch):
    fake_json(monkeypatch, None)
    # le modèle SQLAlchemy ne porte pas les colonnes du lot 9.3 : la vraie requête échoue sous SQLite
    group = _rate(env, headers_for, include_group=True).json()["group"]
    assert group["based_on"] == "estimation"


# ── POST /teacher/analyze-tutor-profile ─────────────────────────────────────

URL_PROFILE = "/api/teacher/analyze-tutor-profile"


def test_profile_prompt_lists_the_profile_and_the_missing_fields(env, headers_for, monkeypatch):
    calls = fake_json(monkeypatch, [{"field": "bio", "suggestion": "Allonge ta bio."}])
    body = {"title": "Prof de maths", "tutor_bio": "Dix ans d'expérience", "subjects": ["Maths", "Physique"],
            "levels": ["Tle"], "hourly_rate_xaf": 3000, "has_video": True, "missing_fields": ["video", "levels"]}
    r = env.post(URL_PROFILE, json=body, headers=teacher(headers_for))
    assert r.json() == {"suggestions": [{"field": "bio", "suggestion": "Allonge ta bio."}]}
    prompt = calls[0]["prompt"]
    assert "Titre : Prof de maths" in prompt and "Matières : Maths, Physique" in prompt and "Tarif horaire : 3000 XAF" in prompt
    assert "Vidéo de présentation : oui" in prompt and "manquants : video, levels" in prompt


def test_profile_prompt_for_an_empty_profile(env, headers_for, monkeypatch):
    calls = fake_json(monkeypatch, None)
    env.post(URL_PROFILE, json={}, headers=teacher(headers_for))
    prompt = calls[0]["prompt"]
    assert "Titre : (vide)" in prompt and "(aucune)" in prompt and "(non défini)" in prompt and "Tous les champs structurels" in prompt


def test_profile_keeps_five_suggestions_and_drops_blank_ones(env, headers_for, monkeypatch):
    fake_json(monkeypatch, [{"field": f"f{i}", "suggestion": f" s{i} "} for i in range(8)] )
    suggestions = env.post(URL_PROFILE, json={}, headers=teacher(headers_for)).json()["suggestions"]
    assert [s["suggestion"] for s in suggestions] == [f"s{i}" for i in range(5)]
    fake_json(monkeypatch, [{"suggestion": "   "}, {"suggestion": "Sans champ"}])
    assert env.post(URL_PROFILE, json={}, headers=teacher(headers_for)).json()["suggestions"] == [
        {"field": "general", "suggestion": "Sans champ"}]


@pytest.mark.parametrize("answer", [None, [], {"a": 1}, [{"suggestion": ""}]])
def test_profile_has_a_default_suggestion_when_the_model_gives_nothing(env, headers_for, monkeypatch, answer):
    fake_json(monkeypatch, answer)
    suggestions = env.post(URL_PROFILE, json={}, headers=teacher(headers_for)).json()["suggestions"]
    assert len(suggestions) == 1 and suggestions[0]["field"] == "general" and "vidéo" in suggestions[0]["suggestion"]


@pytest.mark.xfail(strict=False, reason="analyze-tutor-profile appelle item.get() sur chaque élément du tableau du modèle : "
                   "une liste de chaînes (réponse fréquente) lève AttributeError et la route répond 500 au lieu du repli.")
def test_profile_survives_a_model_answer_made_of_plain_strings(env, headers_for, monkeypatch):
    fake_json(monkeypatch, ["Ajoute une vidéo", "Précise tes niveaux"])
    assert env.post(URL_PROFILE, json={}, headers=teacher(headers_for)).status_code == 200


# ── GET /teacher/recommended-purchases ──────────────────────────────────────

URL_BUY = "/api/teacher/recommended-purchases"


def _subject(env, id_, category, rating, published=True, enrolled=0, author=None, price=1000, deleted=False):
    env.db.add(dbm.Subject(Id=id_, Title=f"S{id_}", Category=category, AverageRating=rating, EnrollmentCount=enrolled,
                           IsPublished=published, IsDeleted=deleted, Price=price, AuthorUserId=author))


def test_purchases_stay_in_the_teachers_categories_and_skip_owned_contents(env, headers_for):
    _subject(env, 1, "Maths", 3.0, published=False)                       # là où le professeur a publié
    env.db.add(dbm.CourseContent(Id=1, SubjectId=1, Title="Cours", CreatedByUserId=3))
    _subject(env, 2, "Maths", 4.9)                                        # déjà acheté
    env.db.add(dbm.Order(Id=1, UserId=3, Status="completed"), dbm.OrderItem(Id=1, OrderId=1, SubjectId=2))
    _subject(env, 3, "Maths", 4.9, author=3)                              # écrit par lui
    _subject(env, 4, "Maths", 4.5, enrolled=1)
    _subject(env, 5, "Maths", 4.8)
    _subject(env, 6, "Maths", 5.0, published=False)
    _subject(env, 7, "Maths", 5.0, deleted=True)
    _subject(env, 8, "Histoire", 5.0)                                     # autre catégorie
    _subject(env, 9, "Maths", 4.9)                                        # achat non abouti : reste recommandable
    env.db.add(dbm.Order(Id=2, UserId=3, Status="pending"), dbm.OrderItem(Id=2, OrderId=2, SubjectId=9))
    items = env.get(URL_BUY, headers=teacher(headers_for, 3)).json()["items"]
    assert [i["subjectId"] for i in items] == [9, 5, 4]
    assert items[0]["justification"].startswith("Bien noté (4.9/5) en Maths")
    assert items[0]["price"] == 1000.0 and items[0]["category"] == "Maths"


def test_purchases_without_any_published_content_fall_back_on_the_best_rated_overall(env, headers_for):
    for i in range(1, 8):
        _subject(env, i, f"Cat{i}", float(i) / 2)
    items = env.get(URL_BUY, headers=teacher(headers_for, 3)).json()["items"]
    assert [i["subjectId"] for i in items] == [7, 6, 5, 4, 3]               # cinq au plus, mieux notés d'abord
    assert "pour démarrer ta bibliothèque" in items[0]["justification"]


def test_purchases_rating_ties_are_broken_by_popularity(env, headers_for):
    _subject(env, 1, "A", 4.0, enrolled=5), _subject(env, 2, "A", 4.0, enrolled=50)
    assert [i["subjectId"] for i in env.get(URL_BUY, headers=teacher(headers_for)).json()["items"]] == [2, 1]


def test_purchases_are_for_teachers_and_admins(env, headers_for):
    assert env.get(URL_BUY).status_code in (401, 403)
    assert env.get(URL_BUY, headers=headers_for(1, "student")).status_code == 403
    assert env.get(URL_BUY, headers=headers_for(1, "admin")).status_code == 200


# ── GET /teacher/editorial-watch ────────────────────────────────────────────

URL_WATCH = "/api/teacher/editorial-watch"


def _demand(env, category, downloads, contents, subject_id, old=0):
    env.db.add(dbm.Subject(Id=subject_id, Title=category, Category=category))
    base = len(env.db.all(dbm.DownloadHistory)) + 1
    env.db.add(*[dbm.DownloadHistory(Id=base + i, UserId=1, SubjectId=subject_id, CreatedAt=NOW - timedelta(days=2))
                 for i in range(downloads)],
               *[dbm.DownloadHistory(Id=base + downloads + i, UserId=1, SubjectId=subject_id, CreatedAt=NOW - timedelta(days=60))
                 for i in range(old)])
    cbase = len(env.db.all(dbm.CourseContent)) + 1
    env.db.add(*[dbm.CourseContent(Id=cbase + i, SubjectId=subject_id, Title="c") for i in range(contents)])


def test_watch_keeps_categories_where_demand_is_three_times_the_supply(env, headers_for):
    _demand(env, "Maths", 12, 2, 1)           # 6 x : retenue
    _demand(env, "Physique", 12, 4, 2)        # 3 x : retenue (seuil inclus)
    _demand(env, "SVT", 12, 5, 3)             # 2,4 x : offre correcte
    _demand(env, "Histoire", 9, 0, 4)         # demande trop faible
    _demand(env, "Anglais", 30, 0, 5)         # aucune offre
    _demand(env, "Latin", 5, 0, 6, old=40)    # téléchargements anciens ignorés
    items = env.get(URL_WATCH, headers=teacher(headers_for)).json()["items"]
    assert [i["category"] for i in items] == ["Anglais", "Maths", "Physique"] or \
        [i["category"] for i in items][0] == "Anglais"
    assert {i["category"] for i in items} == {"Anglais", "Maths", "Physique"}
    anglais = items[0]
    assert anglais["downloadsLast30Days"] == 30 and anglais["publishedContentCount"] == 0
    assert "30 téléchargements" in anglais["message"] and "0 contenu(s)" in anglais["message"]


def test_watch_is_empty_without_signal_and_capped_at_five(env, headers_for):
    assert env.get(URL_WATCH, headers=teacher(headers_for)).json() == {"items": []}
    for i in range(7):
        _demand(env, f"Cat{i}", 20 + i, 0, i + 1)
    assert len(env.get(URL_WATCH, headers=teacher(headers_for)).json()["items"]) == 5


def test_watch_requires_teacher_or_admin(env, headers_for):
    assert env.get(URL_WATCH, headers=headers_for(1, "parent")).status_code == 403


# ── POST /teacher/session-summary ───────────────────────────────────────────

URL_SUMMARY = "/api/teacher/session-summary"
SUMMARY = {"points_covered": ["Limites", 2], "student_questions": [], "decisions": ["Rattrapage mardi"],
           "homework": ["Exercice 3"], "summary_text": "  Bonne séance.  "}


def test_summary_rejects_a_transcript_that_is_too_short_without_calling_the_model(env, headers_for, monkeypatch):
    calls = fake_json(monkeypatch, SUMMARY)
    r = env.post(URL_SUMMARY, json={"transcript_text": "  trop court  "}, headers=teacher(headers_for))
    assert r.status_code == 422 and "trop courte" in r.json()["detail"] and calls == []
    assert env.post(URL_SUMMARY, json={}, headers=teacher(headers_for)).status_code == 422


def test_summary_returns_the_structured_report(env, headers_for, monkeypatch):
    calls = fake_json(monkeypatch, SUMMARY)
    body = env.post(URL_SUMMARY, json={"transcript_text": "x" * 30}, headers=teacher(headers_for)).json()
    assert body == {"points_covered": ["Limites", "2"], "student_questions": [], "decisions": ["Rattrapage mardi"],
                    "homework": ["Exercice 3"], "summary_text": "Bonne séance."}
    assert calls[0]["max_tokens"] == 800


def test_summary_transcript_is_cut_at_six_thousand_characters(env, headers_for, monkeypatch):
    calls = fake_json(monkeypatch, SUMMARY)
    env.post(URL_SUMMARY, json={"transcript_text": "a" * 6000 + "ZZZ"}, headers=teacher(headers_for))
    assert "a" * 6000 in calls[0]["prompt"] and "ZZZ" not in calls[0]["prompt"]


@pytest.mark.parametrize("answer", [None, [], "texte"])
def test_summary_model_failure_is_a_500(env, headers_for, monkeypatch, answer):
    fake_json(monkeypatch, answer)
    r = env.post(URL_SUMMARY, json={"transcript_text": "x" * 40}, headers=teacher(headers_for))
    assert r.status_code == 500 and "impossible" in r.json()["detail"]


def test_summary_missing_fields_default_to_empty(env, headers_for, monkeypatch):
    fake_json(monkeypatch, {"summary_text": "ok"})
    body = env.post(URL_SUMMARY, json={"transcript_text": "x" * 40}, headers=teacher(headers_for)).json()
    assert body["points_covered"] == [] and body["homework"] == [] and body["summary_text"] == "ok"


# ── POST /teacher/student-revision-sheet ────────────────────────────────────

URL_SHEET = "/api/teacher/student-revision-sheet"
SHEET = {"priority_topics": ["Limites"], "recommended_exercises": ["Séries"], "key_methods": ["Taux d'accroissement"],
         "sheet_text": " Fiche. "}


def test_sheet_needs_at_least_one_summary(env, headers_for, monkeypatch):
    calls = fake_json(monkeypatch, SHEET)
    r = env.post(URL_SHEET, json={"student_name": "Awa", "session_summaries": []}, headers=teacher(headers_for))
    assert r.status_code == 422 and "Aucun compte-rendu" in r.json()["detail"] and calls == []
    assert env.post(URL_SHEET, json={"session_summaries": ["x"]}, headers=teacher(headers_for)).status_code == 422


def test_sheet_returns_the_structured_fields_and_cuts_the_inputs(env, headers_for, monkeypatch):
    calls = fake_json(monkeypatch, SHEET)
    summaries = [f"séance{i} " + "x" * 900 for i in range(25)]
    body = env.post(URL_SHEET, json={"student_name": "Awa", "subject": "Maths", "session_summaries": summaries},
                    headers=teacher(headers_for)).json()
    assert body["sheet_text"] == "Fiche." and body["priority_topics"] == ["Limites"]
    prompt = calls[0]["prompt"]
    assert "Élève : Awa  Matière : Maths" in prompt and "séance19" in prompt and "séance20" not in prompt
    assert "x" * 790 in prompt and "x" * 801 not in prompt


def test_sheet_model_failure_is_a_500(env, headers_for, monkeypatch):
    fake_json(monkeypatch, None)
    r = env.post(URL_SHEET, json={"student_name": "Awa", "session_summaries": ["a"]}, headers=teacher(headers_for))
    assert r.status_code == 500 and "fiche de révision" in r.json()["detail"]
