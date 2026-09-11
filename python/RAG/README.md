# RAG WinPlus — `self_hosted` et `api`

Deux moteurs de Retrieval-Augmented Generation, architecturalement identiques
(mêmes 4 phases : ingestion, indexation, moteur de réponse, validation
anti-hallucination), mais dont chaque brique est soit auto-hébergée en
PyTorch pur, soit déléguée à une API tierce choisie pour son rapport
coût/performance vérifié.

Ce document explique **quoi**, **pourquoi** et **comment**. Pour la mise en
production, voir [DEPLOYMENT.md](./DEPLOYMENT.md).

## Statut actuel

**Aucun des deux moteurs n'est branché à l'application WinPlus.** Ils vivent
comme des packages Python autonomes, testables via leurs propres routeurs
FastAPI (`self_hosted/router.py`, `api/router.py`, ou le point d'entrée
unifié `RAG/router.py`), mais **aucun n'est monté dans `app.py`**. Le
branchement viendra dans une étape ultérieure, une fois les deux modules
validés indépendamment.

## Pourquoi deux moteurs

- **`self_hosted`** : zéro flux sortant, zéro coût récurrent de tokens,
  fonctionnement même sans connexion Internet. Contrepartie : nécessite une
  instance GPU pour tourner à une latence raisonnable (voir DEPLOYMENT.md),
  et plus de code à maintenir soi-même.
- **`api`** : opérationnel immédiatement, aucune infrastructure GPU à gérer,étudiant 
  coût proportionnel à l'usage réel. Contrepartie : dépendance à des
  fournisseurs tiers et un coût par requête.

`RAG/router.py` expose un point d'entrée unique (`/rag/query`, `/rag/ingest`)
qui bascule entre les deux via la variable d'environnement `RAG_BACKEND` —
le jour du branchement à l'UX WinPlus, un seul appelant suffit pour les deux.

## Architecture commune (`RAG/shared/`)

| Fichier | Rôle |
|---|---|
| `contracts.py` | Schémas Pydantic partagés (requête, réponse, chunk, citation) — c'est ce qui garantit que les deux moteurs sont interchangeables derrière le même endpoint |
| `pdf_utils.py` | Extraction PDF native (PyMuPDF) + test de classification natif/scanné |
| `chunking.py` | Double granularité (parent-child, 128/512 tokens) + semantic chunking sur documents structurés |
| `rrf.py` | Reciprocal Rank Fusion (fusion dense + BM25) |
| `bm25_index.py` | Index lexical épars (`rank_bm25`), un par collection |
| `vector_store.py` | Client Qdrant — **une seule instance partagée**, collections distinctes selon les dimensions de vecteur |
| `metadata.py` | Schéma de versioning documentaire |

## Module `self_hosted` — fidélité au référentiel KALATI-RAG, PyTorch strict

Reproduit les 4 phases du document `ocr_recherche_documentaire_avance.tex`
(architecture KALATI-RAG), adapté au contenu WinPlus (épreuves, corrections,
formations, vidéos de cours) plutôt qu'au corpus normatif CAMRAIL.

### Arbitrage "PyTorch uniquement" — ce qui a changé par rapport au document source

Le document original choisit **PaddleOCR-VL** (framework PaddlePaddle) comme
moteur OCR principal. Vérification faite sur le classement OmniDocBench
actuel : **GLM-OCR** (Zhipu/Z.ai) est maintenant premier (94,62), 0,9 Md
paramètres, licence MIT, et nativement PyTorch/HuggingFace Transformers.
**GLM-OCR remplace donc PaddleOCR-VL partout** (scans, tampons, images
embarquées) — un seul moteur au lieu de deux, meilleur score, 100% PyTorch.

Le reste des composants est déjà nativement PyTorch/HuggingFace : Qwen3
(embedding, reranker, génération), Table Transformer (TATR), Whisper
(`openai-whisper`, pas `faster-whisper` qui repose sur CTranslate2), Coqui
TTS (fork communautaire actif après la fermeture de la société éditrice).

La quantization passe par **`bitsandbytes`** (NF4 4-bit), qui s'intègre
directement dans `transformers`/`torch` — ce n'est pas un moteur d'inférence
concurrent comme Ollama/vLLM/llama.cpp (qui, eux, sont explicitement exclus).

### Correspondance phase par phase

| Phase | Fichiers | Détail |
|---|---|---|
| **1 — Ingestion** | `ingestion/*.py` | Classification native/scanné → GLM-OCR (texte, tampons, images) → Table Transformer (tableaux) → Whisper local (vidéos de formation, `video_pipeline.py`) → correction lexicale (`rapidfuzz`) → chunking → métadonnées |
| **2 — Indexation** | `indexing/*.py` | Embedding Qwen3-Embedding-8B → Qdrant (dense) + BM25 (lexical) |
| **3 — Moteur de réponse** | `engine/*.py` | Routeur de complexité SIMPLE/COMPLEXE → HyDE (si écart sémantique) → recherche hybride + RRF → GraphRAG (requêtes complexes) → reranking Qwen3-Reranker → boucle Self-RAG → génération Qwen3-14B/30B-A3B avec ancrage strict |
| **4 — Validation** | `validation/faithfulness.py` | RAGAS allégé : décomposition en affirmations atomiques, vérification par le LLM local lui-même, gate de faithfulness à 0,90 |

