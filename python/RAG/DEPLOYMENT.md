# Guide de mise en production — RAG WinPlus

## 0. Avant de commencer

Les deux modules sont **autonomes et non branchés** à `app.py`. Ce guide
couvre : (1) les faire tourner isolément pour validation, (2) leur
infrastructure cible, (3) le branchement final quand vous serez prêts.

### 0.1 Ce qui a été vérifié, et ce qui ne peut l'être qu'en conditions réelles

Chaque appel aux API tierces (Mistral, Cohere, Groq, Gemini) et aux modèles
HuggingFace (GLM-OCR, Qwen3-Reranker) a été comparé à sa documentation
officielle actuelle — le détail des erreurs trouvées et corrigées est dans
[README.md, §Vérifications faites sur les intégrations tierces](./README.md#vérifications-faites-sur-les-intégrations-tierces).
Ce qu'aucune vérification documentaire ne remplace : un premier appel réel
avec de vraies clés API et, côté `self_hosted`, un vrai GPU. Avant de
considérer un des deux modules "prêt", exécutez au moins une fois le test
isolé (§1.3 / §2.4) avec :
- un vrai PDF natif, un PDF scanné, et si possible une courte vidéo ;
- une clé API valide pour chaque fournisseur utilisé ;
- une lecture des `warnings` retournés par `/ingest` (le pipeline avale les
  erreurs par étape plutôt que d'interrompre tout l'import — un fournisseur
  mal configuré se voit dans les warnings, pas dans une exception).

### 0.2 État constaté au premier branchement réel (`app.py` + `.env.production`)

- ✅ `app.py` démarre avec le router RAG monté : `POST /api/rag/ingest`,
  `GET /api/rag/ingest/{doc_id}/status`, `POST /api/rag/query`,
  `GET /api/rag/health` sont bien exposés.
- ✅ Un bug réel a été trouvé et corrigé en installant pour de vrai
  `RAG/requirements-api.txt` et en relançant les tests : `BM25Index.search`
  filtrait `score > 0`, ce qui pouvait renvoyer une liste vide alors que le
  classement relatif entre documents restait valide (IDF négatif = terme
  présent dans 100 % du corpus, pathologie connue de BM25 sur petit corpus).
  Corrigé dans `RAG/shared/bm25_index.py`.
- ⚠️ `GROQ_API_KEY` n'est pas présente dans `.env.production` — la
  transcription vidéo du module `api` échouera tant qu'elle n'est pas
  ajoutée.
- ⚠️ `QDRANT_URL` pointe vers `172.31.8.182:6333` : le port répond au niveau
  TCP mais **réinitialise la connexion** dès qu'une vraie requête HTTP est
  envoyée (`ConnectionResetError`) — signe typique qu'aucun serveur Qdrant
  n'écoute réellement à cette adresse (le port est ouvert au firewall, mais
  le conteneur Qdrant n'a probablement jamais été démarré sur cet hôte,
  voir §3.2). Tant que ce n'est pas corrigé, `/ingest` acceptera la requête
  (statut "queued") mais la tâche d'arrière-plan échouera à l'étape
  d'indexation — vérifiable via `GET /rag/ingest/{doc_id}/status`.

---

## 1. Module `api` — mise en route (le plus rapide)

### 1.1 Dépendances

```bash
cd backend/python
pip install -r RAG/requirements-api.txt
```

### 1.2 Variables d'environnement

Ajoutez à votre `.env` (ou `.env.production`) :

```bash
RAG_BACKEND=api

COHERE_API_KEY=...
MISTRAL_API_KEY=...
GROQ_API_KEY=...
GEMINI_API_KEY=...

# Qdrant auto-hébergé (voir §3)
QDRANT_URL=http://<ip-instance-qdrant>:6333
QDRANT_API_KEY=          # optionnel si l'instance n'a pas d'auth activée
```

### 1.3 Test isolé (sans monter dans app.py)

```bash
cd backend/python
python -c "
from RAG.api.router import api_router
from fastapi import FastAPI
import uvicorn
app = FastAPI()
app.include_router(api_router)
uvicorn.run(app, host='0.0.0.0', port=8100)
"
```

Puis :

