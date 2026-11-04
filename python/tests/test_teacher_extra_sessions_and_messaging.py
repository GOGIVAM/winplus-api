"""Routes IA du professeur, troisième partie (routes/teacher_extra_routes.py) : support de séance de tutorat,
participants, rapport de coaching, messagerie (réponses rapides, réponse longue, rapport aux parents), relance
d'inactivité, détection de décrochage, syllabus. Fournisseur d'IA simulé ; aucune base réelle."""

from types import SimpleNamespace

import pytest
from fastapi import FastAPI, HTTPException
from fastapi.testclient import TestClient

import database as dbm
import routes.teacher_extra_routes as tx


@pytest.fixture
def client():
    app = FastAPI()
    app.include_router(tx.teacher_ai_router, prefix="/api")
    return TestClient(app, raise_server_exceptions=False)


def fake_json(monkeypatch, value):
    calls = []

    def fake(prompt, system, max_tokens=600):
        calls.append({"prompt": prompt, "system": system, "max_tokens": max_tokens})
        return value

    monkeypatch.setattr(tx, "_deepseek_json", fake)
    return calls


def fake_text(monkeypatch, value):
    calls = []

    def fake(prompt, system, max_tokens=200):
        calls.append({"prompt": prompt, "system": system, "max_tokens": max_tokens})
        return value

    monkeypatch.setattr(tx, "_deepseek_text", fake)
    return calls


def teacher(headers_for, uid=3):
    return headers_for(uid, "teacher")


# ── niveaux d'un groupe ─────────────────────────────────────────────────────

def test_group_level_line_without_any_level():
    assert tx._group_level_line([], 1).startswith("Niveau non renseigné")
    assert tx._group_level_line(["", "  "], 3).startswith("Niveau non renseigné")


def test_group_level_line_with_a_single_level():
    assert tx._group_level_line(["Tle", " Tle ", "Tle"], 3) == "Niveau : Tle."


def test_group_level_line_with_mixed_levels_targets_the_most_frequent_one():
    line = tx._group_level_line(["3e", "3e", "Tle", "2nde", "3e"], 5)
    assert "5 élèves" in line and "3e, Tle, 2nde" in line and "niveau le plus fréquent (3e)" in line
    assert "approfondissement" in line


# ── participants d'une séance ───────────────────────────────────────────────

class ScriptedSession:
    """execute() rend les résultats dans l'ordre ; query() passe à la vraie base SQLite."""

    def __init__(self, real, *results):
        self.real, self.results, self.params = real, list(results), []

    def execute(self, statement, params=None):
        self.params.append(params)
        return self.results.pop(0)

    def query(self, *a):
        return self.real.query(*a)


def _one(row):
    return SimpleNamespace(fetchone=lambda: row)


def _many(*rows):
    return SimpleNamespace(fetchall=lambda: [(r,) for r in rows])


@pytest.fixture
def real_session(sqlite_database):
    db = sqlite_database(tx, [dbm.User])
    db.add(dbm.User(Id=11, Email="a@x.test", Level="Tle"), dbm.User(Id=12, Email="b@x.test", Level=" 3e "),
           dbm.User(Id=13, Email="c@x.test", Level=None))
    session = db.get_session()
    yield session
    session.close()


def test_unknown_booking_is_404(real_session):
    with pytest.raises(HTTPException) as err:
        tx._load_session_participants(ScriptedSession(real_session, _one(None)), 1, 3, False)
    assert err.value.status_code == 404


def test_only_the_tutor_of_the_session_or_an_admin_can_read_its_participants(real_session):
    with pytest.raises(HTTPException) as err:
        tx._load_session_participants(ScriptedSession(real_session, _one((11, 99))), 1, 3, False)
    assert err.value.status_code == 403
    admin = tx._load_session_participants(ScriptedSession(real_session, _one((11, 99)), _many()), 1, 3, True)
    assert [p["id"] for p in admin] == [11]


def test_participants_fall_back_on_the_booking_student_and_carry_their_level(real_session):
    result = tx._load_session_participants(ScriptedSession(real_session, _one((11, 3)), _many()), 5, 3, False)
    assert result == [{"id": 11, "level": "Tle"}]