### Modèles utilisés (famille Qwen3 — cohérence écosystémique)

| Rôle | Modèle | VRAM (Q4, indicatif) |
|---|---|---|
| Embedding | Qwen3-Embedding-8B | ~6-8 Go |
| Reranker | Qwen3-Reranker-8B | ~6-8 Go |
| LLM simple | Qwen3-14B | 10-12 Go |
| LLM complexe | Qwen3-30B-A3B (MoE) | 16-20 Go |
| Vérificateur critique | DeepSeek-R1-Distill-Qwen-14B | 10-12 Go |
| OCR/Vision | GLM-OCR | ~2-3 Go |

Tout ne tient pas simultanément sur un seul GPU 24 Go — voir DEPLOYMENT.md
pour la stratégie de chargement/déchargement.

## Module `api` — stack vérifié en ligne

| Rôle | Fournisseur retenu | Pourquoi |
|---|---|---|
| LLM génération | DeepSeek V4-Flash (réutilise `services/deepseek_client.py`) | Déjà intégré, bon en français, très économique |
| OCR (scans, tableaux) | Mistral OCR 4 | Sortie structurée incluse, jusqu'à 15× moins cher qu'Azure en mode structuré |
| Transcription vidéo | Groq Whisper large-v3-turbo | ≈ $0,0007/min, 217-228× temps réel |
| Embeddings | Cohere embed-v4 | Fort en français, pairé nativement avec le reranker Cohere |
| Reranking | Cohere Rerank v3.5 | Même fournisseur que l'embedding |
| Vision (images/schémas) | Gemini 2.5 Flash | ~3-4× moins cher par image que GPT-4o-mini (moins de tokens consommés par image), vérifié en ligne |
| Base vectorielle | Qdrant (même instance que self_hosted) | Auto-hébergé, aucun coût de service managé pendant la construction |

### Pipeline vidéo (ingestion des cours vidéo)

Les **deux moteurs** ingèrent les vidéos de formation, avec la même
segmentation (regroupement en chunks `video_segment` de ~90 mots, métadonnées
`timestamp_start`/`end`, `course_id`, `lesson_id`) mais des moteurs de
transcription différents :
- `api/ingestion/transcription_client.py` → Groq Whisper (API, rapide et bon marché)
- `self_hosted/ingestion/video_pipeline.py` → Whisper local (`openai-whisper`, zéro flux sortant)

Résultat dans les deux cas : WinAI peut citer "expliqué à 12:34 dans la
vidéo Trigonométrie" dans ses réponses.

## Tests

```bash
cd backend/python
pip install -r requirements-dev.txt
python -m pytest RAG/tests -q
```

Couvre sans dépendance externe : contrats Pydantic, chunking (fenêtres
fixes + semantic chunking), Reciprocal Rank Fusion, routeur de complexité.
`test_bm25_index.py` se saute automatiquement (`pytest.importorskip`) tant
que `rank_bm25` n'est pas installé (`requirements-self-hosted.txt` /
`requirements-api.txt`).

## Environnement local en un coup

`docker-compose.yml` inclut désormais un service `qdrant` (mêmes principes
que Postgres/Adminer) — `docker compose up -d qdrant` suffit pour avoir la
base vectorielle disponible en local sans étape manuelle. Les variables
`RAG_*`, `QDRANT_*` et les clés des fournisseurs `api` sont documentées dans
`.env.example`.

## Contrôle d'accès — volontairement hors périmètre

Ni `self_hosted` ni `api` ne réimplémentent les règles de permission
WinPlus (qui a le droit de voir quel contenu). `RAGQueryRequest.filters`
est un dict générique appliqué tel quel comme filtre Qdrant — c'est à
l'appelant (le jour du branchement, probablement le backend ASP.NET Core)
de construire ce filtre à partir des droits réels de l'utilisateur
(formations achetées, catalogue public, etc.).

## Limites connues de cette première version

- GraphRAG (`self_hosted/engine/graphrag.py`) est branché de bout en bout :
  extraction d'entités/relations à l'ingestion (une fois par page, persistée
  via `GraphRegistry`), détection de communautés (Leiden) et indexation de
  leur résumé comme chunks `graph_summary` dans Qdrant à la fin de chaque
  ingestion, et parcours BFS borné à 2 sauts en complément du contexte pour
  les requêtes classées COMPLEXE. Point à surveiller en production : les
  résumés de communautés sont recalculés en entier à chaque ingestion — à
  passer en tâche périodique plutôt qu'à chaque appel si le graphe devient
  volumineux (voir DEPLOYMENT.md, §2.6).
- Les seuils de calibration (`RAG/shared/config.py`) restent des valeurs de
  départ raisonnables. Ce n'est **pas** un manque de code : les calibrer
  correctement demande un golden dataset WinPlus réel (questions annotées
  sur du vrai contenu WinPlus), qui n'existe pas encore — voir
  DEPLOYMENT.md, §5.
- Tests automatisés : `RAG/tests/` couvre tout ce qui est testable sans
  clé API ni GPU (contrats, chunking, RRF, BM25, routeur de complexité,
  RRF). Les pipelines d'ingestion/requête complets nécessitent des
  identifiants réels (Qdrant, modèles) et restent à tester manuellement via
  DEPLOYMENT.md tant que le golden dataset n'existe pas.
