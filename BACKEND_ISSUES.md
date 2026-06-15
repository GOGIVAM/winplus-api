# Backend Issues — Endpoints à implémenter ou corriger

> Document généré pour l'équipe backend .NET. Chaque section liste les endpoints requis par le dashboard admin frontend.

---

## Connexion .NET ↔ FastAPI — Audit

### Architecture en production

Le backend est composé de **deux services distincts** :

| Service | Rôle | URL interne |
|---|---|---|
| **.NET (ASP.NET Core)** | API principale — utilisateurs, paiements, contenu, analytics admin | `https://api.winplus.cm` (public) |
| **FastAPI (Python)** | Service IA — chat conversationnel, recommandations, WinAI | `http://172.31.1.71:5000` (interne) |
| **FastAPI Analytics** | Service secondaire — stats WinAI, topics | `http://172.31.1.71:8000` (interne) |

Configuration actuelle dans `appsettings.Production.json` :
```json
"FastApi": { "BaseUrl": "http://172.31.1.71:5000" },
"AIService": { "BaseUrl": "http://172.31.1.71:8000" }
```

### Circuit breaker désactivé en production

Le circuit breaker est **désactivé** en production (`"Enabled": false`). Cela signifie que si FastAPI ne répond pas, les requêtes .NET vers FastAPI restent bloquées jusqu'au timeout au lieu d'échouer rapidement. **Recommandation : activer le circuit breaker** avec un seuil de 3 échecs consécutifs et un timeout de 5s.

### Hypothèse pour les 500 sur `/admin/analytics` et `/admin/activities/recent`

Ces endpoints .NET font probablement des appels internes vers FastAPI (`http://172.31.1.71:8000`) pour enrichir les données analytiques. Si FastAPI est indisponible ou lent, ces appels échouent → 500 côté frontend.

**Solution recommandée :** Rendre les appels FastAPI optionnels dans ces endpoints. Si FastAPI timeout ou retourne une erreur, retourner quand même les données .NET disponibles (sans enrichissement IA).

### Endpoints `/admin/chat/*` et `/admin/winai/*` absents des deux backends

Ces endpoints ne sont implémentés ni dans .NET ni dans FastAPI. Le frontend les détecte comme 404 et affiche "en cours de déploiement". Ces URLs sont désormais cachées après le premier 404 de la session (pas de re-poll).

---

## Fuite d'information — Moteur IA sous-jacent

Les réponses FastAPI incluent des champs qui révèlent le moteur IA utilisé :

### `POST /chat/message` → `ChatResponse`
```json
{
  "content": "...",
  "model": "deepseek-chat",   ← À MASQUER
  "tokens_used": 142
}
```

### `GET /recommendations` → `RecommendationsListResponse`
```json
{
  "recommendations": [...],
  "models_used": ["deepseek-chat"]   ← À MASQUER
}
```

**Action requise :** Supprimer les champs `model` et `models_used` des réponses JSON publiques (ou ne les envoyer que dans les headers de debug en développement). Le frontend n'utilise pas ces champs — ils ne servent qu'à exposer l'implémentation interne.

---

## Endpoints manquants (404 — non implémentés)

Ces endpoints retournent actuellement 404. Le frontend affiche un état "en cours de déploiement" à la place d'une erreur.

### 1. Certificats
```
GET /admin/certificates?limit=200
```
**Réponse attendue :**
```json
[
  {
    "id": "string|number",
    "studentName": "string",
    "studentEmail": "string",
    "courseName": "string",
    "issuedAt": "ISO8601",
    "score": 85,
    "certificateUrl": "string|null"
  }
]
```

### 2. Plans d'abonnement
```
GET /admin/subscriptions/plans
```
**Réponse attendue :**
```json
[
  {
    "id": "string|number",
    "name": "string",
    "price": 5900,
    "interval": "month|year",
    "features": ["string"],
    "targetRole": "student|teacher|parent",
    "activeCount": 42,
    "isActive": true
  }
]
```

### 3. Statistiques d'abonnements
```
GET /admin/subscriptions/stats
```
**Réponse attendue :**
```json
{
  "total": 1200,
  "monthly": 800,
  "yearly": 400,
  "churnRate": 2.1
}
```

### 4. Logs système
```
GET /admin/logs?limit=200
```
**Réponse attendue :**
```json
[
  {
    "id": "string|number",
    "timestamp": "ISO8601",
    "severity": "info|warn|error|fatal",
    "message": "string",
    "source": "string",
    "occurrences": 3,
    "resolved": false
  }
]
```