```bash
curl -X POST http://localhost:8100/rag/api/ingest \
  -H "Authorization: Bearer <token_valide>" \
  -H "Content-Type: application/json" \
  -d '{"doc_id":"TEST_001","title":"Test","file_path":"/chemin/vers/fichier.pdf"}'

curl -X POST http://localhost:8100/rag/api/query \
  -H "Authorization: Bearer <token_valide>" \
  -H "Content-Type: application/json" \
  -d '{"question":"Que dit ce document ?","top_k":5}'
```

### 1.4 ffmpeg (requis pour la transcription vidéo)

```bash
# Sur l'EC2 (Ubuntu/Debian) :
sudo apt-get install -y ffmpeg
```

---

## 2. Module `self_hosted` — mise en route progressive

### 2.0 Isolation obligatoire : `self_hosted` ne peut pas partager le venv de l'app principale

Vérifié en installant réellement les deux jeux de dépendances côte à côte :
le `requirements.txt` principal fixe `transformers==4.35.2` (utilisé par
`models/nlp_analyzer.py`), alors que GLM-OCR exige `transformers>=5.1.0`
(§2.1 ci-dessous) — un saut de version majeure, pas un simple correctif.
Installer `RAG/requirements-self-hosted.txt` dans le même environnement que
l'app principale **downgrade ou casse l'un des deux**. `self_hosted` doit
tourner dans son propre virtualenv (et, en pratique, sur sa propre instance
GPU — voir §2.2) : ce n'est pas qu'une préférence d'infrastructure, c'est
une incompatibilité de dépendances réelle et vérifiée.

`RAG/requirements-api.txt`, en revanche, cohabite désormais sans conflit
avec le `requirements.txt` principal — vérifié par un import complet de
`app.py` avec les deux installés ensemble (nécessite `httpx>=0.28.1`, relevé
dans `requirements.txt` pour cette raison ; aucun appelant direct de httpx
trouvé ailleurs dans le code, risque faible).

### 2.1 Étape 1 : valider la logique sur l'EC2 actuel (CPU)

Le code est device-agnostic (`torch.device("cuda" if available else "cpu")`)
— il tourne dès maintenant sur votre EC2 Python actuel, **lentement**
(génération : plusieurs dizaines de secondes à quelques minutes par requête
sur Qwen3-14B en CPU, contre 4-5s visés sur GPU). Objectif de cette étape :
valider que le pipeline est correct de bout en bout, pas juger la latence.

```bash
cd backend/python
pip install -r RAG/requirements-self-hosted.txt
```

⚠️ Attention taille : `torch` + `transformers` + les poids des modèles
(Qwen3-14B seul ≈ 28 Go en fp16, bien plus en fp32 sur CPU) — vérifiez
l'espace disque disponible avant de lancer le premier téléchargement HF.
Sur CPU, ne chargez pas Qwen3-30B-A3B ni le vérificateur DeepSeek-R1-Distill
pour ce premier test : forcez le modèle simple uniquement en modifiant
temporairement `get_llm_complex()` pour qu'il retourne `get_llm_simple()`.

⚠️ GLM-OCR (`get_ocr_vlm()`) nécessite `transformers>=5.1.0` (version qui a
introduit `GlmOcrForConditionalGeneration`). Vérifié : l'environnement de
développement actuel tourne en 4.56.0 — le reste du module (Qwen3, Table
Transformer, Whisper) fonctionne déjà avec cette version, seul le chargement
de l'OCR échouera tant que `transformers` n'est pas mis à jour :

```bash
pip install --upgrade "transformers>=5.1.0"
```

```bash
export RAG_SH_DEVICE=cpu
export RAG_BACKEND=self_hosted
```

### 2.2 Étape 2 : provisionner le GPU cible

Quand vous êtes prêt à mesurer la vraie latence, provisionnez une instance
GPU. Le référentiel source cible une RTX 3090 (24 Go VRAM) ; équivalents
AWS :

| Instance AWS | GPU | VRAM | Usage |
|---|---|---|---|
| `g5.xlarge` | A10G | 24 Go | Suffisant pour Qwen3-14B + embedding + reranker + GLM-OCR en quantization 4-bit |
| `g5.2xlarge` | A10G | 24 Go | Idem, plus de vCPU/RAM pour l'ingestion (OCR/tableaux en parallèle) |
| `g4dn.xlarge` | T4 | 16 Go | Budget serré — Qwen3-14B seul en 4-bit, pas de place pour le modèle complexe en simultané |

