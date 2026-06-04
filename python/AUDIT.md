# Audit — Backend Python WinPlus
> Date : 2026-06-04

---

## 1. Structure du projet

```
backend/python/
├── app.py                           # Application principale FastAPI
├── app_sprint3_mock.py              # Version mock Sprint 3 (Flask)
├── auth.py                          # Authentification JWT
├── database.py                      # Connexion PostgreSQL + ORM SQLAlchemy
├── schemas.py                       # Schémas de validation Pydantic
├── requirements.txt                 # Dépendances Python
├── .env.example                     # Template de configuration
├── docker-compose.yml               # Stack PostgreSQL + FastAPI + Adminer
├── Dockerfile.fastapi               # Image Docker multi-stage FastAPI
├── Dockerfile.aws                   # Image Docker .NET (mal nommé)
├── models/
│   ├── nlp_analyzer.py              # Analyseur NLP CamemBERT
│   └── recommender.py               # Système de recommandation TF-IDF
├── routes/
│   ├── __init__.py
│   └── chatbot_routes.py            # Endpoints Chatbot / DeepSeek
└── services/
    ├── __init__.py
    ├── deepseek_client.py           # Client API DeepSeek avec retry
    └── user_performance_analyzer.py # Analyse progression utilisateur
```

---

## 2. Configuration

### Variables d'environnement (`.env.example`)

| Groupe | Variable | Valeur exemple |
|--------|----------|----------------|
| FastAPI | `PORT` | 8000 |
| FastAPI | `ENVIRONMENT` | development |
| FastAPI | `LOG_LEVEL` | info |
| Database | `DB_HOST` | localhost |
| Database | `DB_PORT` | 5432 |
| Database | `DB_NAME` | winplus_db |
| Database | `DB_USER` | miguel |
| JWT | `JWT_SECRET_KEY` | — |
| JWT | `JWT_ALGORITHM` | HS256 |
| DeepSeek | `DEEPSEEK_BASE_URL` | — |
| DeepSeek | `DEEPSEEK_API_KEY` | — |
| DeepSeek | `DEEPSEEK_MODEL` | deepseek-chat |
| NLP | `NLP_MODEL` | camembert-base |
| NLP | `MODEL_CACHE_DIR` | ./models_cache |
| Features | `ENABLE_RECOMMENDATIONS` | true |
| Features | `ENABLE_QUIZ_GENERATION` | true |
| Features | `ENABLE_PROGRESS_ANALYSIS` | true |

### Dépendances (`requirements.txt`)

| Catégorie | Packages |
|-----------|----------|
| Framework | `fastapi==0.104.1`, `uvicorn[standard]==0.24.0` |
| Auth | `PyJWT==2.8.0` |
| Database | `psycopg2-binary==2.9.9`, `SQLAlchemy==2.0.23` |
| ML / Data | `numpy==1.26.2`, `pandas==2.1.4`, `scikit-learn==1.3.2`, `scipy==1.11.4` |
| NLP / BERT | `transformers==4.35.2`, `tokenizers==0.15.0`, `sentencepiece==0.1.99` |
| Rate limit | `slowapi==0.1.9` |
| AWS | `boto3==1.34.0` |
| Cache | `redis==5.0.1` |
| Monitoring | `prometheus-client==0.19.0`, `python-json-logger==2.0.7` |
| Tests | `pytest==7.4.3`, `pytest-cov==4.1.0` |

> **Note :** PyTorch est commenté dans `requirements.txt` — il est installé séparément dans le Dockerfile avec support CUDA.

---

## 3. Application principale (`app.py`)

### Middlewares & configuration au démarrage
- CORS autorisé pour `winplus.cm`, `www.winplus.cm`, `localhost:5173`, `localhost:3000`
- Rate limiting via **slowapi** (30 req/min par défaut)
- Authentification **HTTPBearer** (JWT)
- Initialisation au démarrage : `Database`, `NLPAnalyzer`, `Recommender`, `UserPerformanceAnalyzer`

### Endpoints publics (sans JWT)

| Route | Méthode | Limite | Description |
|-------|---------|--------|-------------|
| `/health` | GET | 30/min | Health check |
| `/api/subjects` | GET | 30/min | Liste des épreuves (filtres : `category`, `search`, `featured`, pagination) |
| `/api/subjects/{id}` | GET | 30/min | Détail d'une épreuve + contenus |
| `/api/categories` | GET | 30/min | Liste des catégories avec compteur |
| `/api/popular` | GET | 30/min | Épreuves populaires |

