"""Schémas Pydantic du service (schemas.py) : bornes, valeurs par défaut, alias des références .NET."""

import pytest
from pydantic import ValidationError

import schemas as s


# ── énumérations ────────────────────────────────────────────────────────────

def test_user_roles_include_institution_and_legacy_organization():
    assert {r.value for r in s.UserRole} == {"student", "teacher", "parent", "admin", "institution", "organization"}


def test_learning_styles_are_the_four_vark_values():
    assert {v.value for v in s.LearningStyle} == {"visual", "auditory", "reading_writing", "kinesthetic"}


# ── contenus et sujets ──────────────────────────────────────────────────────

def test_course_content_defaults_and_bounds():
    c = s.CourseContentBase(title="Leçon 1")
    assert (c.order_index, c.duration_minutes, c.is_locked) == (0, 0, True)
    with pytest.raises(ValidationError):
        s.CourseContentBase(title="")
    with pytest.raises(ValidationError):
        s.CourseContentBase(title="x" * 256)
    with pytest.raises(ValidationError):
        s.CourseContentBase(title="ok", order_index=-1)
    with pytest.raises(ValidationError):
        s.CourseContentBase(title="ok", description="d" * 2001)


def test_subject_create_defaults_and_price_cannot_be_negative():
    sub = s.SubjectCreate(title="Maths")
    assert (sub.price, sub.is_published, sub.is_featured) == (0, False, False)
    with pytest.raises(ValidationError):
        s.SubjectCreate(title="Maths", price=-1)


def test_subject_update_accepts_a_partial_body_and_validates_present_fields():
    assert s.SubjectUpdate().model_dump(exclude_none=True) == {}
    assert s.SubjectUpdate(price=0).price == 0
    with pytest.raises(ValidationError):
        s.SubjectUpdate(title="")
    with pytest.raises(ValidationError):
        s.SubjectUpdate(price=-0.5)


# ── utilisateurs ────────────────────────────────────────────────────────────

def test_user_create_validates_email_and_password_length():
    u = s.UserCreate(email="a@example.com", password="12345678")
    assert u.role == s.UserRole.student
    with pytest.raises(ValidationError):
        s.UserCreate(email="pas-un-email", password="12345678")
    with pytest.raises(ValidationError):
        s.UserCreate(email="a@example.com", password="1234567")
    with pytest.raises(ValidationError):
        s.UserCreate(email="a@example.com", password="12345678", role="superadmin")


def test_enrollment_progress_is_a_percentage():
    assert s.EnrollmentBase(user_id=1, subject_id=2, progress_percentage=100).progress_percentage == 100
    for bad in (-1, 100.1):
        with pytest.raises(ValidationError):
            s.EnrollmentBase(user_id=1, subject_id=2, progress_percentage=bad)


# ── recommandations et analyse ──────────────────────────────────────────────

def test_recommendation_request_limit_bounds():
    assert s.RecommendationRequest(user_id=1).limit == 5
    assert s.RecommendationRequest(user_id=1, limit=20).limit == 20
    for bad in (0, 21):
        with pytest.raises(ValidationError):
            s.RecommendationRequest(user_id=1, limit=bad)


def test_analysis_request_needs_text():
    with pytest.raises(ValidationError):
        s.AnalysisRequest(text="")
    assert s.AnalysisRequest(text="x").metadata is None


def test_recommendations_list_default_data_source():
    assert s.RecommendationsListResponse(success=True, count=0, recommendations=[]).data_source == "content_based"


# ── chat ────────────────────────────────────────────────────────────────────

@pytest.mark.parametrize("role", ["user", "assistant", "system"])
def test_chat_message_accepts_known_roles(role):
    assert s.ChatMessage(role=role, content="salut").role == role


@pytest.mark.parametrize("role", ["", "bot", "User", "tool"])
def test_chat_message_refuses_other_roles(role):
    with pytest.raises(ValidationError):
        s.ChatMessage(role=role, content="salut")


def test_chat_message_content_is_required_and_non_empty():
    with pytest.raises(ValidationError):
        s.ChatMessage(role="user", content="")
    with pytest.raises(ValidationError):
        s.ChatMessage(role="user")


def test_chat_request_token_and_temperature_bounds():
    msg = [{"role": "user", "content": "q"}]
    r = s.ChatRequest(messages=msg)
    assert (r.max_tokens, r.temperature, r.knowledge_base, r.client_message_id) == (2000, 0.7, False, None)
    assert s.ChatRequest(messages=msg, max_tokens=100, temperature=0).max_tokens == 100
    assert s.ChatRequest(messages=msg, max_tokens=4000, temperature=1.0).temperature == 1.0
    for kwargs in ({"max_tokens": 99}, {"max_tokens": 4001}, {"temperature": -0.1}, {"temperature": 1.1}):
        with pytest.raises(ValidationError):
            s.ChatRequest(messages=msg, **kwargs)


def test_chat_request_requires_messages():
    with pytest.raises(ValidationError):
        s.ChatRequest()


# ── références .NET (alias PascalCase) ──────────────────────────────────────

@pytest.mark.parametrize("payload", [
    {"id": 3, "title": "Maths"}, {"SubjectId": 3, "Title": "Maths"}, {"subject_id": 3, "title": "Maths"},
])
def test_enrolled_subject_ref_accepts_snake_case_and_pascal_case(payload):
    ref = s.EnrolledSubjectRef(**payload)
    assert (ref.id, ref.title) == (3, "Maths")