Avec Qwen3-30B-A3B (16-20 Go) en plus de l'embedding/reranker/OCR, un seul
GPU 24 Go ne suffit pas à tout garder chargé simultanément. Deux options :
- **Chargement à la demande** : ne charger Qwen3-30B-A3B qu'au moment d'une
  requête COMPLEXE, le décharger ensuite (`del model; torch.cuda.empty_cache()`)
  — latence additionnelle au premier appel complexe, mais tient dans 24 Go.
  C'est le comportement actuel de `models/loader.py` (chargement paresseux) ;
  ajouter un déchargement explicite si la mémoire devient contrainte.
- **Deux GPU** : un dédié au modèle simple + embedding + reranker + OCR, un
  second dédié au modèle complexe (`CUDA_VISIBLE_DEVICES` par processus).

```bash
export RAG_SH_DEVICE=cuda
export RAG_SH_QUANTIZE_4BIT=true
```

### 2.3 Lexique métier

Avant l'ingestion réelle, construisez `RAG/self_hosted/lexicon_winplus.txt`
(un terme par ligne) à partir d'un échantillon du corpus WinPlus — sigles
d'examens, noms de matières, vocabulaire technique par filière. Le fichier
par défaut (`_DEFAULT_LEXICON` dans `lexicon_correction.py`) est un point de
départ minimal, pas un lexique de production.

### 2.4 Test isolé

Même principe qu'en §1.3, en pointant sur `RAG.self_hosted.router`.

### 2.5 Vidéos de formation (transcription locale)

`self_hosted` transcrit les vidéos avec le Whisper local (`get_whisper_model()`
dans `models/loader.py`), sans dépendance à un service tiers — équivalent
100% local du pipeline vidéo de `RAG/api` (qui, lui, appelle l'API Groq).
Nécessite `ffmpeg` sur la machine pour l'extraction audio :

```bash
sudo apt-get install -y ffmpeg
```

Le premier chargement du modèle `large-v3` télécharge ~3 Go de poids
(mis en cache par la librairie `openai-whisper`). Sur CPU, la transcription
est nettement plus lente que temps réel — réservez ce test à une vidéo
courte tant que vous êtes encore sur l'EC2 CPU (§2.1).

### 2.6 Résumés de communautés GraphRAG en tâche périodique

`graphrag.build_and_index_community_summaries()` recalcule et ré-indexe
l'intégralité des résumés de communautés à chaque appel — actuellement
déclenché à la fin de **chaque** ingestion (`ingestion/pipeline.py`). Correct
et peu coûteux pour un corpus de démarrage, mais redondant une fois le
graphe volumineux (des dizaines d'ingestions successives recalculent le
même graphe encore et encore). À ce stade, remplacez l'appel en fin
d'ingestion par une tâche planifiée (cron, ou un scheduler déjà présent côté
`.NET`/`Hangfire` si vous en utilisez un) qui l'exécute une fois par jour :

```python
from RAG.self_hosted import config
from RAG.self_hosted.engine import graphrag
from RAG.shared.graph_registry import GraphRegistry

graph = GraphRegistry.get(config.QDRANT_COLLECTION)
graphrag.build_and_index_community_summaries(config.QDRANT_COLLECTION, graph)
```

---

## 3. Base vectorielle Qdrant — instance partagée

Une seule instance Qdrant sert les deux modules (collections distinctes :
`winplus_self_hosted` en 3072 dimensions, `winplus_api` en 1536).

### 3.1 Provisionnement

Une instance EC2 modeste suffit pour démarrer — Qdrant est principalement
consommateur de RAM :

```bash
# t3.large (2 vCPU, 8 Go RAM) convient pour un corpus de départ
# (quelques dizaines de milliers de chunks). Scaler la RAM si le volume
# grandit significativement.
```

### 3.2 Lancement (Docker)

```bash
docker run -d --name qdrant \
  -p 6333:6333 -p 6334:6334 \
  -v /data/qdrant_storage:/qdrant/storage \
  qdrant/qdrant:latest
```

Ouvrir le port 6333 dans le security group **uniquement** depuis l'IP de
l'instance FastAPI (jamais 0.0.0.0/0). Activer l'authentification API key en
production :

```bash
docker run -d --name qdrant \
  -p 6333:6333 -p 6334:6334 \
  -v /data/qdrant_storage:/qdrant/storage \
  -e QDRANT__SERVICE__API_KEY=<clé-forte> \
  qdrant/qdrant:latest
```