### Endpoints protégés (JWT requis)

| Route | Méthode | Limite | Description |
|-------|---------|--------|-------------|
| `/api/recommendations/{id}` | GET | 20/min | Sujets similaires via TF-IDF |
| `/api/analyze` | POST | 20/min | Analyse NLP d'un texte (difficulté, tags, durée) |
| `/api/recommend` | POST | 20/min | Recommandations personnalisées par `user_id` |
| `/api/analyze-progress` | POST | 20/min | Analyse complète de progression utilisateur |

### Endpoints admin

| Route | Méthode | Rôle requis | Description |
|-------|---------|-------------|-------------|
| `/api/admin/init-db` | POST | `admin` | Initialise les tables PostgreSQL |

### Problème identifié — code Flask legacy
Le fichier `app.py` contient du code **Flask résiduel** (lignes ~442–834) avec des décorateurs `@app.route()` et des `jsonify()`. Ces routes doublonnent les endpoints FastAPI mais ne sont jamais appelées. Elles doivent être supprimées.

---

## 4. Authentification (`auth.py`)

- Schéma : **Bearer JWT** (partagé avec le backend .NET)
- Algorithme : HS256
- Extraction du token : `HTTPBearer` FastAPI

### Fonctions

**`async verify_token(credentials) → UserTokenData`**
Valide la signature et l'expiration du JWT. Extrait `user_id`, `email`, `role`. Lève `401` si invalide.

**`require_role(*roles) → Callable`**
Dépendance FastAPI pour restreindre par rôle (`student`, `teacher`, `admin`). Lève `403` si insuffisant.

---

## 5. Base de données (`database.py`)

- Driver : **psycopg2-binary**
- ORM : **SQLAlchemy 2.0**
- Base : PostgreSQL 15 (schéma partagé avec le backend ASP.NET)

### Modèles ORM

| Modèle | Table | Colonnes clés |
|--------|-------|---------------|
| `Subject` | Subjects | Id, Title, Category, Price, IsPublished, EnrollmentCount, AverageRating, IsFeatured |
| `CourseContent` | CourseContents | Id, SubjectId, Title, VideoUrl, DocumentUrl, OrderIndex, DurationMinutes |
| `User` | Users | Id, Email, FirstName, LastName, Role |
| `Enrollment` | Enrollments | Id, UserId, SubjectId, ProgressPercentage, IsCompleted |
| `LearningHistory` | LearningHistories | Id, UserId, SubjectId, ActionType, Duration, Metadata (JSON) |
| `Conversation` | Conversations | Id, UserId, Title, IsActive, MessageCount |
| `ChatMessage` | ChatMessages | Id, ConversationId, Role, Content, TokensUsed |

### Méthodes publiques

| Méthode | Description |
|---------|-------------|
| `get_subject_by_id(id)` | Détail d'un sujet |
| `get_all_subjects(filters)` | Liste filtrée + paginée |
| `get_popular_subjects(limit)` | Tri EnrollmentCount + Rating |
| `get_categories()` | Catégories avec compteur |
| `get_user_enrollments(user_id)` | Inscriptions avec détails |
| `get_user_learning_history(user_id)` | Historique trié par timestamp |
| `get_user_progress_stats(user_id)` | Completion, temps, forces, faiblesses |

---

## 6. Schémas de validation (`schemas.py`)

### Enums
- `UserRole` : `student`, `teacher`, `admin`
- `LearningStyle` : `visual`, `auditory`, `reading_writing`, `kinesthetic`

### Schémas principaux

| Schéma | Usage |
|--------|-------|
| `SubjectResponse` | Réponse liste/détail épreuve |
| `AnalysisRequest` / `AnalysisResponse` | Analyse NLP |
| `RecommendationResponse` | Un sujet recommandé avec score |
| `ProgressAnalysisResponse` | Analyse complète progression user |
| `ChatRequest` / `ChatResponse` | Chatbot request/response |

---

## 7. Modèle NLP (`models/nlp_analyzer.py`)

- Modèle : **CamemBERT-base** (Hugging Face, français)
- Device : CUDA si disponible, sinon CPU
- Embedding : vecteur CLS de 768 dimensions