def test_participants_are_deduplicated_limited_to_twenty_and_levels_trimmed(real_session):
    ids = [12, 12, 13, 999] + list(range(100, 130))
    result = tx._load_session_participants(ScriptedSession(real_session, _one((11, 3)), _many(*ids)), 5, 3, False)
    assert len(result) == 20 and result[0] == {"id": 12, "level": "3e"}
    assert result[1] == {"id": 13, "level": ""} and result[2] == {"id": 999, "level": ""}       # inconnu : niveau vide


# ── POST /teacher/session-material ──────────────────────────────────────────

URL_MATERIAL = "/api/teacher/session-material"
MATERIAL = {
    "title": "Les limites", "subject": "Maths", "level": "Tle", "objectives": ["Calculer", "", "Comprendre"],
    "course": {"introduction": "Intro", "sections": [{"title": f"S{i}", "content": "c", "example": "ex" if i == 0 else ""}
                                                      for i in range(9)] + [{"foo": 1}, "texte"],
               "summary": "Résumé", "keyPoints": [f"k{i}" for i in range(15)]},
    "quiz": [{"id": "q1", "text": "Q ?", "options": [{"id": "a", "text": "A"}], "correctOptionId": "a", "explanation": "E"}],
    "exercises": [{"id": "e1", "title": "Ex", "statement": "Calculer", "difficulty": "facile",
                   "hints": ["h1", "", "h2", "h3", "h4", "h5"], "solution": "S"}, {"title": "sans énoncé"}],
    "groupNote": "Adapté au groupe",
}


@pytest.fixture
def material(monkeypatch):
    state = SimpleNamespace(participants=[{"id": 11, "level": "Tle"}], signals={}, profile={}, loaded=[])
    monkeypatch.setattr(tx, "_load_session_participants", lambda session, booking, uid, admin: state.participants)
    monkeypatch.setattr(tx, "Database", lambda: SimpleNamespace(SessionLocal=lambda: SimpleNamespace(close=lambda: None)))

    def signals(uid):
        state.loaded.append(uid)
        return state.signals.get(uid, ("", []))

    monkeypatch.setattr(tx, "_load_student_generation_signals", signals)
    monkeypatch.setattr("routes.chatbot_routes._load_teacher_profile_data", lambda uid: state.profile)
    return state


def _material(client, headers_for, **body):
    return client.post(URL_MATERIAL, json={"booking_id": 5, "subject": "Maths", **body}, headers=teacher(headers_for))


def test_material_needs_a_token_the_right_role_and_a_booking(client, headers_for, material, monkeypatch):
    fake_json(monkeypatch, MATERIAL)
    assert client.post(URL_MATERIAL, json={"booking_id": 5, "subject": "x"}).status_code in (401, 403)
    assert client.post(URL_MATERIAL, json={"booking_id": 5, "subject": "x"}, headers=headers_for(1, "student")).status_code == 403
    assert client.post(URL_MATERIAL, json={"subject": "x"}, headers=teacher(headers_for)).status_code == 422


def test_material_without_any_subject_topic_or_instruction_is_422(client, headers_for, material, monkeypatch):
    calls = fake_json(monkeypatch, MATERIAL)
    r = client.post(URL_MATERIAL, json={"booking_id": 5, "topics": ["  ", ""], "subject": "  "}, headers=teacher(headers_for))
    assert r.status_code == 422 and "Aucun sujet" in r.json()["detail"] and calls == []


def test_material_booking_errors_are_passed_through(client, headers_for, monkeypatch):
    fake_json(monkeypatch, MATERIAL)
    monkeypatch.setattr(tx, "Database", lambda: SimpleNamespace(SessionLocal=lambda: SimpleNamespace(close=lambda: None)))

    def refuse(session, booking, uid, admin):
        raise HTTPException(status_code=403, detail="Cette séance ne vous appartient pas.")

    monkeypatch.setattr(tx, "_load_session_participants", refuse)
    assert _material(client, headers_for).status_code == 403