### 3.3 Sauvegarde

Le volume `/data/qdrant_storage` contient tout l'index — à inclure dans vos
sauvegardes régulières (snapshot EBS ou `qdrant` snapshot API native).

---

## 4. Branchement à l'application — FAIT

```python
# backend/python/app.py
from RAG.router import rag_router
app.include_router(rag_router, prefix="/api", tags=["rag"])
```

`/api/rag/ingest`, `/api/rag/ingest/{doc_id}/status`, `/api/rag/query`,
`/api/rag/health` sont live. Le choix du moteur actif reste piloté par
`RAG_BACKEND` sans toucher au code appelant.

Le contrôle d'accès à la RECHERCHE reste volontairement hors périmètre de
ce module (voir README.md) — mais le branchement au chat WinAI (§7)
implémente le filtrage d'accès aux CITATIONS, qui est la forme retenue
après discussion avec l'équipe produit (topo validé, point 5).

---

## 5. Évaluation continue (Phase 4 du référentiel)

Avant un déploiement en usage réel, constituez un **golden dataset WinPlus**
(équivalent des 50 questions annotées du référentiel source) : questions
représentatives du contenu WinPlus, réponses de référence, chunk_id sources
attendus. Utilisez-le pour :
1. Calibrer les seuils de `RAG/shared/config.py` (actuellement des valeurs
   de départ raisonnables, pas des valeurs mesurées sur votre corpus).
2. Détecter les régressions à chaque évolution du pipeline ou du corpus.

Cette étape n'est pas incluse dans cette version — à construire une fois
qu'un volume de contenu WinPlus réel a été ingéré dans l'un des deux moteurs.

---

## 6. Coûts récurrents à anticiper (module `api`)

| Poste | Ordre de grandeur |
|---|---|
| DeepSeek V4-Flash | $0,14/M tokens entrée, $0,28/M sortie |
| Cohere embed-v4 | $0,12/M tokens |
| Cohere Rerank v3.5 | $2/1000 requêtes |
| Mistral OCR 4 | $4/1000 pages ($2 en batch) |
| Groq Whisper large-v3-turbo | ≈ $0,0007/minute audio |
| Gemini 2.5 Flash (vision) | facturation par image, voir console Google AI |
| Qdrant auto-hébergé | Coût EC2 uniquement (pas de frais par requête) |

Pas de coût de licence logicielle côté `self_hosted` — uniquement
l'infrastructure GPU (§2.2) et la maintenance humaine.

---

## 7. Intégration au chat WinAI — déploiement et vérification

Cette section couvre la partie qui résout le problème d'origine (contenu
uploadé mais jamais utilisable par WinAI) — voir README.md, §"Intégration
au chat WinAI" pour l'architecture.

### 7.1 Pré-requis avant d'activer réellement le RAG dans le chat

