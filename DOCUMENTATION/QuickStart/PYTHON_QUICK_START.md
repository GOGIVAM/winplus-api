# 🚀 Guide de Démarrage Rapide - Service IA Éducative

## ⏱️ Démarrage en 5 minutes

### Étape 1 : Générer les données (1 min)

```bash
# Exécuter le script de génération
python generate_data.py

# Vérifier que les fichiers sont créés
ls data/
# Devrait afficher: users.csv  contents.csv  interactions.csv
```

### Étape 2 : Installer Python (2 min)

```bash
cd python

# Créer l'environnement virtuel
python -m venv venv

# Activer (Linux/Mac)
source venv/bin/activate
# OU activer (Windows)
venv\Scripts\activate

# Installer les dépendances
pip install -r requirements.txt

# Télécharger le modèle NLP (seulement la première fois)
python -c "from sentence_transformers import SentenceTransformer; SentenceTransformer('paraphrase-multilingual-MiniLM-L12-v2')"
```

**Note**: Le téléchargement du modèle NLP (~500MB) peut prendre quelques minutes.

### Étape 3 : Lancer FastApi (30 sec)

```bash
# Dans le dossier python/ avec venv activé
python app.py
```

✅ FastApi devrait démarrer sur `http://localhost:5000`

### Étape 4 : Tester FastApi (30 sec)

Ouvrir un **nouveau terminal** :

```bash
# Test 1: Health check
curl http://localhost:5000/health

# Test 2: Recommandations pour user_id=1
curl "http://localhost:5000/api/v1/recommendations?user_id=1&limit=3"

# Test 3: Analyse NLP
curl -X POST http://localhost:5000/api/v1/analyze_content \
  -H "Content-Type: application/json" \
  -d '{"content_id": 1}'
```

Si tout fonctionne, vous devriez voir des réponses JSON !

### Étape 5 : Lancer .NET Gateway (1 min) - OPTIONNEL

```bash
cd dotnet
dotnet restore
dotnet run
```

✅ Gateway .NET démarre sur `http://localhost:5001`

Tester :
```bash
curl http://localhost:5001/api/ai/health
curl "http://localhost:5001/api/ai/recommendations/1?limit=3"
```

---

## 🧪 Script de Test Automatique

Pour tester tous les endpoints automatiquement :

```bash
# Installer colorama pour les couleurs (optionnel)
pip install colorama

# Lancer le script de test
python test_api.py
```

Ce script va :
- ✅ Tester tous les endpoints FastApi
- ✅ Tester tous les endpoints .NET Gateway
- ✅ Vérifier la gestion d'erreurs
- ✅ Mesurer les performances

---

## 📊 Données générées

Le script `generate_data.py` a créé :

| Fichier | Contenu | Nombre d'enregistrements |
|---------|---------|--------------------------|
| `users.csv` | Profils utilisateurs | 100 |
| `contents.csv` | Contenus éducatifs | 50 |
| `interactions.csv` | Historique d'interactions | 2000 |

### Structure des données

**users.csv**
```csv
user_id,nom,prenom,age,niveau,objectif
1,Dupont,Jean,25,intermédiaire,maîtriser les matrices
```

**contents.csv**
```csv
content_id,titre,theme,difficulte,description
1,Introduction aux matrices,mathématiques,0.45,Cours complet sur...
```

**interactions.csv**
```csv
interaction_id,user_id,content_id,clics,temps_passe,reussite,timestamp
1,42,15,8,650,True,2024-11-15 14:30:22
```

---

## 🎯 Endpoints Principaux

### 1. Analyse NLP

**Analyser un contenu par ID**
```bash
curl -X POST http://localhost:5000/api/v1/analyze_content \
  -H "Content-Type: application/json" \
  -d '{"content_id": 5}'
```

**Analyser un texte directement**
```bash
curl -X POST http://localhost:5000/api/v1/analyze_content \
  -H "Content-Type: application/json" \
  -d '{
    "text": "Introduction aux algorithmes de tri complexes et optimisation",
    "title": "Algorithmes avancés"
  }'
```

**Réponse attendue**
```json
{
  "difficulty_score": 0.67,
  "difficulty_level": "moyen",
  "estimated_duration_minutes": 8,
  "tags": ["programmation"],
  "complexity_metrics": {
    "word_count": 120,
    "sentence_count": 6,
    "avg_word_length": 5.8,
    "avg_sentence_length": 20.0
  }
}
```

### 2. Recommandations

**Recommandations basiques**
```bash
curl "http://localhost:5000/api/v1/recommendations?user_id=1&limit=5"
```

**Recommandations filtrées**
```bash
curl -X POST http://localhost:5000/api/v1/recommendations/personalized \
  -H "Content-Type: application/json" \
  -d '{
    "user_id": 1,
    "theme": "programmation",
    "difficulty_range": [0.4, 0.8],
    "limit": 5
  }'
```

**Réponse attendue**
```json
{
  "user_id": 1,
  "recommendations": [
    {
      "content_id": 12,
      "titre": "Python pour débutants",
      "theme": "programmation",
      "difficulte": 0.35,
      "score": 0.873,
      "description": "Cours complet sur..."
    }
  ],
  "count": 5
}
```

### 3. Statistiques Utilisateur

```bash
curl http://localhost:5000/api/v1/users/1/stats
```

