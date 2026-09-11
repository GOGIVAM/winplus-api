from RAG.self_hosted.engine.complexity_router import classify_complexity


def test_simple_single_fact_question():
    q = "Quel est le tarif horaire du répétiteur ?"
    assert classify_complexity(q) == "simple"


def test_complex_question_with_connector():
    q = "Un élève en période d'essai est-il soumis aux mêmes obligations, contrairement à un élève titulaire ?"
    assert classify_complexity(q) == "complex"


def test_complex_question_with_many_entities():
    q = "Quelle est la différence entre le Baccalauréat C et le Baccalauréat D pour l'ENSP Yaoundé ?"
    assert classify_complexity(q) == "complex"


def test_simple_short_question_no_entities():
    q = "combien coûte ce cours ?"
    assert classify_complexity(q) == "simple"