def test_material_admin_flag_is_forwarded_to_the_ownership_check(client, headers_for, material, monkeypatch):
    fake_json(monkeypatch, MATERIAL)
    seen = []
    monkeypatch.setattr(tx, "_load_session_participants", lambda s, b, uid, admin: seen.append((uid, admin)) or material.participants)
    client.post(URL_MATERIAL, json={"booking_id": 5, "subject": "x"}, headers=headers_for(1, "admin"))
    _material(client, headers_for)
    assert seen == [(1, True), (3, False)]


def test_material_shapes_and_limits_the_model_output(client, headers_for, material, monkeypatch):
    calls = fake_json(monkeypatch, MATERIAL)
    body = _material(client, headers_for, duration_minutes=60).json()
    assert body["title"] == "Les limites" and body["objectives"] == ["Calculer", "Comprendre"]
    sections = body["course"]["sections"]
    assert len(sections) == 5 and sections[0]["example"] == "ex" and sections[1]["example"] is None     # 3 + 2 de marge
    assert len(body["course"]["keyPoints"]) == 10 and body["course"]["summary"] == "Résumé"
    assert body["quiz"][0]["correctOptionId"] == "a"
    assert len(body["exercises"]) == 1 and body["exercises"][0]["hints"] == ["h1", "h2", "h3", "h4"]
    assert body["groupNote"] is None                                       # une seule personne : pas de note de groupe
    assert calls[0]["max_tokens"] == 4750 and "exactement 8 questions" in calls[0]["prompt"]


def test_material_group_session_gets_a_group_note_and_group_level_line(client, headers_for, material, monkeypatch):
    material.participants = [{"id": 11, "level": "3e"}, {"id": 12, "level": "Tle"}]
    calls = fake_json(monkeypatch, MATERIAL)
    body = _material(client, headers_for).json()
    assert body["groupNote"] == "Adapté au groupe"
    assert "aux niveaux variés" in calls[0]["prompt"] and "comment le support s'adapte au groupe" in calls[0]["prompt"]


def test_material_participant_count_comes_from_the_largest_of_booking_and_request(client, headers_for, material, monkeypatch):
    calls = fake_json(monkeypatch, MATERIAL)
    body = _material(client, headers_for, participant_count=4).json()
    assert body["groupNote"] == "Adapté au groupe" and "Laisse groupNote vide" not in calls[0]["prompt"]


def test_material_levels_fall_back_on_the_request_when_participants_have_none(client, headers_for, material, monkeypatch):
    material.participants = [{"id": 11, "level": ""}]
    calls = fake_json(monkeypatch, MATERIAL)
    _material(client, headers_for, levels=["2nde"])
    assert "Niveau : 2nde." in calls[0]["prompt"]


def test_material_prompt_carries_topics_mode_instructions_and_enrolled_subjects(client, headers_for, material, monkeypatch):
    material.signals = {11: ("STYLE", ["Maths", "SVT"])}
    material.profile = {"teaching_style": "Pédagogue patient"}
    calls = fake_json(monkeypatch, MATERIAL)
    _material(client, headers_for, topics=["Limites", " ", "Suites"] + [f"t{i}" for i in range(12)], mode="in_person",
              instructions="Insiste sur les exemples", duration_minutes=45)
    prompt = calls[0]["prompt"]
    assert "Limites, Suites, t0" in prompt and "t7" in prompt and "t8" not in prompt          # dix sujets au plus
    assert "Séance en présentiel." in prompt and "Consigne du tuteur : Insiste sur les exemples" in prompt
    assert "45 minutes" in prompt and "suivies par l’élève sur WinPlus : Maths, SVT." in prompt
    assert "[Style d'apprentissage de l'élève]\nSTYLE" in prompt and "« Pédagogue patient »" in prompt


def test_material_online_by_default_and_short_sessions_are_planned_for_thirty_minutes(client, headers_for, material, monkeypatch):
    calls = fake_json(monkeypatch, MATERIAL)
    _material(client, headers_for, duration_minutes=5)
    assert "Séance en ligne." in calls[0]["prompt"] and "30 minutes" in calls[0]["prompt"]


