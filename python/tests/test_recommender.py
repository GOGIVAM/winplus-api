"""Recommandations (models/recommender.py) : similarité de contenu, populaires, tendances, score
personnalisé (catégorie, mise en avant, style d'apprentissage). La base est remplacée par un faux objet."""

import pandas as pd
import pytest

from models.recommender import Recommender


def _subject(id, title, category, description="", count=0, featured=False, rating=4.0, price=0.0):
    return {"id": id, "title": title, "description": description, "category": category, "thumbnailUrl": None,
            "price": price, "averageRating": rating, "totalRatings": 1, "enrollmentCount": count,
            "isFeatured": featured}


class FakeDb:
    def __init__(self, subjects=(), enrollments=(), style=None, formats=None, fail=False):
        self.subjects = list(subjects)
        self.enrollments = list(enrollments)
        self.style = style
        self.formats = formats or {}
        self.fail = fail

    def get_all_subjects(self, filters=None):
        if self.fail:
            raise RuntimeError("base indisponible")
        return list(self.subjects)

    def get_popular_subjects(self, limit=10):
        if self.fail:
            raise RuntimeError("base indisponible")
        return sorted(self.subjects, key=lambda s: -s["enrollmentCount"])[:limit]

    def get_user_enrollments(self, user_id):
        if self.fail:
            raise RuntimeError("base indisponible")
        return self.enrollments

    def get_user_learning_style(self, user_id):
        return self.style

    def get_subjects_format_map(self):
        return self.formats


CATALOGUE = [
    _subject(1, "Algèbre", "Maths", "equations algebre lineaires", count=10),
    _subject(2, "Fonctions", "Maths", "algebre fonctions equations", count=120, featured=True),
    _subject(3, "Histoire", "Lettres", "guerre empire histoire", count=49),
    _subject(4, "Géométrie", "Maths", "triangles cercles", count=0),
]


# ── load_subjects ───────────────────────────────────────────────────────────

def test_load_subjects_builds_frame_and_vectors():
    rec = Recommender(FakeDb(CATALOGUE))
    rec.load_subjects()
    assert list(rec.subjects_df["id"]) == [1, 2, 3, 4]
    assert rec.subject_vectors.shape[0] == 4


def test_load_subjects_handles_missing_text_fields():
    rec = Recommender(FakeDb([_subject(1, "Seul", None, description=None)]))
    rec.load_subjects()
    assert rec.subject_vectors.shape[0] == 1


def test_load_subjects_empty_catalogue_gives_empty_frame():
    rec = Recommender(FakeDb([]))
    rec.load_subjects()
    assert rec.subjects_df.empty


def test_load_subjects_database_error_gives_empty_frame():
    rec = Recommender(FakeDb(fail=True))
    rec.load_subjects()
    assert rec.subjects_df.empty


# ── get_similar_subjects ────────────────────────────────────────────────────

def test_similar_subjects_are_ranked_by_content_and_exclude_self():
    rec = Recommender(FakeDb(CATALOGUE))
    result = rec.get_similar_subjects(1, top_n=2)
    assert result[0] == 2          # partage « algebre » et « equations »
    assert 1 not in result and len(result) == 2


def test_similar_subjects_unknown_id_or_empty_catalogue():
    assert Recommender(FakeDb(CATALOGUE)).get_similar_subjects(999) == []
    assert Recommender(FakeDb([])).get_similar_subjects(1) == []


def test_similar_subjects_database_error_is_absorbed():
    assert Recommender(FakeDb(fail=True)).get_similar_subjects(1) == []


# ── populaires, catégorie, tendances ────────────────────────────────────────

def test_popular_subjects_filter_by_category_and_limit():
    rec = Recommender(FakeDb(CATALOGUE))
    assert rec.get_popular_subjects(limit=2) == [2, 3]
    assert rec.get_popular_subjects(category="Maths", limit=10) == [2, 1, 4]
    assert rec.get_popular_subjects(category="Inconnue") == []


def test_popular_subjects_error_gives_empty_list():
    assert Recommender(FakeDb(fail=True)).get_popular_subjects() == []


def test_recommend_by_category_sorts_by_enrollment():
    rec = Recommender(FakeDb(CATALOGUE))
    assert rec.recommend_by_category("Maths") == [2, 1, 4]
    assert rec.recommend_by_category("Maths", limit=1) == [2]
    assert rec.recommend_by_category("Inconnue") == []


def test_recommend_by_category_with_no_data_is_empty():
    assert Recommender(FakeDb([])).recommend_by_category("Maths") == []


def test_trending_subjects_are_the_most_enrolled():
    rec = Recommender(FakeDb(CATALOGUE))
    assert rec.get_trending_subjects(limit=2) == [2, 3]
    assert Recommender(FakeDb([])).get_trending_subjects() == []