### 5. Statistiques WinAI
```
GET /admin/winai/stats
```
**Réponse attendue :**
```json
{
  "totalConversations": 450,
  "activeUsers": 120,
  "avgMessagesPerSession": 6.4,
  "satisfactionRate": 87,
  "topTopics": [
    { "topic": "Mathématiques BAC C", "count": 142 }
  ],
  "recentConversations": [
    { "userId": "string", "userName": "string", "messages": 8, "lastAt": "ISO8601" }
  ]
}
```

### 6. Sessions de chat
```
GET /admin/chat/sessions?limit=100
```
**Réponse attendue :**
```json
[
  {
    "id": "string|number",
    "participantName": "string",
    "participantEmail": "string",
    "participantRole": "student|teacher|parent|admin",
    "subject": "string",
    "messagesCount": 12,
    "lastMessage": "string",
    "lastAt": "ISO8601",
    "status": "active|closed|reported",
    "unread": 2
  }
]
```

### 7. Statistiques de chat
```
GET /admin/chat/stats
```
**Réponse attendue :**
```json
{
  "totalSessions": 340,
  "activeNow": 18,
  "messagesToday": 92,
  "reportedCount": 3
}
```

---

## Endpoints en erreur (500 — bugs serveur)

Ces endpoints existent mais retournent 500. Le frontend affiche un état d'erreur avec bouton "Réessayer" et log console en DEV.

### 1. Revenus / Analytics
```
GET /admin/analytics?period=7d
```
Paramètre `period` : `1d`, `7d`, `30d`, `90d`

**Réponse attendue :**
```json
{
  "revenue": 1250000,
  "totalRevenue": 1250000,
  "revenueGrowth": 12.5,
  "avgOrderValue": 8500,
  "completedOrders": 147,
  "conversionRate": 4.2,
  "byMonth": [
    { "label": "Jan", "value": 95000 }
  ],
  "topProducts": [
    { "name": "BAC C Mathématiques", "revenue": 145000, "orders": 24 }
  ]
}
```

### 2. Journal d'activité / Audit
```
GET /admin/activities/recent?limit=200
```
**Réponse attendue :**
```json
[
  {
    "id": "string|number",
    "timestamp": "ISO8601",
    "userName": "string",
    "userEmail": "string",
    "action": "user|content|order|payment|system",
    "target": "string",
    "status": "success|failure|warning"
  }
]
```

---

## Endpoint sur-sollicité (ERR_INSUFFICIENT_RESOURCES — corrigé côté frontend)

### Paiements
```
GET /admin/payments?limit=50
```
**Ancienne valeur :** `limit=500` → causait ERR_INSUFFICIENT_RESOURCES. Réduit à 50 côté frontend.
**Action backend recommandée :** implémenter la pagination server-side et supporter `?page=N&limit=50`.

**Réponse attendue :**
```json
[
  {
    "id": "string",
    "orderNumber": "ORD-10001",
    "customerName": "string",
    "customerEmail": "string",
    "items": 2,
    "total": 11800,
    "status": "pending|processing|completed|cancelled",
    "date": "ISO8601",
    "paymentMethod": "MTN MoMo|Orange Money|Carte"
  }
]
```

---

## Endpoints existants à vérifier (utilisés mais sans confirmation)

| Endpoint | Composant | Méthode |
|---|---|---|
| `/admin/analytics` | AdminJourney | GET |
| `/admin/analytics/geo` | AdminJourney | GET |
| `/admin/analytics/active-users` | AdminOverview | GET |
| `/admin/system/health` | AdminSystemHealth | GET |
| `/admin/users` | AdminUsers | GET |
| `/admin/contents` | AdminContents | GET |
| `/admin/chat/sessions/:id/messages` | AdminChat | GET |
| `/admin/chat/sessions/:id/close` | AdminChat | PATCH |
| `/admin/chat/messages/:id` | AdminChat | DELETE |
| `/admin/logs/:id/resolve` | AdminLogs | PATCH |

---

## Actions urgentes (priorité haute)

1. **Implémenter `/admin/analytics`** — endpoint critique pour les revenus (actuellement 500)
2. **Implémenter `/admin/logs`** — nécessaire pour le monitoring des erreurs
3. **Implémenter `/admin/subscriptions/*`** — bloque la gestion des plans
4. **Corriger `/admin/activities/recent`** — le journal d'audit retourne 500

## Actions secondaires (priorité normale)

5. Implémenter `/admin/certificates`
6. Implémenter `/admin/winai/stats`
7. Implémenter `/admin/chat/sessions` + `/admin/chat/stats`
8. Ajouter pagination sur `/admin/payments` (actuellement limité à 50 côté frontend)
