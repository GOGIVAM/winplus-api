# 🎓 Service IA Éducative - Architecture Complète

Système d'intelligence artificielle pour personnaliser l'apprentissage, avec analyse NLP, recommandations et prédictions.

## 📋 Table des matières

- [Architecture](#architecture)
- [Prérequis](#prérequis)
- [Installation](#installation)
- [Démarrage rapide](#démarrage-rapide)
- [API Endpoints](#api-endpoints)
- [Exemples d'utilisation](#exemples-dutilisation)
- [Déploiement Docker](#déploiement-docker)
- [Phase 2 & 3](#roadmap-phases-suivantes)

---

## 🏗️ Architecture

```
┌─────────────────────────────────────────────┐
│         Frontend / Applications             │
└──────────────────┬──────────────────────────┘
                   │ HTTP/REST
        ┌──────────▼──────────┐
        │  API Gateway (.NET)  │  ← Port 5001
        │  - Orchestration     │
        │  - Validation        │
        │  - Logging           │
        └──────────┬───────────┘
                   │
        ┌──────────▼──────────────────┐
        │   FastApi AI Service (Python) │  ← Port 5000
        ├─────────────────────────────┤
        │ • NLP Analyzer              │
        │ • Recommender Engine        │
        │ • Success Predictor         │
        │ • Semantic Clustering       │
        └──────────┬──────────────────┘
                   │
        ┌──────────▼──────────┐
        │   PostgreSQL / SQLite│
        │   - users            │
        │   - contents         │
        │   - interactions     │
        └──────────────────────┘
```

---

## ✅ Prérequis

### Python (FastApi Service)
- Python 3.9+
- pip
- virtualenv (recommandé)

### C# (.NET Gateway)
- .NET 7.0 SDK ou supérieur
- Visual Studio 2022 / VS Code / Rider

### Base de données
- SQLite (développement) - déjà inclus
- PostgreSQL 15+ (production) - optionnel

### Docker (optionnel)
- Docker Engine 20+
- Docker Compose 2+

---

## 📦 Installation

### 1. Cloner le repository

```bash
git clone <your-repo>
cd educational-ai
```

### 2. Structure des dossiers

```
educational-ai/
├── python/                 # Service FastApi
│   ├── app.py
│   ├── database.py
│   ├── requirements.txt
│   ├── models/
│   │   ├── nlp_analyzer.py
│   │   └── recommender.py
│   └── .env
├── dotnet/                 # API Gateway .NET
│   ├── Program.cs
│   ├── Controllers/
│   │   └── AIController.cs
│   ├── Services/
│   │   └── AIServiceClient.cs
│   ├── Models/
│   │   └── DTOs.cs
│   └── appsettings.json
├── data/                   # Données générées
│   ├── users.csv
│   ├── contents.csv
│   └── interactions.csv
├── docker-compose.yml
└── README.md
```

### 3. Générer les données

```bash
python generate_data.py
```

Ceci va créer `data/users.csv`, `data/contents.csv`, `data/interactions.csv`.

### 4. Installation Python

```bash
cd python

# Créer un environnement virtuel
python -m venv venv
source venv/bin/activate  # Windows: venv\Scripts\activate

# Installer les dépendances
pip install -r requirements.txt

# Télécharger le modèle NLP (première fois seulement)
python -c "from sentence_transformers import SentenceTransformer; SentenceTransformer('paraphrase-multilingual-MiniLM-L12-v2')"
```

### 5. Installation .NET

```bash
cd dotnet
dotnet restore
dotnet build
```

---

## 🚀 Démarrage rapide

### Mode développement (local)

#### Terminal 1 : Lancer FastApi

```bash
cd python
source venv/bin/activate
python app.py
```

Serveur FastApi démarre sur `http://localhost:5000`

#### Terminal 2 : Lancer .NET Gateway

```bash
cd dotnet
dotnet run
```

API Gateway démarre sur `http://localhost:5001` (HTTPS: 5002)

#### Terminal 3 : Tester

```bash
# Health check FastApi
curl http://localhost:5000/health

# Health check .NET
curl http://localhost:5001/api/ai/health

# Recommandations via .NET Gateway
curl http://localhost:5001/api/ai/recommendations/1?limit=5
```

---

## 📡 API Endpoints

### FastApi Service (Python) - Port 5000

#### 1. Health Check
```http
GET /health
```

#### 2. Analyser un contenu (NLP)
```http
POST /api/v1/analyze_content
Content-Type: application/json

{
  "content_id": 1
}
```

Ou analyse directe :
```json
{
  "text": "Introduction à l'algèbre linéaire. Ce cours couvre les matrices...",
  "title": "Algèbre Linéaire"
}
```

**Réponse :**
```json
{
  "difficulty_score": 0.45,
  "difficulty_level": "moyen",
  "estimated_duration_minutes": 12,
  "tags": ["mathématiques"],
  "complexity_metrics": {
    "word_count": 150,
    "sentence_count": 8,
    "avg_word_length": 5.2,
    "avg_sentence_length": 18.75
  }
}
```

#### 3. Recommandations basiques
```http
GET /api/v1/recommendations?user_id=1&limit=10
```

**Réponse :**
```json
{
  "user_id": 1,
  "recommendations": [
    {
      "content_id": 12,
      "titre": "Algèbre linéaire",
      "theme": "mathématiques",
      "difficulte": 0.65,
      "score": 0.873,
      "description": "..."
    }
  ],
  "count": 10
}
```

#### 4. Recommandations personnalisées
```http
POST /api/v1/recommendations/personalized
Content-Type: application/json

{
  "user_id": 1,
  "theme": "mathématiques",
  "difficulty_range": [0.3, 0.7],
  "limit": 5
}
```

#### 5. Statistiques utilisateur
```http
GET /api/v1/users/1/stats
```

**Réponse :**
```json
{
  "user_id": 1,
  "profile": {
    "nom": "Dupont",
    "prenom": "Jean",
    "age": 25,
    "niveau": "intermédiaire",
    "objectif": "maîtriser les matrices"
  },
  "statistics": {
    "total_interactions": 45,
    "total_reussites": 32,
    "taux_reussite": 0.71,
    "avg_temps_passe": 850,
    "avg_clics": 12.3,
    "contenus_distincts": 18
  }
}
```

### .NET Gateway - Port 5001

Tous les endpoints FastApi sont accessibles via le gateway avec validation et gestion d'erreurs améliorées :

```http
GET  /api/ai/health
POST /api/ai/analyze
GET  /api/ai/recommendations/{userId}?limit=10
POST /api/ai/recommendations/personalized
GET  /api/ai/users/{userId}/stats
```

Les réponses sont wrappées dans un format uniforme :
```json
{
  "success": true,
  "data": { ... },
  "error": null,
  "timestamp": "2025-11-03T10:30:00Z"
}
```

---

## 💡 Exemples d'utilisation

### Python (requests)

```python
import requests

BASE_URL = "http://localhost:5001/api/ai"

# 1. Analyser un contenu
response = requests.post(f"{BASE_URL}/analyze", json={
    "text": "Introduction aux réseaux de neurones profonds",
    "title": "Deep Learning",
    "compute_embedding": False
})
analysis = response.json()
print(f"Difficulté: {analysis['data']['difficulty_level']}")

# 2. Obtenir des recommandations
response = requests.get(f"{BASE_URL}/recommendations/1?limit=5")
recs = response.json()
for rec in recs['data']['recommendations']:
    print(f"- {rec['titre']} (score: {rec['score']:.2f})")

# 3. Stats utilisateur
response = requests.get(f"{BASE_URL}/users/1/stats")
stats = response.json()
print(f"Taux de réussite: {stats['data']['statistics']['taux_reussite']:.1%}")
```

### C# (.NET)

```csharp
using System.Net.Http.Json;

var client = new HttpClient { BaseAddress = new Uri("http://localhost:5001") };

// 1. Analyser un contenu
var analyzeRequest = new {
    text = "Introduction aux réseaux de neurones profonds",
    title = "Deep Learning"
};
var response = await client.PostAsJsonAsync("/api/ai/analyze", analyzeRequest);
var analysis = await response.Content.ReadFromJsonAsync<ApiResponse<NLPAnalysisResult>>();

// 2. Recommandations
var recs = await client.GetFromJsonAsync<ApiResponse<RecommendationResponse>>(
    "/api/ai/recommendations/1?limit=5"
);

foreach (var rec in recs.Data.Recommendations) {
    Console.WriteLine($"- {rec.Titre} (score: {rec.Score:F2})");
}
```

### cURL

```bash
# Analyser un contenu
curl -X POST http://localhost:5001/api/ai/analyze \
  -H "Content-Type: application/json" \
  -d '{"content_id": 1}'

# Recommandations personnalisées
curl -X POST http://localhost:5001/api/ai/recommendations/personalized \
  -H "Content-Type: application/json" \
  -d '{
    "userId": 1,
    "theme": "programmation",
    "difficultyRange": [0.4, 0.8],
    "limit": 5
  }'

# Stats utilisateur
curl http://localhost:5001/api/ai/users/1/stats
```

---

## 🐳 Déploiement Docker

### Lancer tous les services

```bash
# Construire et démarrer
docker-compose up -d

# Vérifier les logs
docker-compose logs -f

# Arrêter
docker-compose down
```

Les services seront accessibles sur :
- PostgreSQL: `localhost:5432`
- FastApi: `localhost:5000`
- .NET Gateway: `localhost:5001`

### Dockerfile Python (créer dans `python/Dockerfile`)

```dockerfile
FROM python:3.11-slim

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Télécharger le modèle NLP
RUN python -c "from sentence_transformers import SentenceTransformer; SentenceTransformer('paraphrase-multilingual-MiniLM-L12-v2')"

COPY . .

EXPOSE 5000

CMD ["gunicorn", "-w", "4", "-b", "0.0.0.0:5000", "app:app"]
```

### Dockerfile .NET (créer dans `dotnet/Dockerfile`)

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:7.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 80
ENTRYPOINT ["dotnet", "EducationalAI.dll"]
```

---

## 🗺️ Roadmap (Phases suivantes)

### ✅ Phase 1 (MVP) - COMPLÉTÉ
- [x] Collecte & stockage des données
- [x] Module NLP (difficulté des exercices)
- [x] Module Recommandation hybride
- [x] API FastApi + Gateway .NET

### 🚧 Phase 2 - Prédiction & Clustering
- [ ] **Success Prediction**: ML model pour prédire les chances de réussite
- [ ] **Clustering sémantique**: Grouper les exercices par concepts
- [ ] Endpoints API pour ces features

### 🔮 Phase 3 - Intelligence avancée
- [ ] **Assistant conversationnel**: Intégration d'un LLM (GPT-4, Claude)
- [ ] **Génération de plans d'étude**: Parcours optimisés adaptatifs
- [ ] Dashboard analytics (React/Vue frontend)

### 🔧 Phase 4 - MLOps
- [ ] Pipeline CI/CD (GitHub Actions)
- [ ] Monitoring (Prometheus + Grafana)
- [ ] A/B Testing des modèles
- [ ] Retraining automatique

---

## 🔒 Sécurité & Production

### Recommandations pour la production :

1. **Authentication** : Implémenter JWT ou OAuth2
2. **Rate Limiting** : Limiter les appels API (FastApi-Limiter)
3. **HTTPS** : Certificats SSL/TLS obligatoires
4. **Secrets** : Utiliser Azure KeyVault / AWS Secrets Manager
5. **Logging** : Centraliser avec ELK Stack ou Datadog
6. **Monitoring** : Alertes sur erreurs / latence

### Variables d'environnement sensibles

```bash
# NE JAMAIS commit ces valeurs en production !
DB_PASSWORD=<strong_password>
JWT_SECRET_KEY=<random_secure_key>
API_KEY=<api_key_for_external_services>
```

---

## 🤝 Contribution

Contributions bienvenues ! Pour proposer des améliorations :

1. Fork le repo
2. Créer une branche (`git checkout -b feature/amazing-feature`)
3. Commit (`git commit -m 'Add amazing feature'`)
4. Push (`git push origin feature/amazing-feature`)
5. Ouvrir une Pull Request

---

## 📄 Licence

MIT License - voir `LICENSE` file

---

## 📞 Support

Pour toute question :
- **Issues GitHub** : Ouvrir un ticket
- **Email** : support@educational-ai.example.com

---

**Dernière mise à jour** : Novembre 2025 | **Version** : 1.0.0-mvp