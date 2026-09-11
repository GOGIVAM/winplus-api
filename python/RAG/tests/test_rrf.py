from RAG.shared.rrf import reciprocal_rank_fusion


def test_rrf_favors_item_ranked_high_in_both_lists():
    dense = ["a", "b", "c", "d"]
    sparse = ["b", "a", "d", "c"]
    fused = reciprocal_rank_fusion([dense, sparse])
    ids = [item for item, _ in fused]
    # "a" et "b" occupent les rangs 1-2 dans les deux listes : ils doivent
    # dominer "c" et "d", classés plus bas partout.
    assert set(ids[:2]) == {"a", "b"}


def test_rrf_item_only_in_one_list_still_scored():
    fused = reciprocal_rank_fusion([["a", "b"], ["c"]])
    ids = {item for item, _ in fused}
    assert ids == {"a", "b", "c"}


def test_rrf_empty_lists():
    assert reciprocal_rank_fusion([[], []]) == []


def test_rrf_scores_strictly_decrease_with_rank_alone():
    fused = reciprocal_rank_fusion([["a", "b", "c"]])
    scores = [s for _, s in fused]
    assert scores == sorted(scores, reverse=True)