**Réponse attendue**
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
    "avg_temps_passe": 850.5,
    "avg_clics": 12.3,
    "contenus_distincts": 18
  }
}
```

---

## 🔧 Dépannage

### Problème : "Module not found"

```bash
# Vérifier que venv est activé
which python  # devrait pointer vers venv/bin/python

# Réinstaller les dépendances
pip install -r requirements.txt
```

### Problème : "Connection refused" sur FastApi

```bash
# Vérifier que FastApi tourne
ps aux | grep python

# Vérifier les logs
# Dans le terminal où FastApi tourne, regarder les erreurs

# Relancer FastApi
python app.py
```

### Problème : "Model not found" (NLP)

```bash
# Télécharger manuellement le modèle
python -c "from sentence_transformers import SentenceTransformer; SentenceTransformer('paraphrase-multilingual-MiniLM-L12-v2')"
```

### Problème : Données manquantes

```bash
# Regénérer les données
python generate_data.py

# Vérifier la création
ls -lh data/
```

### Problème : Performance lente

Le premier appel NLP peut être lent (chargement du modèle). Les appels suivants sont rapides.

```bash
# Forcer le préchargement au démarrage
# Le modèle se charge automatiquement dans app.py
```

---

## 🐳 Déploiement Docker (Alternative)

Si tu préfères Docker (plus simple, pas besoin d'installer Python/packages) :

```bash
# Construire et lancer tous les services
docker-compose up -d

# Vérifier les logs
docker-compose logs -f fastapi_service

# Arrêter tout
docker-compose down
```

**Services accessibles** :
- FastApi: `http://localhost:5000`
- .NET Gateway: `http://localhost:5001`
- PostgreSQL: `localhost:5432`

---

## 📝 Configuration Personnalisée

### Modifier le port FastApi

Éditer `.env` :
```bash
FLASK_PORT=8000
```

Puis relancer :
```bash
python app.py
```

### Utiliser PostgreSQL au lieu de SQLite

Éditer `.env` :
```bash
DB_TYPE=postgresql
DB_USER=postgres
DB_PASSWORD=your_password
DB_HOST=localhost
DB_PORT=5432
DB_NAME=educational_ai
```

Initialiser la DB :
```bash
python -c "from database import init_db; init_db()"
```

### Changer le modèle NLP

Éditer `.env` :
```bash
# Modèles disponibles sur HuggingFace
NLP_MODEL=sentence-transformers/paraphrase-multilingual-mpnet-base-v2
```

---

## 🎓 Exemples de cas d'usage

### Cas 1 : Tableau de bord utilisateur

Récupérer toutes les données d'un utilisateur :

```python
import requests

user_id = 1
base_url = "http://localhost:5000/api/v1"

# Stats
stats = requests.get(f"{base_url}/users/{user_id}/stats").json()

# Recommandations
recs = requests.get(f"{base_url}/recommendations?user_id={user_id}&limit=10").json()

# Afficher
print(f"Utilisateur: {stats['profile']['prenom']} {stats['profile']['nom']}")
print(f"Taux de réussite: {stats['statistics']['taux_reussite']*100:.1f}%")
print(f"\nRecommandations:")
for rec in recs['recommendations']:
    print(f"  - {rec['titre']} (score: {rec['score']:.2f})")
```

### Cas 2 : Analyse de contenu en masse

```python
import requests
import pandas as pd

base_url = "http://localhost:5000/api/v1"

# Charger tous les contenus
contents_df = pd.read_csv('data/contents.csv')

# Analyser chaque contenu
for _, row in contents_df.iterrows():
    response = requests.post(f"{base_url}/analyze_content", json={
        "content_id": int(row['content_id'])
    })
    
    if response.status_code == 200:
        analysis = response.json()
        print(f"{row['titre']}: {analysis['difficulty_level']}")
```

### Cas 3 : Recommandations personnalisées

```python
import requests

# Utilisateur débutant en mathématiques
response = requests.post("http://localhost:5000/api/v1/recommendations/personalized", json={
    "user_id": 1,
    "theme": "mathématiques",
    "difficulty_range": [0.1, 0.4],  # Facile
    "limit": 5
})

recs = response.json()
print(f"Recommandations faciles en maths: {recs['count']}")
```

---

## ✅ Checklist de vérification

Avant de passer à la Phase 2, vérifie que tout fonctionne :

- [ ] Les 3 fichiers CSV sont générés dans `data/`
- [ ] FastApi démarre sans erreur sur port 5000
- [ ] `/health` retourne status 200
- [ ] `/api/v1/recommendations?user_id=1` retourne des résultats
- [ ] `/api/v1/analyze_content` analyse correctement un texte
- [ ] Le script `test_api.py` passe tous les tests
- [ ] (Optionnel) .NET Gateway démarre sur port 5001

---

## 🚀 Prochaines étapes (Phase 2)

Une fois le MVP validé, tu peux implémenter :

1. **Success Prediction** : Prédire si un utilisateur va réussir un test
2. **Clustering sémantique** : Grouper les exercices par concepts similaires
3. **Dashboard React/Vue** : Interface visuelle pour les recommandations
4. **Authentication JWT** : Sécuriser l'API

Consulte `README.md` pour plus de détails sur la roadmap !

---

**Bon développement ! 🎉**

*Dernière mise à jour : Novembre 2025*