# ── recommandations personnalisées ──────────────────────────────────────────

def _enrollment(subject_id, category):
    return {"subject_id": subject_id, "subject_category": category}


def test_without_enrollment_falls_back_to_popular_with_zero_score():
    rec = Recommender(FakeDb(CATALOGUE, enrollments=[]))
    result = rec.get_personalized_recommendations(5, limit=2)
    assert [r["id"] for r in result] == [2, 3]
    assert all(r["score"] == 0.0 and r["reason"] == "populaire" for r in result)


def test_personalized_scores_category_feature_and_popularity():
    rec = Recommender(FakeDb(CATALOGUE, enrollments=[_enrollment(1, "Maths")]))
    result = rec.get_personalized_recommendations(5, limit=10)
    by_id = {r["id"]: r for r in result}
    assert 1 not in by_id                               # déjà inscrit
    # sujet 2 : catégorie préférée (3) + mis en avant (2) + 120 // 50 = 2
    assert by_id[2]["score"] == 7.0
    assert by_id[2]["reason"] == "catégorie préférée, mis en avant"
    # sujet 4 : catégorie préférée seulement
    assert by_id[4]["score"] == 3.0 and by_id[4]["reason"] == "catégorie préférée"
    # sujet 3 : 49 // 50 = 0, aucune raison particulière
    assert by_id[3]["score"] == 0.0 and by_id[3]["reason"] == "populaire"
    assert [r["id"] for r in result] == [2, 4, 3]


def test_personalized_respects_limit():
    rec = Recommender(FakeDb(CATALOGUE, enrollments=[_enrollment(1, "Maths")]))
    assert [r["id"] for r in rec.get_personalized_recommendations(5, limit=1)] == [2]


def test_personalized_returns_empty_when_everything_is_enrolled():
    rec = Recommender(FakeDb(CATALOGUE[:1], enrollments=[_enrollment(1, "Maths")]))
    assert rec.get_personalized_recommendations(5) == []


@pytest.mark.parametrize("style,expected_bonus", [
    ("visual", 1.5), ("auditory", 0.5), ("kinesthetic", 0.0), ("reading_writing", 0.0),
])
def test_learning_style_boosts_subjects_with_videos(style, expected_bonus):
    formats = {3: {"video_count": 2, "doc_count": 0}}
    rec = Recommender(FakeDb(CATALOGUE, enrollments=[_enrollment(1, "Maths")], style=style, formats=formats))
    by_id = {r["id"]: r for r in rec.get_personalized_recommendations(5, limit=10)}
    assert by_id[3]["score"] == expected_bonus
    assert f"adapté au style {style}" in by_id[3]["reason"]


def test_learning_style_boosts_subjects_with_documents():
    formats = {3: {"video_count": 0, "doc_count": 4}}
    rec = Recommender(FakeDb(CATALOGUE, enrollments=[_enrollment(1, "Maths")], style="reading_writing",
                             formats=formats))
    by_id = {r["id"]: r for r in rec.get_personalized_recommendations(5, limit=10)}
    assert by_id[3]["score"] == 1.5


def test_unknown_style_gives_no_boost_but_keeps_reason():
    formats = {3: {"video_count": 1, "doc_count": 1}}
    rec = Recommender(FakeDb(CATALOGUE, enrollments=[_enrollment(1, "Maths")], style="mixte", formats=formats))
    by_id = {r["id"]: r for r in rec.get_personalized_recommendations(5, limit=10)}
    assert by_id[3]["score"] == 0.0 and "adapté au style mixte" in by_id[3]["reason"]


def test_personalized_adds_content_similarity_when_vectors_are_loaded():
    rec = Recommender(FakeDb(CATALOGUE, enrollments=[_enrollment(1, "Lettres")]))
    rec.load_subjects()
    by_id = {r["id"]: r for r in rec.get_personalized_recommendations(5, limit=10)}
    # sujet 2 : mis en avant (2) + 120 // 50 (2) = 4 de règles fixes, plus la similarité de contenu avec le sujet 1
    assert by_id[2]["score"] > 4.0
    # sujet 4 n'a aucun mot en commun avec le sujet 1 : aucune règle ne s'applique
    assert by_id[4]["score"] == 0.0


def test_personalized_database_error_gives_empty_list():
    assert Recommender(FakeDb(fail=True)).get_personalized_recommendations(5) == []


def test_empty_dataframe_does_not_disable_scoring():
    rec = Recommender(FakeDb(CATALOGUE, enrollments=[_enrollment(1, "Maths")]))
    rec.subjects_df = pd.DataFrame()
    assert rec.get_personalized_recommendations(5, limit=1)[0]["id"] == 2