### Méthodes

**`analyze(text, metadata) → dict`**
Retourne : `difficulty_level`, `difficulty_score`, `estimated_duration_minutes`, `tags`, `complexity_metrics`, `embedding` (768-dim).

**`compute_similarity(text1, text2) → float`**
Similarité cosinus entre embeddings CamemBERT.

**`batch_encode(texts, batch_size=8) → ndarray`**
Encode plusieurs textes en batch.

### Heuristiques de difficulté
- **Facile** : introduction, base, débutant, élémentaire, fondamental…
- **Moyen** : intermédiaire, application, exercice, approfondir…
- **Difficile** : avancé, expert, théorique, algorithmique, maîtrise…

---

## 8. Système de recommandation (`models/recommender.py`)

- Algorithme : **TF-IDF + Cosine Similarity** (scikit-learn)
- Vectorizer : `TfidfVectorizer(max_features=100)` sur `title + description + category`

### Méthodes

| Méthode | Description |
|---------|-------------|
| `get_similar_subjects(id, top_n=5)` | Top-N sujets similaires au contenu |
| `get_popular_subjects(limit=10)` | Sujets populaires (enrollment + rating) |
| `get_personalized_recommendations(user_id)` | **Non implémenté** — retourne popular |
| `recommend_by_category(category)` | Sujets d'une catégorie |
| `get_trending_subjects(limit)` | Trending par EnrollmentCount |

> **Lacune :** `get_personalized_recommendations` n'utilise pas encore l'historique utilisateur.

---

## 9. Routes Chatbot (`routes/chatbot_routes.py`)

### Endpoints

| Route | Méthode | JWT | Description |
|-------|---------|-----|-------------|
| `/api/chatbot/health` | GET | Non | Santé du service DeepSeek |
| `/api/chatbot/chat` | POST | Oui | Chat complet avec historique |
| `/api/chatbot/stream` | POST | Oui | Streaming SSE temps réel |
| `/api/chatbot/complete` | POST | Oui | Complétion simple sans historique |

### Prompt système
Construit dynamiquement selon le profil utilisateur : niveau éducation, classe, matières inscrites, objectifs, style d'apprentissage. Directives : répondre en français, pédagogue, utiliser LaTeX, encourager.

### Format stream (`/api/chatbot/stream`)
```
data: {"delta": "...", "tokens_used": N}
data: [DONE]
```
Le message assistant est persisté en base après complétion du stream.

---

## 10. Client DeepSeek (`services/deepseek_client.py`)

- Endpoint : `POST /v1/chat/completions`
- Retry : 3 tentatives avec backoff exponentiel (`2^attempt` secondes)
- Timeout : 60 s

### Méthodes

| Méthode | Retour |
|---------|--------|
| `chat(messages, system_prompt, ...)` | `{success, content, tokens_used, generation_time_ms, model}` |
| `chat_stream(...)` | Générateur de chunks SSE |
| `health_check()` | `{status: healthy|unhealthy|unreachable}` |

Instancié en singleton via `get_deepseek_client()`.

---

## 11. Analyseur de performance (`services/user_performance_analyzer.py`)

### Méthodes principales

**`analyze_user_progress(user_id) → dict`**

Lit les enrollments et l'historique réels depuis PostgreSQL, puis calcule :

| Clé de retour | Description |
|---------------|-------------|
| `overview` | total enrolled, completed, completion_rate, learning_time_hours |
| `analysis` | learning_velocity, strengths, weak_areas, learning_pattern |
| `projections` | estimated_completion_date, remaining_days, daily_target |
| `recommendations` | Liste de conseils textuels |

**`get_personalized_recommendations(user_id, limit=5) → list`**
Score les sujets non inscrits selon catégories préférées + analyse NLP des descriptions.

**`generate_learning_path(user_id) → dict`**
Génère un parcours en 3 phases basé sur la vélocité d'apprentissage réelle :

| Phase | Durée | Focus | Cible |
|-------|-------|-------|-------|
| 1 — Renforcement | 14j / vélocité | Faiblesses, facile | 60 % |
| 2 — Approfondissement | 21j / vélocité | Forces, moyen | 85 % |
| 3 — Maîtrise | 28j / vélocité | Diversification, difficile | 95 % |

