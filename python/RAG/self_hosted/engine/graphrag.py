"""
GraphRAG — graphe de connaissances pour les requêtes multi-hop (Phase 3,
§3.7). Construit un graphe d'entités/relations pendant l'ingestion, détecte
des communautés par l'algorithme de Leiden, et l'exploite en génération pour
les requêtes classées COMPLEXE via un parcours BFS borné à 2 sauts.

`networkx` (structure de graphe) et `python-igraph`/`leidenalg` (Leiden) sont
des librairies d'algorithmique de graphe classique, pas des frameworks ML —
même statut que Qdrant ou rank_bm25 dans l'arbitrage "PyTorch uniquement".
"""

from __future__ import annotations

import json
import logging
from dataclasses import dataclass
from typing import Dict, List, Tuple

import networkx as nx

from RAG.self_hosted.engine.llm_utils import generate
from RAG.self_hosted.models.loader import get_llm_simple

logger = logging.getLogger(__name__)

_ENTITY_TYPES = ["notion", "matiere", "niveau", "examen", "procedure", "document", "exercice"]
_RELATION_TYPES = ["definit", "remplace", "reference", "exception_de", "applicable_a", "prerequis_de"]

_EXTRACTION_SYSTEM = (
    "Tu extrais les entités et relations normatives/pédagogiques d'un texte. "
    f"Types d'entités autorisés : {_ENTITY_TYPES}. "
    f"Types de relations autorisés : {_RELATION_TYPES}. "
    'Réponds en JSON strict : {"triples": [{"subject": "...", "relation": "...", "object": "..."}]}. '
    "Si aucune relation claire n'est identifiable, réponds {\"triples\": []}."
)


@dataclass
class Triple:
    subject: str
    relation: str
    obj: str
    doc_id: str


def extract_triples(text: str, doc_id: str) -> List[Triple]:
    tokenizer, model = get_llm_simple()
    raw = generate(tokenizer, model, _EXTRACTION_SYSTEM, text[:3000], max_new_tokens=500)
    raw = raw.strip().strip("`")
    if raw.lower().startswith("json"):
        raw = raw[4:]
    try:
        data = json.loads(raw)
    except json.JSONDecodeError:
        logger.warning("[RAG/self_hosted/graphrag] Extraction JSON invalide, ignorée.")
        return []

    return [
        Triple(subject=t["subject"], relation=t["relation"], obj=t["object"], doc_id=doc_id)
        for t in data.get("triples", [])
        if t.get("subject") and t.get("object")
    ]


def build_graph(triples: List[Triple]) -> nx.DiGraph:
    graph = nx.DiGraph()
    for t in triples:
        graph.add_node(t.subject)
        graph.add_node(t.obj)
        graph.add_edge(t.subject, t.obj, relation=t.relation, doc_id=t.doc_id)
    return graph


def detect_communities(graph: nx.DiGraph) -> Dict[str, int]:
    if graph.number_of_nodes() == 0:
        return {}
    try:
        import igraph as ig
        import leidenalg
    except ImportError:
        logger.warning("[RAG/self_hosted/graphrag] python-igraph/leidenalg absents — communautés non calculées.")
        return {node: 0 for node in graph.nodes}

    node_list = list(graph.nodes)
    index = {n: i for i, n in enumerate(node_list)}
    edges = [(index[u], index[v]) for u, v in graph.edges]

    ig_graph = ig.Graph(n=len(node_list), edges=edges, directed=True)
    partition = leidenalg.find_partition(ig_graph, leidenalg.ModularityVertexPartition)

    return {node_list[i]: community_id for community_id, community in enumerate(partition) for i in community}


def summarize_community(graph: nx.DiGraph, nodes: List[str]) -> str:
    subgraph = graph.subgraph(nodes)
    facts = [f"{u} --{d['relation']}--> {v}" for u, v, d in subgraph.edges(data=True)]
    tokenizer, model = get_llm_simple()
    system = "Résume en 2-3 phrases factuelles la thématique commune de ces relations, pour indexation."
    return generate(tokenizer, model, system, "\n".join(facts), max_new_tokens=200)


def bfs_related_docs(graph: nx.DiGraph, entities: List[str], max_hops: int = 2) -> List[Tuple[str, str]]:
    """Retourne les paires (relation, doc_id) atteintes par parcours BFS
    borné depuis les entités de la requête — contexte structuré fourni au
    reranker en complément de la recherche vectorielle."""
    results: List[Tuple[str, str]] = []
    seen = set()

    for entity in entities:
        if entity not in graph:
            continue
        for node, depth in nx.single_source_shortest_path_length(graph.to_undirected(), entity, cutoff=max_hops).items():
            for _, target, data in graph.out_edges(node, data=True):
                key = (data["relation"], data["doc_id"])
                if key not in seen:
                    seen.add(key)
                    results.append(key)

    return results