def test_material_group_with_one_shared_style_still_gets_the_generic_group_instruction(client, headers_for, material, monkeypatch):
    material.participants = [{"id": 11, "level": "Tle"}, {"id": 12, "level": "Tle"}]
    material.signals = {11: ("STYLE", []), 12: ("STYLE", [])}
    calls = fake_json(monkeypatch, MATERIAL)
    _material(client, headers_for)
    assert "[Styles d'apprentissage du groupe]" in calls[0]["prompt"] and "STYLE" not in calls[0]["prompt"].split("groupe]")[1][:5]


def test_material_reads_the_profile_of_six_students_at_most(client, headers_for, material, monkeypatch):
    material.participants = [{"id": i, "level": ""} for i in range(1, 11)]
    fake_json(monkeypatch, MATERIAL)
    _material(client, headers_for)
    assert material.loaded == [1, 2, 3, 4, 5, 6]


def test_material_survives_an_unavailable_tutor_profile(client, headers_for, material, monkeypatch):
    fake_json(monkeypatch, MATERIAL)

    def boom(uid):
        raise RuntimeError("profil HS")

    monkeypatch.setattr("routes.chatbot_routes._load_teacher_profile_data", boom)
    assert _material(client, headers_for).status_code == 200


@pytest.mark.parametrize("answer", [None, [], "texte"])
def test_material_unusable_model_answer_is_a_502(client, headers_for, material, monkeypatch, answer):
    fake_json(monkeypatch, answer)
    r = _material(client, headers_for)
    assert r.status_code == 502 and "support impossible" in r.json()["detail"]


def test_material_subject_defaults_to_the_requested_one(client, headers_for, material, monkeypatch):
    fake_json(monkeypatch, {"title": "T"})
    body = _material(client, headers_for, subject="Physique").json()
    assert body["subject"] == "Physique" and body["level"] is None and body["course"]["sections"] == []


# ── POST /teacher/coaching-report ───────────────────────────────────────────

URL_COACH = "/api/teacher/coaching-report"
COACH = {"strengths": ["Clarté", 2], "subjects_improving": ["Maths"], "stagnation_patterns": [],
         "recommendations": ["Varier"], "report_text": " Rapport. "}


def test_coaching_accepts_teacher_admin_and_the_monthly_service_token(client, headers_for, service_headers, monkeypatch):
    fake_json(monkeypatch, COACH)
    body = {"month_label": "mai 2026", "reviews": ["Super"]}
    assert client.post(URL_COACH, json=body).status_code in (401, 403)
    assert client.post(URL_COACH, json=body, headers=headers_for(1, "student")).status_code == 403
    assert client.post(URL_COACH, json=body, headers=headers_for(1, "teacher")).status_code == 200
    assert client.post(URL_COACH, json=body, headers=service_headers("ai.coaching-report")).status_code == 200
    assert client.post(URL_COACH, json=body, headers=service_headers("ai.task")).status_code in (401, 403)


def test_coaching_without_reviews_or_sessions_is_422(client, headers_for, monkeypatch):
    calls = fake_json(monkeypatch, COACH)
    r = client.post(URL_COACH, json={"month_label": "mai", "reviews": []}, headers=teacher(headers_for))
    assert r.status_code == 422 and "Pas assez de données" in r.json()["detail"] and calls == []
    assert client.post(URL_COACH, json={"reviews": []}, headers=teacher(headers_for)).status_code == 422


def test_coaching_report_and_prompt(client, headers_for, monkeypatch):
    calls = fake_json(monkeypatch, COACH)
    reviews = [f"avis{i} " + "x" * 500 for i in range(40)]
    body = client.post(URL_COACH, json={"month_label": "mai 2026", "reviews": reviews, "sessions_count": 12,
                                        "average_rating": 4.5, "subjects_taught": ["Maths"]},
                       headers=teacher(headers_for)).json()
    assert body["strengths"] == ["Clarté", "2"] and body["report_text"] == "Rapport."
    prompt = calls[0]["prompt"]
    assert "Période : mai 2026" in prompt and "Séances effectuées : 12" in prompt and "Note moyenne : 4.5" in prompt
    assert "avis29" in prompt and "avis30" not in prompt and "x" * 395 in prompt and "x" * 401 not in prompt