---

## 12. Infrastructure Docker

### `docker-compose.yml`

| Service | Image | Port | Rôle |
|---------|-------|------|------|
| `postgres` | postgres:15-alpine | 5432 | Base de données |
| `fastapi-app` | build local | 8000 | API principale |
| `adminer` | adminer:latest | 8080 | Interface web BD |

Réseau personnalisé `winplus_network` (bridge).

### `Dockerfile.fastapi`
Build multi-stage (builder + runtime) sur Python 3.11-slim. Healthcheck sur `GET /health` toutes les 30 s.

### `Dockerfile.aws`
**Attention :** ce fichier est un Dockerfile **.NET** (EducationalAI.dll) mal nommé — pas Python.

---

## 13. Problèmes identifiés

### Critiques

| # | Problème | Fichier | Action |
|---|----------|---------|--------|
| 1 | Code Flask legacy mélangé avec FastAPI | `app.py` lignes ~442–834 | Supprimer les routes `@app.route()` |
| 2 | `get_personalized_recommendations` non implémenté | `recommender.py` | Implémenter avec enrollments réels |
| 3 | `Dockerfile.aws` est du .NET, pas du Python | `Dockerfile.aws` | Renommer ou clarifier |

### Améliorations recommandées

| # | Sujet | État actuel | Recommandation |
|---|-------|-------------|----------------|
| 4 | Tests | Absents | Ajouter pytest avec couverture ≥ 80 % |
| 5 | Rate limiting | slowapi en mémoire | Passer sur Redis pour distribué |
| 6 | Cache requêtes | Aucun | Redis TTL 15 min sur subjects/categories |
| 7 | Logging | Basique | JSON structuré + Sentry ou Datadog |
| 8 | Indexation BD | Non vérifié | Index sur `Subjects.Category`, `Enrollments.UserId` |
| 9 | Pagination | Incohérente | Standardiser sur tous les endpoints |
| 10 | Secrets JWT | Partagé .NET/Python | Documenter la rotation, ne jamais committer |

---

## 14. Vue d'ensemble architecturale

```
                  ┌──────────────────────────────┐
                  │        FastAPI (app.py)        │
                  │                              │
                  │  Public routes  │  Protected  │
                  └────────┬────────┴──────┬──────┘
                           │               │
              ┌────────────┘               └────────────┐
              ▼                                         ▼
    ┌─────────────────┐                    ┌─────────────────────┐
    │   PostgreSQL     │                    │  Services / Routers  │
    │                 │                    │                     │
    │  Subjects        │◄───────────────────│  Chatbot Router     │
    │  Enrollments     │                    │  DeepSeek Client    │
    │  LearningHistory │                    │  Performance Anal.  │
    │  Conversations   │                    └─────────────────────┘
    └────────┬─────────┘                              │
             │                          ┌─────────────┴────────────┐
             │                          ▼                          ▼
             │               ┌─────────────────┐      ┌──────────────────┐
             └──────────────►│  NLP Analyzer   │      │   Recommender    │
                             │  (CamemBERT)    │      │   (TF-IDF)       │
                             └─────────────────┘      └──────────────────┘

Services externes :
  - DeepSeek API  (chatbot IA)
  - Hugging Face  (modèles BERT, cache local)
```

---

## 15. Résumé par fichier

| Fichier | Lignes | Rôle |
|---------|--------|------|
| `app.py` | ~830 | Entrée FastAPI + legacy Flask à nettoyer |
| `auth.py` | ~110 | Vérification JWT + contrôle de rôle |
| `database.py` | ~510 | ORM SQLAlchemy + requêtes métier |
| `schemas.py` | ~250 | Schémas Pydantic request/response |
| `models/nlp_analyzer.py` | ~355 | CamemBERT — difficulté, tags, durée |
| `models/recommender.py` | ~175 | TF-IDF — similarité de contenu |
| `routes/chatbot_routes.py` | ~350 | Chatbot + streaming SSE |
| `services/deepseek_client.py` | ~250 | Client DeepSeek avec retry |
| `services/user_performance_analyzer.py` | ~465 | Analyse progression + parcours |
| `Dockerfile.fastapi` | 46 | Image Docker multi-stage |
| `docker-compose.yml` | 79 | Stack locale PostgreSQL + FastAPI |