@pytest.mark.parametrize("payload", [{"id": 4, "title": "Cours"}, {"CourseId": 4, "Title": "Cours"},
                                     {"course_id": 4, "title": "Cours"}])
def test_enrolled_course_ref_accepts_the_same_variants(payload):
    ref = s.EnrolledCourseRef(**payload)
    assert (ref.id, ref.title) == (4, "Cours")


def test_enrolled_refs_require_id_and_title():
    with pytest.raises(ValidationError):
        s.EnrolledSubjectRef(title="Maths")
    with pytest.raises(ValidationError):
        s.EnrolledCourseRef(id=1)


def test_chatbot_context_defaults_and_nested_refs():
    ctx = s.ChatbotContextRequest()
    assert ctx.role == "student" and ctx.enrolled_subjects == [] and ctx.child_ids == []
    ctx = s.ChatbotContextRequest(enrolled_subjects=[{"SubjectId": 1, "Title": "A"}],
                                  enrolled_courses=[{"id": 2, "title": "B"}],
                                  performance_history={"Maths": 14.5})
    assert ctx.enrolled_subjects[0].id == 1 and ctx.enrolled_courses[0].title == "B"
    assert ctx.performance_history == {"Maths": 14.5}


def test_chatbot_context_refuses_a_non_numeric_score():
    with pytest.raises(ValidationError):
        s.ChatbotContextRequest(performance_history={"Maths": "beaucoup"})


# ── quiz adaptatif ──────────────────────────────────────────────────────────

def test_adaptive_quiz_request_defaults_and_bounds():
    r = s.AdaptiveQuizRequest(user_id=1)
    assert (r.count, r.mode, r.subject, r.attempts_count, r.language) == (8, "consolidation", "", 0, None)
    assert r.weak_topics == [] and r.strong_topics == []
    for count in (3, 20):
        assert s.AdaptiveQuizRequest(user_id=1, count=count).count == count
    for count in (2, 21):
        with pytest.raises(ValidationError):
            s.AdaptiveQuizRequest(user_id=1, count=count)


def test_adaptive_quiz_recent_score_is_a_percentage():
    assert s.AdaptiveQuizRequest(user_id=1, recent_score=0).recent_score == 0
    assert s.AdaptiveQuizRequest(user_id=1, recent_score=100).recent_score == 100
    for bad in (-1, 101):
        with pytest.raises(ValidationError):
            s.AdaptiveQuizRequest(user_id=1, recent_score=bad)


def test_adaptive_quiz_list_defaults_are_not_shared_between_instances():
    a, b = s.AdaptiveQuizRequest(user_id=1), s.AdaptiveQuizRequest(user_id=2)
    a.weak_topics.append("fractions")
    assert b.weak_topics == []


def test_adaptive_quiz_question_requires_every_field():
    q = dict(id=1, question="?", options=["A", "B"], correct_answer="A", explanation="e", difficulty="facile", topic="t")
    assert s.AdaptiveQuizQuestion(**q).id == 1
    for missing in q:
        with pytest.raises(ValidationError):
            s.AdaptiveQuizQuestion(**{k: v for k, v in q.items() if k != missing})


def test_adaptive_quiz_response_default_mode():
    r = s.AdaptiveQuizResponse(success=True, subject="Maths", weak_areas=[], questions=[], count=0, generated_at="x")
    assert r.mode == "consolidation"


# ── style d'apprentissage, parcours, prédiction ─────────────────────────────

def test_learning_style_request_nests_answers():
    r = s.LearningStyleRequest(answers=[{"question_id": "q1", "style_tag": "V"}])
    assert r.answers[0].style_tag == "V"
    with pytest.raises(ValidationError):
        s.LearningStyleRequest(answers=[{"question_id": "q1"}])


def test_learning_path_response_round_trip():
    payload = {
        "success": True, "user_id": 1, "learning_velocity": 2.5, "total_duration_days": 30, "estimated_end_date": "2026-04-01",
        "phases": [{"phase": 1, "name": "P1", "duration_days": 10, "focus_areas": ["a"], "difficulty": "facile",
                    "target_completion": 60.0, "actions": ["x"]}],
        "recommendations": {"daily_study_time": "45 min", "focus_areas": [], "growth_areas": []},
        "generated_at": "2026-03-01T00:00:00",
    }
    assert s.LearningPathResponse(**payload).model_dump() == payload


def test_success_prediction_response_requires_factors_lists():
    base = dict(success=True, user_id=1, probability=50, level="medium", context={}, computed_at="x")
    r = s.SuccessPredictionResponse(positive_factors=[{"label": "l", "impact": "positive", "detail": "d"}],
                                    negative_factors=[], **base)
    assert r.positive_factors[0].impact == "positive"
    with pytest.raises(ValidationError):
        s.SuccessPredictionResponse(**base)


def test_progress_analysis_optional_fields_default():
    overview = dict(total_enrolled_subjects=1, completed_subjects=0, completion_rate=0.0, enrolled_days=1,
                    total_learning_time_hours=0.0, average_session_duration_minutes=0.0)
    r = s.ProgressAnalysisResponse(success=True, user_id=1, overview=overview, analysis={}, recommendations=[])
    assert r.overview.avg_score is None and r.history == [] and r.strong_areas == [] and r.estimated_completion_date is None


def test_generic_api_responses():
    assert s.ApiResponse(success=True).data is None
    assert s.PaginatedResponse(success=True, count=1, data=[{"a": 1}]).count == 1
    with pytest.raises(ValidationError):
        s.PaginatedResponse(success=True, count=1, data=[1])