def test_coaching_with_sessions_but_no_review_says_so(client, headers_for, monkeypatch):
    calls = fake_json(monkeypatch, COACH)
    client.post(URL_COACH, json={"month_label": "mai", "reviews": [], "sessions_count": 3}, headers=teacher(headers_for))
    assert "aucun avis reçu" in calls[0]["prompt"] and "Note moyenne : N/A" in calls[0]["prompt"]


def test_coaching_model_failure_is_a_500(client, headers_for, monkeypatch):
    fake_json(monkeypatch, None)
    r = client.post(URL_COACH, json={"month_label": "mai", "reviews": ["x"]}, headers=teacher(headers_for))
    assert r.status_code == 500 and "rapport de coaching" in r.json()["detail"]


# ── messagerie ──────────────────────────────────────────────────────────────

URL_QUICK = "/api/messaging/quick-replies"


@pytest.mark.parametrize("role", ["student", "parent", "teacher", "admin"])
def test_quick_replies_are_open_to_every_authenticated_role(client, headers_for, monkeypatch, role):
    fake_json(monkeypatch, {"suggestions": ["Oui", "Non"]})
    assert client.post(URL_QUICK, json={"message_text": "Bonjour"}, headers=headers_for(1, role)).status_code == 200
    assert client.post(URL_QUICK, json={"message_text": "Bonjour"}).status_code in (401, 403)


def test_quick_replies_keep_three_suggestions_and_cut_the_message(client, headers_for, monkeypatch):
    calls = fake_json(monkeypatch, {"suggestions": ["a", "b", "c", "d", 5]})
    body = client.post(URL_QUICK, json={"message_text": "m" * 1200}, headers=headers_for(1, "student")).json()
    assert body == {"suggestions": ["a", "b", "c"]} and "m" * 1000 in calls[0]["prompt"] and "m" * 1001 not in calls[0]["prompt"]
    assert calls[0]["max_tokens"] == 300


def test_quick_replies_reject_a_too_short_message_without_calling_the_model(client, headers_for, monkeypatch):
    calls = fake_json(monkeypatch, {"suggestions": ["a"]})
    r = client.post(URL_QUICK, json={"message_text": " a "}, headers=headers_for(1, "student"))
    assert r.status_code == 422 and r.json()["detail"] == "Message trop court." and calls == []


@pytest.mark.parametrize("answer", [None, {}, {"suggestions": []}, ["a"]])
def test_quick_replies_model_failure_is_a_500(client, headers_for, monkeypatch, answer):
    fake_json(monkeypatch, answer)
    assert client.post(URL_QUICK, json={"message_text": "Bonjour"}, headers=headers_for(1, "student")).status_code == 500


URL_REPLY = "/api/messaging/generate-reply"


def test_long_reply_with_and_without_context(client, headers_for, monkeypatch):
    calls = fake_text(monkeypatch, "  Bonjour, merci.  ")
    body = client.post(URL_REPLY, json={"message_text": "m" * 2000, "context": "c" * 700}, headers=headers_for(1, "parent")).json()
    assert body == {"reply_text": "Bonjour, merci."}
    prompt = calls[0]["prompt"]
    assert "m" * 1500 in prompt and "m" * 1501 not in prompt and "c" * 500 in prompt and "c" * 501 not in prompt
    assert calls[0]["max_tokens"] == 350
    client.post(URL_REPLY, json={"message_text": "Salut"}, headers=headers_for(1, "parent"))
    assert "Contexte additionnel" not in calls[1]["prompt"]


def test_long_reply_errors(client, headers_for, monkeypatch):
    fake_text(monkeypatch, "")
    r = client.post(URL_REPLY, json={"message_text": "Salut"}, headers=headers_for(1, "parent"))
    assert r.status_code == 500 and "réponse impossible" in r.json()["detail"]
    assert client.post(URL_REPLY, json={}, headers=headers_for(1, "parent")).status_code == 422
    assert client.post(URL_REPLY, json={"message_text": "x"}).status_code in (401, 403)


