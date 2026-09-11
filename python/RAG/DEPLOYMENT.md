# Guide de mise en production — RAG WinPlus

## 0. Avant de commencer

Les deux modules sont **autonomes et non branchés** à `app.py`. Ce guide
couvre : (1) les faire tourner isolément pour validation, (2) leur
infrastructure cible, (3) le branchement final quand vous serez prêts.

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
OPENAI_API_KEY=...

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

## 4. Branchement final à l'application (étape future, pas maintenant)

Quand les deux modules sont validés indépendamment :

```python
# backend/python/app.py
from RAG.router import rag_router
# ...
app.include_router(rag_router)
```

Une seule ligne. Le choix du moteur actif reste piloté par `RAG_BACKEND`
sans toucher au code appelant (ASP.NET Core via `FastApiClient.cs`, ou
directement depuis le frontend si l'endpoint est exposé publiquement).

Le contrôle d'accès (qui a le droit de voir quel contenu) reste à
construire côté appelant à ce moment-là : traduire les droits réels de
l'utilisateur authentifié en `RAGQueryRequest.filters` (ex.
`{"subject_id": [...], "status": "active"}`).

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
| Groq Whisper turbo | ≈ $0,0007/minute audio |
| Qdrant auto-hébergé | Coût EC2 uniquement (pas de frais par requête) |

Pas de coût de licence logicielle côté `self_hosted` — uniquement
l'infrastructure GPU (§2.2) et la maintenance humaine.
