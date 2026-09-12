# RAG WinPlus — `self_hosted` et `api`

Deux moteurs de Retrieval-Augmented Generation, architecturalement identiques
(mêmes 4 phases : ingestion, indexation, moteur de réponse, validation
anti-hallucination), mais dont chaque brique est soit auto-hébergée en
PyTorch pur, soit déléguée à une API tierce choisie pour son rapport
coût/performance vérifié.

Ce document explique **quoi**, **pourquoi** et **comment**. Pour la mise en
production, voir [DEPLOYMENT.md](./DEPLOYMENT.md).

## Statut actuel

**`RAG/router.py` est monté dans `app.py`** (`/api/rag/ingest`,
`/api/rag/ingest/{doc_id}/status`, `/api/rag/query`, `/api/rag/health`) —
branché pour test en conditions réelles, backend actif piloté par
`RAG_BACKEND` (actuellement `api` en `.env.production`). Les routeurs de
test directs par moteur (`self_hosted/router.py`, `api/router.py`) restent
disponibles séparément pour déboguer un moteur en isolation. Voir
DEPLOYMENT.md §0.2 pour l'état constaté au premier branchement réel (ce qui
marche, ce qui reste à corriger côté infrastructure : clé Groq manquante,
Qdrant à démarrer réellement sur l'hôte configuré).

`self_hosted`, lui, ne peut **pas** tourner dans le même environnement
Python que l'app principale (conflit de version `transformers` réel et
vérifié — voir DEPLOYMENT.md §2.0) : il reste à déployer comme service
séparé le jour où vous basculez `RAG_BACKEND=self_hosted`.

**RAG est maintenant branché à WinAI (le chat)** — c'est la partie qui
résout le problème d'origine (documents/vidéos uploadés mais jamais
utilisables par le chat). Voir `RAG/query_service.py` et
`services/rag_chat_bridge.py`, §"Intégration au chat WinAI" ci-dessous, et
DEPLOYMENT.md §7 pour le déploiement de cette partie.

## Pourquoi deux moteurs

- **`self_hosted`** : zéro flux sortant, zéro coût récurrent de tokens,
  fonctionnement même sans connexion Internet. Contrepartie : nécessite une
  instance GPU pour tourner à une latence raisonnable (voir DEPLOYMENT.md),
  et plus de code à maintenir soi-même.
- **`api`** : opérationnel immédiatement, aucune infrastructure GPU à gérer,
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
| Embedding | Qwen3-Embedding-8B | ~5-6 Go |
| Reranker | Qwen3-Reranker-8B | ~5-6 Go |
| LLM simple | Qwen3-14B | 10-12 Go |
| LLM complexe | Qwen3-30B-A3B (MoE) | 16-20 Go |
| Vérificateur critique | DeepSeek-R1-Distill-Qwen-14B | 10-12 Go |
| OCR/Vision | GLM-OCR (0,9 Md paramètres) | ~1-2 Go |

Les quatre premiers modèles sont quantizés en 4-bit (`bitsandbytes`) dès que
`RAG_SH_DEVICE=cuda` — embedding + reranker + LLM simple + OCR tiennent
ensemble dans ~24 Go (≈21-26 Go selon le modèle exact). Le LLM complexe
(Qwen3-30B-A3B) ne tient pas en plus sur le même GPU : voir DEPLOYMENT.md
§2.2 pour la stratégie (chargement à la demande ou second GPU).

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

## Vérifications faites sur les intégrations tierces

Chaque appel à une API/librairie externe a été comparé à sa documentation
officielle actuelle (pas seulement écrit de mémoire), ce qui a corrigé
plusieurs erreurs réelles avant tout premier test en conditions réelles :