1. `RAG_BACKEND` correctement configuré et `/api/rag/health` répond `ok`.
2. Au moins un document/vidéo déjà indexé dans Qdrant pour la formation que
   vous allez utiliser pour le test (voir §7.3 pour le backfill, ou faites
   simplement un nouvel upload de test via l'admin).
3. **Vérifié en lisant le code réel (pas supposé)** : le chemin de chat
   effectivement utilisé par le frontend (`useChatbot.ts` →
   `chatbotService.sendMessage` → .NET `POST /api/chatbot/message` →
   `ChatbotService.SendMessageAsync`) n'envoyait `enrolled_subjects` que si
   un `ChatbotContext` avait été synchronisé au préalable via
   `POST /chatbot/context/sync` — or **rien dans le frontend n'appelle
   jamais cette route** (`chatbotService.syncContext` existe mais n'est
   invoqué nulle part). En clair : `enrolled_subjects` arrivait vide dans
   99% des cas, ce qui aurait rendu le filtrage d'accès aux citations
   (point 5) inopérant. **Corrigé** dans `ChatbotService.cs` :
   `BuildFastApiRequestAsync` recalcule maintenant `EnrolledSubjects`
   directement depuis la table `Enrollments` à chaque message, sans
   dépendre de la synchronisation frontend.
   - `navigation_history`, lui, n'était alimenté par rien : `chatbotService
     .syncContext()` existait côté frontend mais n'était appelé nulle part.
     **Corrigé** : `hooks/useWinAIContext.ts` (monté globalement via
     `GlobalAIAssistant` dans `App.tsx`, donc actif sur toutes les pages)
     appelle maintenant `syncContext({ navigationHistory: [...] })` à
     chaque changement de route (`useLocation().pathname`), un seul élément
     par appel — le backend fusionne les champs (`CreateOrUpdateContextAsync`,
     `?? existing...`) donc ça ne peut pas écraser le reste du contexte
     déjà synchronisé, et `rag_chat_bridge.py` ne lit de toute façon que la
     dernière entrée. Le déclenchement RAG "page consultée" (topo, point
     3a) est donc maintenant réellement alimenté en conditions réelles, pas
     seulement le repli "mention explicite" (3b).
   - **Bug de casse JSON réel, trouvé et corrigé (vérifié empiriquement,
     pas supposé)** : `ChatbotService.CallFastApiServiceAsync` envoyait
     `FastApiChatRequest` via `PostAsJsonAsync` SANS options explicites.
     Test isolé avec un petit programme .NET consommant les mêmes types :
     ce chemin sérialise en **camelCase** (`JsonSerializerDefaults.Web`,
     PAS `JsonSerializerOptions.Default`/PascalCase comme on pourrait le
     supposer) — `UserContext` devenait `"userContext"`, `EnrolledSubjects`
     devenait `"enrolledSubjects"`. Comme les schémas Pydantic
     (`ChatRequest.user_context`, etc.) attendent du snake_case strict et
     que ces champs ont une valeur par défaut, la requête ne plantait PAS
     (pas de 422) — elle perdait juste **tout le contexte WinAI
     silencieusement** (formations inscrites, page consultée, lacunes,
     mémoire...) sur chaque message envoyé via `/api/chatbot/message`.
     **Corrigé** : `CallFastApiServiceAsync` utilise maintenant
     `JsonNamingPolicy.SnakeCaseLower` (disponible net8.0+, la cible de ce
     projet) explicitement, dans les deux sens (envoi de la requête ET
     lecture de la réponse — `TokensUsed`/`GenerationTimeMs` avaient le
     même problème en sens inverse). Revérifié par un test Python direct :
     le JSON désormais produit (`user_context`, `enrolled_subjects` avec
     `subject_id`/`title`, `navigation_history`) est correctement parsé par
     `ChatRequest`. Les DTO partagés avec le frontend
     (`ChatbotContextResponse` retourné par `GET /chatbot/context`) n'ont
     PAS été touchés : ils restent camelCase, ce qui est le format attendu
     côté React — seul l'appel .NET → Python a été isolé avec sa propre
     policy.

### 7.2 Test manuel du branchement chat ↔ RAG

1. Naviguez sur `/subjects/{id}` ou `/formations/{id}` d'une formation qui
   a du contenu déjà indexé.
2. Ouvrez WinAI et posez une question dont la réponse se trouve dans ce
   contenu (ex: une notion précise traitée dans le document).
3. Vérifiez dans les logs FastAPI (`services/rag_chat_bridge.py`) que RAG a
   bien été déclenché (pas de warning "Timeout" ni "Récupération de
   contexte échouée").
4. Si la réponse de WinAI intègre l'information sans jamais la déclencher
   sur une question hors-sujet (ex: "motive-moi"), le déclenchement hybride
   fonctionne comme prévu (topo, point 3).
5. Testez le filtrage de citation (topo, point 5) : posez une question dont
   la réponse vient d'une formation à laquelle l'utilisateur de test n'est
   **pas** inscrit (`enrolled_subjects` ne la contient pas) — WinAI doit
   utiliser l'information sans jamais nommer la formation/le document.

### 7.3 Backfill du contenu déjà existant (topo, point 2)

Script one-shot, à lancer manuellement UNE fois après avoir validé le §7.2 :

```bash
cd backend/python

# 1. Dry-run d'abord : liste et compte ce qui serait ingéré, sans appeler
#    aucune API tierce — sert à estimer le coût (§6) avant de lancer.
python -m RAG.scripts.backfill_existing_content --dry-run

# 2. Test sur un petit échantillon
python -m RAG.scripts.backfill_existing_content --limit 20

# 3. Ingestion complète
python -m RAG.scripts.backfill_existing_content
```

Le script tourne en série (pas de parallélisme) pour rester dans les
limites de rate-limiting des API tierces — un catalogue volumineux peut
prendre plusieurs heures, c'est attendu (voir README.md pour le détail de
ce qui est couvert : `Exams`, `CourseContents`, `CourseLessons`). Un échec
sur un document n'interrompt pas le reste : la liste des échecs est
affichée en fin d'exécution pour un ré-essai ciblé.

### 7.4 Ce qui N'EST PAS fait automatiquement

- **Backfill automatique périodique** : le script est one-shot et manuel
  par choix explicite (topo, point 2). S'il faut le refaire (ex: après une
  restauration de sauvegarde Qdrant), relancez-le à la main.

La supersession des anciens chunks à la mise à jour d'un document et le
filtrage d'accès aux citations `course_id` sont, eux, désormais gérés
automatiquement — voir README.md, "Limites connues de cette intégration —
toutes corrigées et vérifiées".

---

## 8. Base personnelle, score de pertinence, pièces jointes de chat

Voir README.md, "Base de connaissance personnelle, score de pertinence,
pièces jointes de chat" pour l'architecture complète. Ce qui suit couvre
la vérification et le déploiement.

### 8.1 Ce qui a été vérifié par test réel (pas supposé)

- **`RAG/shared/file_resolver.py`** : testé avec un vrai PDF généré à la
  volée (PyMuPDF) encodé en base64 — décodage vers fichier temporaire,
  lecture par `fitz.open()`, nettoyage en sortie de contexte. Un bug réel a
  été trouvé ET corrigé dans ce test même : sous Windows, PyMuPDF peut
  garder un verrou sur le fichier après lecture, ce qui faisait échouer
  `os.remove()` avec `PermissionError` — le nettoyage est maintenant
  best-effort (log, ne fait jamais échouer une ingestion par ailleurs
  réussie pour ce seul détail).
- **`services/attachment_processor.py::_prepare`** : testé avec le même
  PDF de test — extraction native immédiate confirmée fonctionnelle,
  `IngestRequest` généré avec `owner_user_id`, `inline_content_base64`,
  `file_extension_hint` corrects.
- **`RAG/shared/relevance_scoring.py::compute_relevance_score`** : testé
  avec un `embed_fn` factice, Qdrant et DeepSeek injoignables (environnement
  de dev sans ces services démarrés) — confirmé que le score composite reste
  dans [0, 1] et se calcule quand même (dégradation par sous-score neutre,
  pas d'exception propagée qui ferait échouer l'ingestion).
- **`dotnet build`** : 0 erreur après le passage de `DescribeDocument` en
  `DescribeDocumentAsync` (appel HTTP vers le nouvel endpoint Python) et la
  restructuration de `StreamChat` (pré-calcul des descriptions de pièces
  jointes hors de la lambda LINQ synchrone, qui ne peut pas `await`).

### 8.2 Ce qui reste à vérifier en conditions réelles (ne peut pas l'être ici)

- Le comportement réel de `POST /api/rag/chat-attachment` avec un vrai
  utilisateur, un vrai PDF scanné (déclenche l'OCR Mistral en tâche de
  fond) et une vraie clé Qdrant/Cohere.
- Le coût réel ajouté par l'interrogation systématique du périmètre
  personnel à chaque message (`services/rag_chat_bridge.py`) — un appel
  d'embedding Cohere par message, même quand le corpus personnel de
  l'utilisateur est vide. Négligeable individuellement, à surveiller à
  l'échelle de tout le trafic chat si le volume de messages est élevé.
- Le jugement de qualité LLM (`_quality_score_llm`) ajoute un appel
  DeepSeek par document ingéré — surveiller son impact sur le temps
  d'ingestion total pour un gros backfill (§7.3), et sur le coût mensuel
  DeepSeek (§6).

### 8.3 Nouveau endpoint

`POST /api/rag/chat-attachment` — utilisé par `ChatbotController.cs`
(chemin `/stream`) pour extraire le texte d'une pièce jointe de chat non
textuelle. Body : `{"data_url_or_base64": "...", "file_name": "..."}`.
Réponse : `{"text_for_prompt": "..."}`. Authentifié (JWT) — `owner_user_id`
de l'ingestion planifiée en tâche de fond est dérivé du token, jamais du
corps de la requête.