URL_PARENT = "/api/messaging/parent-report"


def test_parent_report_is_for_teachers_only(client, headers_for, monkeypatch):
    fake_text(monkeypatch, "Chers parents")
    body = {"student_name": "Awa", "summary_text": "Awa progresse bien en maths."}
    assert client.post(URL_PARENT, json=body).status_code in (401, 403)
    for role in ("student", "parent"):
        assert client.post(URL_PARENT, json=body, headers=headers_for(1, role)).status_code == 403
    assert client.post(URL_PARENT, json=body, headers=teacher(headers_for)).json() == {"report_text": "Chers parents"}


def test_parent_report_validation_and_cut(client, headers_for, monkeypatch):
    calls = fake_text(monkeypatch, "ok")
    short = client.post(URL_PARENT, json={"student_name": "A", "summary_text": "  court  "}, headers=teacher(headers_for))
    assert short.status_code == 422 and calls == []
    client.post(URL_PARENT, json={"student_name": "A", "summary_text": "s" * 900}, headers=teacher(headers_for))
    assert "s" * 800 in calls[0]["prompt"] and "s" * 801 not in calls[0]["prompt"] and calls[0]["max_tokens"] == 400
    fake_text(monkeypatch, "")
    assert client.post(URL_PARENT, json={"student_name": "A", "summary_text": "s" * 20},
                       headers=teacher(headers_for)).status_code == 500


URL_RELAUNCH = "/api/teacher/inactivity-relaunch-message"


def test_relaunch_message(client, headers_for, monkeypatch):
    calls = fake_text(monkeypatch, " On t'attend ! ")
    r = client.post(URL_RELAUNCH, json={"course_title": "Algèbre"}, headers=teacher(headers_for))
    assert r.json() == {"message_text": "On t'attend !"} and "« Algèbre »" in calls[0]["prompt"]
    fake_text(monkeypatch, "")
    assert client.post(URL_RELAUNCH, json={"course_title": "x"}, headers=teacher(headers_for)).status_code == 500
    assert client.post(URL_RELAUNCH, json={}, headers=teacher(headers_for)).status_code == 422
    assert client.post(URL_RELAUNCH, json={"course_title": "x"}, headers=headers_for(1, "student")).status_code == 403


# ── détection de décrochage ─────────────────────────────────────────────────

URL_DROP = "/api/winai/detection-decrochage"


@pytest.mark.parametrize("scores,expected", [
    ([], False), ([50], False), ([80, 60], True), ([80, 70], True), ([80, 71], False), ([90, 80, 50], True),
    ([50, 50, 50, 50], False), ([40, 40, 80, 80], False), ([80, 80, 60, 60], True)])
def test_score_decline_compares_the_two_halves(scores, expected):
    assert tx._score_en_baisse(scores) is expected


def _drop(client, service_headers, students):
    return client.post(URL_DROP, json={"course_title": "Algèbre", "students": students},
                       headers=service_headers("ai.decrochage")).json()["alerts"]


def test_dropout_detection_only_accepts_the_background_service_token(client, headers_for, service_headers):
    body = {"course_title": "x", "students": []}
    assert client.post(URL_DROP, json=body).status_code in (401, 403)
    for role in ("teacher", "admin", "student"):
        assert client.post(URL_DROP, json=body, headers=headers_for(1, role)).status_code in (401, 403)
    assert client.post(URL_DROP, json=body, headers=service_headers("ai.task")).status_code in (401, 403)
    assert client.post(URL_DROP, json=body, headers=service_headers("ai.decrochage")).status_code == 200


def test_dropout_levels_and_signals(client, service_headers):
    alerts = _drop(client, service_headers, [
        {"user_id": 1, "name": "Actif", "days_inactive": 6, "recent_quiz_scores": [90, 10]},          # trop récent : ignoré
        {"user_id": 2, "name": "Faible", "days_inactive": 7},
        {"user_id": 3, "name": "Élevé", "days_inactive": 14},
        {"user_id": 4, "name": "Baisse", "days_inactive": 8, "recent_quiz_scores": [80, 60]},
        {"user_id": 5, "name": "Stable", "days_inactive": 10, "recent_quiz_scores": [60, 60]},
    ])
    assert [(a["user_id"], a["niveau"]) for a in alerts] == [(2, "faible"), (3, "eleve"), (4, "eleve"), (5, "faible")]
    assert alerts[0]["signaux"] == ["inactif depuis 7 jours"]
    assert alerts[2]["signaux"] == ["inactif depuis 8 jours", "scores de quiz en baisse"]


