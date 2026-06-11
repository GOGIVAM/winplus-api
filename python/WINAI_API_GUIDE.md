# WinAI — Guide d'intégration API

WinAI est l'assistant IA de la plateforme WinPlus. Il adapte automatiquement son comportement, son registre et ses capacités au **rôle de l'utilisateur connecté**. Côté utilisateur, il s'appelle toujours **WinAI** ; le modèle sous-jacent (DeepSeek) n'est jamais exposé.

---

## 1. Architecture du module de prompts

```
backend/python/services/prompt_builder.py
```

| Symbole | Type | Description |
|---|---|---|
| `UserContext` | `dataclass` | Profil utilisateur injecté dans le prompt |
| `build_system_prompt(ctx)` | `str` | Retourne le prompt système complet selon le rôle |

### UserContext

```python
@dataclass
class UserContext:
    role: str = "student"
    # student | teacher | parent | admin | organization

    first_name: Optional[str] = None
    education_level: Optional[str] = None   # "lycée", "université", …
    grade: Optional[str] = None             # "Terminale S", "L1", …
    enrolled_subjects: List[str] = []       # ["Mathématiques", "Physique"]
    objectives: List[str] = []              # ["Préparer le BAC", …]
    learning_style: Optional[str] = None   # visual | auditory | reading_writing | kinesthetic
    performance_history: Dict[str, float] = {}  # {"Maths": 14.5, "Physique": 11.0}
```

---

## 2. Comportement par rôle

| Rôle | Persona WinAI | Registre | Capacités spécifiques |
|---|---|---|---|
| `student` | Tuteur pédagogue | Bienveillant, encourageant | Exercices, révisions, LaTeX, fiches, guidage pas-à-pas (sans donner les réponses directes) |
| `parent` | Conseiller familial | Chaleureux, accessible | Traduction des résultats en conseils actionnables, orientation vers professionnels |
| `teacher` | Attaché éditorial | Professionnel, précis | Plans de cours, quiz, corrections types, reformulation de contenus |
| `admin` | Auditeur analytique | Factuel, structuré | Synthèses, KPIs textuels, détection d'anomalies (sans modification de données) |
| `organization` | Conseiller institutionnel | Stratégique, orienté ROI | Pilotage cohortes, rapports, conformité RGPD, recommandations stratégiques |

> **Fallback** : tout rôle inconnu utilise le prompt `student`.

---

## 3. Schéma de la requête (POST `/chatbot/chat` et `/chatbot/stream`)

```json
{
  "messages": [
    { "role": "user", "content": "Explique-moi la dérivation." }
  ],
  "user_context": {
    "role": "student",
    "first_name": "Amara",
    "education_level": "lycée",
    "grade": "Terminale S",
    "enrolled_subjects": ["Mathématiques", "Physique-Chimie"],
    "objectives": ["Préparer le BAC S"],
    "learning_style": "visual",
    "performance_history": {
      "Mathématiques": 14.5,
      "Physique-Chimie": 11.0
    }
  },
  "max_tokens": 2000,
  "temperature": 0.7
}
```

Tous les champs de `user_context` sont optionnels. Le champ `role` est lu en priorité depuis le `user_context` ; s'il est absent, il est déduit du JWT (`token_data.role`).

---

## 4. Exemples par rôle

### 4.1 Étudiant

```bash
curl -X POST https://api.winplus.app/chatbot/chat \
  -H "Authorization: Bearer <JWT_STUDENT>" \
  -H "Content-Type: application/json" \
  -d '{
    "messages": [{"role": "user", "content": "Je ne comprends pas les intégrales."}],
    "user_context": {
      "role": "student",
      "first_name": "Kofi",
      "grade": "L1",
      "enrolled_subjects": ["Analyse"],
      "learning_style": "kinesthetic"
    }
  }'
```

### 4.2 Parent

```bash
curl -X POST https://api.winplus.app/chatbot/chat \
  -H "Authorization: Bearer <JWT_PARENT>" \
  -H "Content-Type: application/json" \
  -d '{
    "messages": [{"role": "user", "content": "Mon fils a 9/20 en maths, que faire ?"}],
    "user_context": {
      "role": "parent",
      "first_name": "Marie",
      "performance_history": {"Mathématiques": 9.0}
    }
  }'
```

### 4.3 Enseignant

```bash
curl -X POST https://api.winplus.app/chatbot/chat \
  -H "Authorization: Bearer <JWT_TEACHER>" \
  -H "Content-Type: application/json" \
  -d '{
    "messages": [{"role": "user", "content": "Génère un quiz sur les dérivées pour la Terminale."}],
    "user_context": {
      "role": "teacher",
      "first_name": "Prof. Ndiaye",
      "enrolled_subjects": ["Mathématiques"],
      "education_level": "lycée"
    }
  }'
```

### 4.4 Admin

```bash
curl -X POST https://api.winplus.app/chatbot/chat \
  -H "Authorization: Bearer <JWT_ADMIN>" \
  -H "Content-Type: application/json" \
  -d '{
    "messages": [{"role": "user", "content": "Résume les KPIs du mois dernier."}],
    "user_context": { "role": "admin" }
  }'
```

### 4.5 Organisation

```bash
curl -X POST https://api.winplus.app/chatbot/chat \
  -H "Authorization: Bearer <JWT_ORG>" \
  -H "Content-Type: application/json" \
  -d '{
    "messages": [{"role": "user", "content": "Quel est le taux de complétion de notre cohorte ?"}],
    "user_context": { "role": "organization", "first_name": "Direction RH" }
  }'
```

---

## 5. Endpoint SSE (streaming)

```
POST /chatbot/stream
```

Même corps que `/chatbot/chat` avec le champ `conversation_id` optionnel (entier, identifiant de la conversation à persister).

Format des chunks SSE :

```
data: {"delta": "Bonjour ", "tokens_used": 0}

data: {"delta": "Amara", "tokens_used": 0}

data: [DONE]
```

Le message assistant est automatiquement persisté en base à la fin du stream si `conversation_id` est fourni.

---

## 6. Logging interne

Chaque requête produit une ligne de log structurée incluant `winai_role` :

```
INFO  Processing chat request from user 42 winai_role=teacher messages=3
INFO  Stream request from user 7, winai_role=student, conv_id=115
```

Ce champ permet de filtrer les traces par rôle pour l'analyse comportementale et l'amélioration des prompts.

---

## 7. Règles de sécurité des prompts

- WinAI ne mentionne jamais DeepSeek, OpenAI, ou tout autre modèle sous-jacent.
- Si l'utilisateur demande « Quel modèle es-tu ? », WinAI répond : *« Je suis WinAI, l'assistant IA de WinPlus. »*
- Aucun prompt système ne contient d'informations personnelles en dur ; toutes les données proviennent du `UserContext` injecté dynamiquement à chaque requête.
- Le rôle `admin` et le rôle `organization` ne modifient jamais les données ; ils lisent et synthétisent uniquement.