| Intégration | Erreur trouvée | Correction |
|---|---|---|
| Qdrant (les deux moteurs) | `chunk_id` des images/tableaux/segments vidéo n'étaient pas des identifiants Qdrant valides (ni entier, ni UUID) — l'upsert aurait échoué | UUID4 partout, UUID5 déterministe pour les résumés GraphRAG (upsert idempotent) |
| Qdrant (les deux moteurs) | `client.search()` déprécié, retrait prévu côté serveur à partir de Qdrant v1.18 | Remplacé par `client.query_points(...).points` |
| Qwen3-Reranker (self_hosted) | Gabarit de prompt approximatif (via `apply_chat_template` générique) au lieu du gabarit exact de la fiche modèle | Préfixe système + suffixe `<think>\n\n</think>\n\n` copiés de la doc officielle |
| GLM-OCR (self_hosted) | Classe `AutoModelForImageTextToText` générique (non garantie enregistrée) + version `transformers` minimale sous-évaluée (4.46.0) | Classe dédiée `GlmOcrForConditionalGeneration` + `transformers>=5.1.0` (version qui l'introduit) |
| Embedding/Reranker (self_hosted) | Jamais quantizés malgré des chiffres VRAM "Q4" annoncés dans ce README | `bitsandbytes` appliqué aux deux, chiffres VRAM recalculés |
| Mistral OCR (api) | Mauvais chemin d'import (`from mistralai import Mistral`) | `from mistralai.client import Mistral`, version plancher relevée à 2.0.0 |
| Cohere embed (api) | Mauvais nom d'attribut sur la réponse (`response.embeddings.float_`) | `response.embeddings.float` (pas un mot réservé Python, pas besoin de suffixe) |
| Groq Whisper (api) | `timestamp_granularities` non demandé — les segments horodatés ne sont pas garantis sans ce paramètre | Ajouté `timestamp_granularities=["segment"]` + accès aux champs rendu robuste (dict ou objet selon la version du SDK, non vérifiable sans appel réel) |
| Gemini vision (api) | GPT-4o-mini initialement retenu | Remplacé par Gemini 2.5 Flash, ~3-4× moins cher par image (vérifié en ligne) |
| BM25 (les deux moteurs) | `search()` filtrait `score > 0` — trouvé en installant réellement `rank_bm25` et en relançant les tests, pas en le devinant | Filtre retiré : la fusion RRF en aval ne consomme que le rang, pas le signe du score (l'IDF négatif est une pathologie normale de BM25 sur petit corpus, pas une absence de pertinence) |

Ce qui reste **non vérifiable sans identifiants réels** (donc à confirmer au
premier test avec de vraies clés API / un vrai GPU, pas un défaut de
vigilance) : le comportement exact de chaque service au runtime, les quotas,
et les éventuels changements d'API publiés après la dernière vérification.

## Intégration au chat WinAI

Topo validé avec l'utilisateur avant cette implémentation : le problème
n'était pas RAG lui-même mais le fait que (1) rien n'indexait le contenu
uploadé, et (2) `routes/chatbot_routes.py` n'avait aucune notion de RAG.
Les deux sont maintenant résolus :

- **`RAG/query_service.py`** — point d'accès interne (pas HTTP) utilisé par
  `services/rag_chat_bridge.py` : `retrieve_context()` fait la récupération
  + rerank SEULS (pas de génération), pour laisser DeepSeek/WinAI composer
  la réponse finale avec son prompt existant (persona, pédagogie, mémoire
  élève) plutôt que celui, générique, de `run_query()`. Les fonctions
  `run_query`/`retrieve_passages` des deux moteurs sont synchrones et
  bloquantes (appels réseau ou inférence locale) : `query_service` les
  exécute via `run_in_threadpool` pour ne jamais geler l'event loop FastAPI.
- **`services/rag_chat_bridge.py`** — déclenchement hybride (topo validé,
  point 3) : RAG n'est interrogé que si (a) la dernière page visitée par
  l'utilisateur (`navigation_history`, envoyé par le frontend) est une
  formation/un cours identifiable (`/subjects/{id}`, `/formations/{id}`),
  ou (b) l'utilisateur nomme explicitement une de ses formations inscrites
  dans son message. Sinon, RAG n'est pas appelé — pas de coût ni de
  latence ajoutés aux messages qui ne portent pas sur un contenu du
  catalogue. Timeout de 8s et dégradation silencieuse (log + poursuite sans
  RAG) en cas d'échec : un souci RAG ne doit jamais empêcher WinAI de
  répondre.
- **Citations et droits d'accès (topo validé, point 5)** — la recherche
  n'est PAS filtrée par les droits de l'utilisateur (un abonnement peut
  donner accès à de l'information sans donner accès au document source).
  Seul l'AFFICHAGE de la source est conditionné : `Citation.subject_id` est
  comparé à `enrolled_subjects` (envoyé par le frontend) pour décider si
  DeepSeek peut nommer la formation/le document, ou doit se contenter
  d'utiliser l'information sans la sourcer (voir le bloc de consigne
  injecté par `_format_context_block`).
- **Déclenchement de l'ingestion à l'upload (topo validé, point 1)** — côté
  ASP.NET Core, `IFastApiClient.QueueRagIngestion()` (`FastApiClient.cs`)
  appelle `POST /api/rag/ingest` en fire-and-forget dès qu'un document/vidéo
  est rattaché à une fiche : `AdminExamsController` (épreuves),
  `AdminLibraryController` (livres/vidéos du catalogue),
  `TeacherCourseController` (leçons vidéo). Le jeton JWT est capturé sur le
  thread de la requête d'origine (pas relu depuis `IHttpContextAccessor`
  après coup, qui n'est plus fiable une fois la réponse HTTP envoyée).
- **Contenu déjà existant (topo validé, point 2)** —
  `RAG/scripts/backfill_existing_content.py`, script one-shot à lancer
  manuellement : `python -m RAG.scripts.backfill_existing_content
  --dry-run` pour lister/compter (estimer le coût) avant de lancer pour de
  vrai. Couvre `Exams`, `CourseContents` et `CourseLessons` (ces deux
  derniers via `database.py` / requête SQL brute, `CourseLessons` n'ayant
  pas de modèle SQLAlchemy Python).

## Base de connaissance personnelle, score de pertinence, pièces jointes de chat

Extension décidée après un test réel : un utilisateur a joint un PDF
directement dans le chat, et WinAI a répondu qu'il ne pouvait pas lire son
contenu — un chemin de code totalement séparé de RAG (`DescribeDocument`
côté .NET, `format_messages_for_deepseek` côté Python), qui codait en dur
"extraction non disponible" pour tout fichier non textuel, indépendamment
de RAG. Décision utilisateur : **tout document uploadé, y compris une
pièce jointe de chat, doit servir dans la base de connaissance**, avec un
score de pertinence assigné à l'ingestion.

- **Base de connaissance personnelle** — une pièce jointe de chat est
  souvent personnelle (devoir, brouillon) : elle est donc TOUJOURS indexée
  dans un périmètre personnel (`ChunkMetadata.owner_user_id`), jamais dans
  le corpus public partagé entre utilisateurs. `POST /api/rag/ingest`
  détermine ça automatiquement : sans `subject_id` ni `course_id` fournis,
  `owner_user_id` est posé depuis le JWT (jamais accepté du corps de la
  requête — un utilisateur ne peut pas usurper le corpus d'un autre).
  `services/rag_chat_bridge.py` interroge maintenant TOUJOURS ce périmètre
  personnel en plus du scope public hybride (voir plus haut) — un utilisateur
  qui n'a jamais rien joint obtient juste une recherche vide, rapide.
- **Score de pertinence composite** (`RAG/shared/relevance_scoring.py`) —
  calculé une fois par document à l'ingestion, combine (décision
  utilisateur : "on peut combiner les 3") :
  - *nouveauté* : 1 − similarité cosinus avec le contenu le plus proche déjà
    indexé dans le même périmètre (repère les quasi-doublons) ;
  - *qualité pédagogique* : jugée par DeepSeek (repère les pages de garde,
    sommaires vides, texte non substantiel) ;
  - *adéquation au sujet déclaré* : similarité entre le contenu et le
    sujet/catégorie renseigné à l'upload (repère un document mal classé,
    logué en warning si très faible).
  Composite : `topic_fit × (0.5×nouveauté + 0.5×qualité)` — l'adéquation
  au sujet agit en filtre/multiplicateur plutôt qu'en simple moyenne, pour
  qu'un document hors-sujet ne remonte pas haut même par ailleurs "bon".
  Stocké dans `ChunkMetadata.relevance_score`, utilisé pour repondérer le
  score de rerank au retrieval (`_apply_relevance_boost` dans les deux
  `engine/pipeline.py`). Dégradation systématique par sous-score
  indisponible (clé API absente, toute première ingestion d'un périmètre) :
  neutralisé plutôt que de pénaliser le document pour un souci
  d'infrastructure sans rapport avec son contenu.
- **`RAG/shared/file_resolver.py`** — bug réel trouvé en vérifiant le code
  (pas supposé) : `fitz.open(pdf_path)`, utilisé partout dans les pipelines
  d'ingestion, n'accepte qu'un chemin local ou un flux d'octets, PAS une
  URL http(s). Or TOUS les appelants (`.NET` via `QueueRagIngestion`, le
  script de backfill) passent une URL S3 publique comme `file_path` —
  l'ingestion aurait échoué dès le premier vrai appel avec de vraies
  données. Corrigé : ce module télécharge l'URL vers un fichier temporaire
  avant tout traitement (et décode un contenu inline en base64 pour les
  pièces jointes de chat, qui ne passent jamais par S3), avec nettoyage
  résilient (testé réellement : PyMuPDF garde parfois un verrou sur le
  fichier sous Windows, le nettoyage ne doit jamais faire échouer une
  ingestion par ailleurs réussie pour cette seule raison).
- **`services/attachment_processor.py`** — le correctif concret du bug
  observé : une pièce jointe non textuelle (PDF...) déclenche désormais
  une extraction native immédiate (PyMuPDF, testé avec un vrai PDF généré
  à la volée) injectée dans le message pour une réponse sans attendre,
  ET planifie en parallèle l'ingestion RAG complète en tâche de fond (OCR
  si le PDF est scanné, embeddings, indexation) pour que le contenu
  redevienne cherchable sur les messages futurs. `doc_id` dérivé du hash du
  contenu (pas un uuid) : renvoyer le même fichier deux fois réutilise le
  même `doc_id` (supersession plutôt que doublon illimité).
  - Côté Python (`routes/chatbot_routes.py`, chemin `/chat` non-stream,
    confirmé être le chemin réellement utilisé par le frontend
    aujourd'hui) : appel direct, en process, `process_chat_attachment_async`.
  - Côté .NET (`ChatbotController.cs::DescribeDocumentAsync`, chemin
    `/stream`) : `.NET` n'a pas d'équivalent PyMuPDF/OCR — nouvel endpoint
    `POST /api/rag/chat-attachment` exposé pour cet usage précis.

### Limites connues de cette intégration — toutes corrigées et vérifiées

- ~~Déclenchement par page consultée pas alimenté~~ — **corrigé** :
  `useWinAIContext.ts` synchronise `navigationHistory` à chaque changement
  de route, et `enrolled_subjects` est recalculé depuis `Enrollments` à
  chaque message (`ChatbotService.cs::GetRealEnrolledSubjectsAsync`). Un
  bug de casse JSON .NET→Python qui aurait fait perdre tout ce contexte
  silencieusement a aussi été trouvé et corrigé (voir DEPLOYMENT.md §0.2)
  — vérifié par un test isolé + un test de parsing Python, pas supposé.
- ~~Citations de cours (`course_id`) non filtrées par accès~~ — **corrigé** :
  `enrolled_courses` (nouveau champ `ChatbotContextRequest`, recalculé côté
  .NET depuis `CourseEnrollments` à chaque message,
  `ChatbotService.cs::GetRealEnrolledCoursesAsync`) est maintenant comparé
  au `course_id` de chaque citation, symétriquement à `subject_id`/
  `enrolled_subjects`. Vérifié par un test direct (citation `subject_id`
  non inscrit → masquée, citation `course_id` inscrit → citable).
- ~~Pas de supersession automatique~~ — **corrigé** :
  `RAG/router.py::_supersede_previous_version` (et l'équivalent dans le
  script de backfill) marque `superseded` les chunks existants d'un
  `doc_id` juste avant d'indexer sa nouvelle version, donc un document
  remplacé ne laisse plus l'ancien contenu retrouvable indéfiniment aux
  côtés du nouveau.

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