def test_dropout_with_no_student_or_bad_input(client, service_headers):
    assert _drop(client, service_headers, []) == []
    r = client.post(URL_DROP, json={"course_title": "x", "students": [{"user_id": 1, "name": "A"}]},
                    headers=service_headers("ai.decrochage"))
    assert r.status_code == 422


# ── POST /teacher/generate-syllabus ─────────────────────────────────────────

URL_SYLLABUS = "/api/teacher/generate-syllabus"
WEEK = {"week": 1, "title": " Intro ", "concepts": ["a", 2], "activities": ["b"], "duration_minutes": 90,
        "suggested_resources": ["manuel"]}


def _syllabus(client, headers_for, **body):
    return client.post(URL_SYLLABUS, json={"subject": "Maths", "level": "Tle", "duration_weeks": 4, **body}, headers=teacher(headers_for))


def test_syllabus_validation_and_roles(client, headers_for, monkeypatch):
    fake_json(monkeypatch, {"weeks": [WEEK]})
    assert client.post(URL_SYLLABUS, json={"subject": "x"}, headers=teacher(headers_for)).status_code == 422
    assert client.post(URL_SYLLABUS, json={"subject": "x", "level": "y", "duration_weeks": 2}).status_code in (401, 403)
    assert _syllabus(client, headers_for).status_code == 200


def test_syllabus_returns_cleaned_weeks_and_scales_the_token_budget(client, headers_for, monkeypatch):
    calls = fake_json(monkeypatch, {"weeks": [WEEK]})
    body = _syllabus(client, headers_for, duration_weeks=12, objectives=["Réussir"], target_exams=["BAC"]).json()
    assert body["weeks"] == [{"week": 1, "title": "Intro", "concepts": ["a", "2"], "activities": ["b"],
                              "duration_minutes": 90, "suggested_resources": ["manuel"]}]
    assert calls[0]["max_tokens"] == 230 * 12 + 500 and "12 semaines" in calls[0]["prompt"]
    assert "Réussir" in calls[0]["prompt"] and "BAC" in calls[0]["prompt"]


@pytest.mark.parametrize("asked,weeks,tokens", [(0, 1, 730), (-5, 1, 730), (24, 24, 6020), (100, 24, 6020)])
def test_syllabus_duration_is_kept_between_one_and_twenty_four_weeks(client, headers_for, monkeypatch, asked, weeks, tokens):
    calls = fake_json(monkeypatch, {"weeks": [WEEK]})
    _syllabus(client, headers_for, duration_weeks=asked)
    assert f"Durée : {weeks} semaines" in calls[0]["prompt"] and calls[0]["max_tokens"] == tokens


def test_syllabus_keeps_only_the_requested_number_of_weeks_and_skips_junk(client, headers_for, monkeypatch):
    fake_json(monkeypatch, {"weeks": ["texte", {"title": "A", "duration_minutes": 0}, {"title": "B"}, {"title": "C"}]})
    weeks = _syllabus(client, headers_for, duration_weeks=3).json()["weeks"]
    assert [w["title"] for w in weeks] == ["A", "B"] and [w["week"] for w in weeks] == [1, 2]       # "C" dépasse 3 éléments
    assert weeks[0]["duration_minutes"] == 60                                # 0 ramené à 60


@pytest.mark.parametrize("answer", [None, {}, {"weeks": []}, {"weeks": ["x"]}, [WEEK]])
def test_syllabus_model_failure_is_a_500(client, headers_for, monkeypatch, answer):
    fake_json(monkeypatch, answer)
    r = _syllabus(client, headers_for)
    assert r.status_code == 500 and "syllabus impossible" in r.json()["detail"]
