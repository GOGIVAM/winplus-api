# WinPlus — Architecture Système (État cible)

> Document de référence — généré le 2026-06-04  
> Reflète les décisions d'architecture arrêtées et l'état cible de l'union front/back/BD/IA.

---

## 1. Vue d'ensemble

WinPlus est une plateforme éducative camerounaise permettant aux élèves, parents et enseignants d'accéder à des annales corrigées, de s'entraîner via des quiz, d'interagir avec un assistant IA pédagogique, et d'échanger sur un forum communautaire.

### Couches du système

```
┌────────────────────────────────────────────────┐
│  FRONTEND  (React + Vite, hash-router)          │
│  m:/win/winplus/frontend/                       │
└─────────────────────┬──────────────────────────┘
                      │ HTTPS — Bearer JWT
┌─────────────────────▼──────────────────────────┐
│  BACKEND .NET  (ASP.NET Core 8, port 5001/7023) │
│  m:/win/winplus/backend/dotnet/                 │
│  Auth JWT · CRUD · Paiements · Notifications    │
└──────────┬──────────────────────┬──────────────┘
           │ HTTP (FastApiClient) │ ntfy HTTP API
           │ port 8000            │ (self-hosted AWS)
┌──────────▼─────────┐  ┌────────▼───────────────┐
│  MODULE IA Python  │  │  ntfy Server            │
│  FastAPI 0.104+    │  │  Push notifications     │
│  DeepSeek · NLP    │  └────────────────────────┘
│  SSE Streaming     │
└──────────┬─────────┘
           │ PostgreSQL 14+ (partagé)
┌──────────▼──────────────────────────────────────┐
│  BASE DE DONNÉES  PostgreSQL (AWS RDS / EC2)     │
│  Host : 172.31.20.230:5432  DB : winplus_db      │
└─────────────────────────────────────────────────┘
```

### Services tiers retenus

| Service | Rôle | Remplace |
|---------|------|----------|
| **NotchPay** | Paiements Mobile Money (MTN + Orange) | MtnMomoService, OrangeMoneyService, WaveService, Stripe, PayPal |
| **Resend** | Emails transactionnels | SendGrid |
| **ntfy** (self-hosted) | Push notifications in-app + alertes internes | Twilio, Cognito notifications |
| **DeepSeek** | LLM chatbot | — |
| **AWS S3** | Stockage fichiers PDF, avatars | — (conservé) |

---

## 2. Stack technique

### Frontend
- React 18 + Vite (JSX + TypeScript mixte)
- Routing hash-based (`window.location.hash`)
- Auth : JWT stocké dans `localStorage` (`winplus_access_token`, `winplus_refresh_token`)
- HTTP : Axios avec intercepteurs (refresh automatique à 401)
- CSS : Variables CSS globales + modules CSS par page

### Backend .NET
- ASP.NET Core 8.0, C#, Entity Framework Core 8
- PostgreSQL via Npgsql
- JWT Bearer (HS256, secret partagé avec Python)
- BCrypt pour les mots de passe
- Polly : retry + circuit breaker vers le service Python
- Resend SDK pour les emails

### Module IA Python
- FastAPI 0.104+, Python 3.11
- SQLAlchemy 2.0 (connexion à la même BD PostgreSQL)
- DeepSeek via HTTP client (stream=True pour SSE)
- CamemBERT (HuggingFace) pour analyse NLP
- TF-IDF (scikit-learn) pour recommandations
- SlowAPI pour le rate limiting

---

## 3. Authentification

### Principe général
L'authentification est **entièrement gérée par le backend .NET** via JWT HS256. Il n'y a plus de Cognito. Le module Python partage le même secret JWT et valide les tokens lui-même (pas de délégation vers .NET).

### Flux d'inscription
1. Formulaire frontend : prénom, nom, téléphone, email, mot de passe, rôle
2. `POST /api/auth/signup` → .NET crée l'utilisateur (password hashé BCrypt), envoie un code OTP 6 chiffres par email (Resend)
3. `POST /api/auth/verify-email` → code validé, `IsEmailVerified = true`
4. Retour : `{ accessToken, refreshToken, user }`

### Flux de connexion
1. `POST /api/auth/signin` → .NET vérifie email + password BCrypt, génère tokens JWT
2. AccessToken : 1440 min (24h), RefreshToken : 30 jours
3. Frontend stocke les tokens dans `localStorage`

### Refresh automatique
- À chaque réponse 401, l'intercepteur Axios appelle `POST /api/auth/refresh`
- Si le refresh échoue : déconnexion et redirection vers login

### Routes canoniques Auth

| Méthode | Route | Auth | Description |
|---------|-------|------|-------------|
| POST | `/api/auth/signup` | ❌ | Inscription |
| POST | `/api/auth/signin` | ❌ | Connexion |
| POST | `/api/auth/refresh` | ❌ | Renouveler le token |
| POST | `/api/auth/logout` | ✅ | Déconnexion |
| POST | `/api/auth/verify-email` | ❌ | Valider le code OTP |
| POST | `/api/auth/resend-verification` | ❌ | Renvoyer le code |
| POST | `/api/auth/forgot-password` | ❌ | Demande de reset |
| POST | `/api/auth/reset-password` | ❌ | Réinitialiser le mot de passe |
| POST | `/api/auth/change-password` | ✅ | Changer le mot de passe |

### Rôles
- `student` — accès dashboard, chat, panier, téléchargements
- `parent` — accès espace parent, suivi enfants
- `teacher` — accès espace enseignant, publications
- `admin` — accès total + panel d'administration

---

## 4. Profil utilisateur

### Règle canonique des routes
> L'ID utilisateur est **toujours extrait du JWT** côté serveur. Les routes utilisateur n'incluent jamais d'ID dans le chemin.  
> Exception : les routes admin utilisent `/admin/users/{id}` avec ID explicite.

| Méthode | Route | Auth | Description |
|---------|-------|------|-------------|
| GET | `/api/users/profile` | ✅ | Récupérer son profil |
| PUT | `/api/users/profile` | ✅ | Modifier prénom, nom, téléphone, bio, niveau, ville |
| POST | `/api/users/profile/avatar` | ✅ | Upload avatar (multipart, stockage S3) |
| GET | `/api/users/profile/statistics` | ✅ | KPIs personnels (score, streak, téléchargements) |
| GET | `/api/users/profile/subscriptions` | ✅ | Plan actif, dates, historique |

---

## 5. Paiements — NotchPay

### Principe
**Tous les paiements passent exclusivement par NotchPay.** Il n'y a plus de MtnMomoService, OrangeMoneyService, WaveService, Stripe, ni PayPal. Les paiements bancaires sont hors scope pour le lancement.

### Flux de paiement complet

```
1. Utilisateur saisit son numéro de téléphone dans le frontend
2. Frontend → POST /api/payments/initiate
   body: { orderId, phone, amount, currency: "XAF" }
3. Backend → NotchPay API : crée la transaction
   NotchPay retourne : { transactionRef, checkoutUrl?, status: "pending" }
4. NotchPay → envoie le prompt USSD/Push Mobile Money sur le téléphone
5. Utilisateur confirme sur son téléphone
6. NotchPay → webhook POST /api/payments/webhook/notchpay
   body: { reference, status: "complete"|"failed"|"expired", ... }
7. Backend traite le webhook → met à jour Order + Payment
8. Frontend poll GET /api/payments/{id}/status jusqu'à résolution
   (ou notification ntfy push si le user est en ligne)
```

### Gestion d'erreurs NotchPay (exhaustive)

| Cas | Comportement backend | Message frontend |
|-----|---------------------|-----------------|
| Numéro invalide | Réponse 400 immédiate | "Numéro de téléphone invalide pour Mobile Money" |
| Solde insuffisant | Webhook `status: failed`, code `insufficient_balance` | "Solde Mobile Money insuffisant" |
| Timeout (> 3 min sans action) | Webhook `status: expired` | "Délai de confirmation dépassé, réessayez" |
| Opérateur down | Erreur réseau NotchPay → retry x3 → 503 | "Service Mobile Money temporairement indisponible" |
| Webhook manqué | Job de reconciliation toutes les 15 min via `GET /transactions/{ref}` | — (transparent) |
| Transaction expirée (> 24h pending) | Cron job → mark as `expired`, libère le stock | — (transparent) |
| Double paiement | Unicité `orderId` côté NotchPay + BD | "Cette commande a déjà été payée" |

### Routes canoniques Paiements

| Méthode | Route | Auth | Description |
|---------|-------|------|-------------|
| POST | `/api/payments/initiate` | ✅ | Initier un paiement NotchPay |
| POST | `/api/payments/webhook/notchpay` | ❌ (signature HMAC) | Webhook de confirmation NotchPay |
| GET | `/api/payments/{id}/status` | ✅ | Vérifier le statut d'un paiement |
| GET | `/api/payments/history` | ✅ | Historique paiements de l'utilisateur (JWT) |
| POST | `/api/payments/{id}/retry` | ✅ | Retenter un paiement échoué |
| GET | `/api/admin/payments` | ✅ Admin | Liste tous les paiements (paginated) |
| GET | `/api/admin/payments/user/{userId}` | ✅ Admin | Paiements d'un utilisateur spécifique |

### Champs requis dans la table `Payments`
- `notchpayReference` VARCHAR(255) — référence unique NotchPay
- `phoneNumber` VARCHAR(20) — numéro appelé
- `operator` VARCHAR(50) — `mtn` | `orange` (détecté par NotchPay)
- `currency` VARCHAR(3) DEFAULT `'XAF'` (remplace `'EUR'`)
- Les anciens champs Stripe/PayPal (`clientSecret`, `paymentMethodId`) sont supprimés

---

## 6. Notifications — ntfy + Resend

### ntfy (push in-app)
ntfy est auto-hébergé sur l'instance AWS existante. Le backend .NET publie des messages via l'API HTTP de ntfy. Le frontend s'abonne au topic de l'utilisateur pour recevoir les notifications en temps réel.

**Topics ntfy :**
- `winplus-user-{userId}` — notifications personnelles (paiement confirmé, nouveau message forum, etc.)
- `winplus-admin` — alertes internes pour l'administrateur (transaction échouée, erreur critique, nouvel utilisateur)

**Événements déclenchant une notification ntfy :**
| Événement | Topic | Titre | Priorité |
|-----------|-------|-------|----------|
| Paiement confirmé | `winplus-user-{id}` | "Paiement reçu ✓" | high |
| Paiement échoué | `winplus-user-{id}` | "Paiement échoué" | high |
| Téléchargement disponible | `winplus-user-{id}` | "Votre épreuve est prête" | default |
| Réponse sur son fil forum | `winplus-user-{id}` | "Nouvelle réponse à votre discussion" | default |
| Nouveau message chatbot | — | (géré par SSE) | — |
| Transaction suspecte | `winplus-admin` | "Alerte paiement" | urgent |
| Erreur service IA | `winplus-admin` | "Module IA indisponible" | high |

**Routes ntfy sur le backend :**

| Méthode | Route | Auth | Description |
|---------|-------|------|-------------|
| GET | `/api/notifications` | ✅ | Liste les notifications BD de l'utilisateur |
| PUT | `/api/notifications/{id}/read` | ✅ | Marquer comme lue |
| PUT | `/api/notifications/read-all` | ✅ | Tout marquer comme lu |

### Resend (emails transactionnels)
Resend remplace SendGrid et l'ancien SMTP Gmail.

**Emails envoyés :**
| Déclencheur | Template |
|-------------|---------|
| Inscription | Code de vérification OTP |
| Reset password | Lien de réinitialisation (valable 1h) |
| Paiement confirmé | Reçu de paiement |
| Abonnement expiré (J-7) | Rappel de renouvellement |
| Changement de mot de passe | Confirmation de sécurité |
| Nouveau device détecté | Alerte de connexion |

**From address :** `support@winplus.cm` (domaine à configurer dans Resend)

---

## 7. Chatbot IA — DeepSeek + SSE Streaming

### Architecture

```
Frontend (Chat.jsx)
  │ fetch POST /api/chatbot/stream  (Authorization: Bearer JWT)
  │ ReadableStream (SSE lines: "data: {delta}\n\n")
  ▼
.NET ChatbotController
  │ Crée/met à jour la conversation en BD
  │ Récupère le contexte utilisateur
  │ Proxy HTTP → FastAPI /api/chatbot/stream
  ▼
FastAPI Python (chatbot_routes.py)
  │ Construit les messages + system prompt enrichi
  │ Appelle DeepSeek API avec stream=True
  │ Lit le stream chunk par chunk
  │ Re-émet chaque token via StreamingResponse (text/event-stream)
  ▼
DeepSeek API (stream=True)
```

### Format SSE émis par FastAPI

```
data: {"delta": "Bonjour", "tokens_used": 1}

data: {"delta": " !", "tokens_used": 2}

data: [DONE]
```

En cas d'erreur SSE, le frontend bascule automatiquement sur `POST /api/chatbot/message` (mode non-streaming).

### Routes canoniques Chatbot

| Méthode | Route | Auth | Description |
|---------|-------|------|-------------|
| POST | `/api/chatbot/stream` | ✅ | Streaming SSE (mode principal) |
| POST | `/api/chatbot/message` | ✅ | Réponse complète (fallback) |
| GET | `/api/chatbot/conversations` | ✅ | Liste les conversations |
| POST | `/api/chatbot/conversations` | ✅ | Créer une nouvelle conversation |
| GET | `/api/chatbot/conversations/{id}` | ✅ | Détail conversation + messages |
| PUT | `/api/chatbot/conversations/{id}` | ✅ | Renommer la conversation |
| DELETE | `/api/chatbot/conversations/{id}` | ✅ | Supprimer la conversation |
| POST | `/api/chatbot/messages/{id}/feedback` | ✅ | Like/dislike un message |
| GET | `/api/chatbot/context` | ✅ | Récupérer le contexte IA de l'utilisateur |
| POST | `/api/chatbot/context/sync` | ✅ | Mettre à jour le contexte IA |

### Contexte utilisateur injecté dans le system prompt
- Niveau d'étude, classe
- Matières inscrites
- Objectifs (concours cible)
- Style d'apprentissage

### Sélection automatique du modèle DeepSeek
- Mots-clés scientifiques détectés → `deepseek-coder`
- Sinon → `deepseek-chat`

---

## 8. Catalogue & Sujets

### Routes canoniques Sujets

| Méthode | Route | Auth | Description |
|---------|-------|------|-------------|
| GET | `/api/subjects` | ❌ | Liste paginée (`page`, `pageSize`, `sortBy`, `sortOrder`, `q`, `category`, `difficulty`) |
| GET | `/api/subjects/{id}` | ❌ | Détail d'un sujet |
| GET | `/api/subjects/{id}/similar` | ❌ | Sujets similaires (via module IA TF-IDF) |
| GET | `/api/subjects/{id}/download` | ✅ | Retourne `{ downloadUrl, filename }` (URL S3 signée) |
| GET | `/api/subjects/categories` | ❌ | Liste des catégories disponibles |
| POST | `/api/subjects` | ✅ Admin | Créer un sujet |
| PUT | `/api/subjects/{id}` | ✅ Admin | Modifier un sujet |
| DELETE | `/api/subjects/{id}` | ✅ Admin | Soft-delete |
| POST | `/api/admin/subjects/{id}/pdf` | ✅ Admin | Upload PDF vers S3 |

### Structure normalisée Subject (retournée par l'API)

```json
{
  "id": 1,
  "title": "Mathématiques BAC C 2023",
  "exam": "bac-c",
  "category": "Mathématiques",
  "year": 2023,
  "session": "Juin",
  "difficulty": "hard",
  "price": 2500,
  "isFree": false,
  "hasCorrection": true,
  "downloadCount": 1420,
  "averageRating": 4.7,
  "totalRatings": 89,
  "thumbnailUrl": "https://s3.../thumb.jpg",
  "isPublished": true
}
```

---

## 9. Forum

### Principe
Le forum est entièrement géré côté backend .NET. Les entités `ForumThread` et `ForumPost` doivent être créées en base de données (actuellement absentes).

### Routes canoniques Forum

| Méthode | Route | Auth | Description |
|---------|-------|------|-------------|
| GET | `/api/forums/threads` | ❌ | Liste (`category`, `page`, `pageSize`) |
| POST | `/api/forums/threads` | ✅ | Créer un fil (`title`, `content`, `category`, `tag`) |
| GET | `/api/forums/threads/{id}/posts` | ❌ | Posts d'un fil |
| POST | `/api/forums/threads/{id}/posts` | ✅ | Répondre à un fil |
| POST | `/api/forums/posts/{id}/vote` | ✅ | Voter up/down |
| DELETE | `/api/forums/threads/{id}` | ✅ | Supprimer son fil (ou admin) |
| POST | `/api/forums/posts/{id}/accept` | ✅ | Marquer comme solution acceptée |

### Tables BD à créer
- `ForumThreads` : id, userId, title, content, category, tag, isPinned, isSolved, viewsCount, repliesCount, upvotes, createdAt, updatedAt, isDeleted
- `ForumPosts` : id, threadId, userId, content, upvotes, isAccepted, createdAt, updatedAt, isDeleted
- `ForumVotes` : id, postId, userId, type (`up`|`down`), createdAt — UNIQUE(postId, userId)

---

## 10. Dashboard & Statistiques

### Route principale enrichie

`GET /api/student/stats` retourne tout en une seule réponse :

```json
{
  "overview": {
    "averageScore": 78.5,
    "scoreDelta": +3.2,
    "totalDownloads": 34,
    "weeklyDownloads": 5,
    "currentStreak": 7,
    "longestStreak": 21,
    "studyTimeFormatted": "14h cette semaine"
  },
  "priorities": [
    { "subject": "Physique", "urgency": "high", "nextExam": "BAC C" }
  ],
  "goals": [
    { "id": 1, "label": "Réussir l'ENSP", "progress": 42 }
  ],
  "upcomingEvents": [
    { "title": "Révision Maths", "date": "2026-06-10", "type": "revision" }
  ],
  "weeklyStudyHours": [2, 3, 1.5, 4, 2, 0, 3],
  "monthlyScores": [72, 75, 78, 79]
}
```

### Autres routes dashboard

| Méthode | Route | Auth | Description |
|---------|-------|------|-------------|
| GET | `/api/student/stats` | ✅ | Toutes les statistiques + priorités + goals + events |
| GET | `/api/users/profile/statistics` | ✅ | KPIs personnels (téléchargements, quiz, streak) |
| GET | `/api/users/profile/subscriptions` | ✅ | Plan actif + features + historique + invoices |
| GET | `/api/student/learning/continue` | ✅ | Cours à reprendre |
| GET | `/api/student/exams/recommended` | ✅ | Examens recommandés |
| GET | `/api/history` | ✅ | Historique activités (`type`: Tous/Téléchargements/Quiz/Révisions) |
| GET | `/api/payments/history` | ✅ | Historique paiements |

---

## 11. Plans & Abonnements

### Routes Pricing

| Méthode | Route | Auth | Description |
|---------|-------|------|-------------|
| GET | `/api/pricing/plans` | ❌ | Tous les plans disponibles |
| GET | `/api/pricing/promotions` | ❌ | Promotions actives |
| GET | `/api/pricing/compare` | ❌ | Tableau comparatif des plans |

### Structure d'un plan retourné

```json
{
  "id": "premium",
  "name": "Premium",
  "price": 5000,
  "currency": "XAF",
  "billingPeriod": "monthly",
  "isPopular": true,
  "features": [
    "Accès illimité aux épreuves",
    "Corrections complètes",
    "Chatbot IA sans limite",
    "Quiz personnalisés"
  ],
  "maxDownloads": null,
  "maxChatMessages": null
}
```

### Gestion des abonnements
- Table `Subscriptions` : userId, pricingPlanId, startDate, endDate, status, createdAt
- À l'expiration : cron job qui passe le status à `expired` et rétrograde l'accès
- Renouvellement : nouveau paiement NotchPay → nouvelle ligne Subscription

---

## 12. Administration

### Routes Admin

| Méthode | Route | Auth | Description |
|---------|-------|------|-------------|
| GET | `/api/admin/users` | ✅ Admin | Liste utilisateurs (`q`, `page`, `pageSize`) |
| PUT | `/api/admin/users/{id}/role` | ✅ Admin | Changer le rôle |
| PUT | `/api/admin/users/{id}/status` | ✅ Admin | Activer/désactiver (`{ isActive }`) |
| GET | `/api/admin/analytics` | ✅ Admin | Dashboard analytics global |
| GET | `/api/admin/subjects` | ✅ Admin | Liste sujets |
| POST | `/api/admin/subjects` | ✅ Admin | Créer un sujet |
| PUT | `/api/admin/subjects/{id}` | ✅ Admin | Modifier un sujet |
| DELETE | `/api/admin/subjects/{id}` | ✅ Admin | Supprimer un sujet |
| POST | `/api/admin/subjects/{id}/pdf` | ✅ Admin | Upload PDF (S3) |
| GET | `/api/admin/payments` | ✅ Admin | Tous les paiements |
| GET | `/api/admin/payments/user/{userId}` | ✅ Admin | Paiements d'un utilisateur |

---

## 13. Panier & Commandes

### Routes Panier

| Méthode | Route | Auth | Description |
|---------|-------|------|-------------|
| GET | `/api/cart` | ✅/❌ | Récupérer le panier (anonyme avec deviceId ou authentifié) |
| POST | `/api/cart/items` | ✅ | Ajouter un sujet (`{ subjectId }`) |
| DELETE | `/api/cart/items/{id}` | ✅ | Retirer un item |
| DELETE | `/api/cart` | ✅ | Vider le panier |
| POST | `/api/cart/promo` | ✅ | Appliquer un code promo (`{ code }`) |
| DELETE | `/api/cart/promo` | ✅ | Retirer le code promo |
| POST | `/api/cart/sync` | ✅ | Synchroniser panier local → serveur à la connexion |

### Routes Commandes

| Méthode | Route | Auth | Description |
|---------|-------|------|-------------|
| POST | `/api/orders` | ✅ | Créer une commande |
| GET | `/api/orders` | ✅ | Liste des commandes de l'utilisateur |
| GET | `/api/orders/{id}` | ✅ | Détail d'une commande |

---

## 14. Module IA Python — Endpoints

| Méthode | Route | Auth | Description |
|---------|-------|------|-------------|
| GET | `/health` | ❌ | Santé FastAPI + DeepSeek |
| POST | `/api/chatbot/stream` | ✅ JWT | **Streaming SSE** (principal) |
| POST | `/api/chatbot/chat` | ✅ JWT | Réponse complète (fallback) |
| POST | `/api/recommend` | ✅ JWT | Recommandations personnalisées (TF-IDF) |
| GET | `/api/recommendations/{subjectId}` | ✅ JWT | Sujets similaires |
| POST | `/api/analyze-progress` | ✅ JWT | Analyse de progression |
| POST | `/api/get-performance` | ✅ JWT | Métriques de performance |
| POST | `/api/generate-learning-path` | ✅ JWT | Parcours d'apprentissage adapté |
| GET | `/api/subjects` | ❌ | Sujets (vue Python, pour recommandations) |
| POST | `/api/analyze` | ✅ JWT | Analyse NLP d'un texte |

---

## 15. Base de données — Tables cibles

### Modifications à apporter au schéma existant

**Table `Payments`** :
- Ajouter : `notchpayReference VARCHAR(255)`, `phoneNumber VARCHAR(20)`, `operator VARCHAR(50)`
- Modifier : `currency DEFAULT 'XAF'` (remplace `'EUR'`)
- Supprimer : champs Stripe/PayPal spécifiques

**Table `Users`** :
- `CognitoId` → passer à nullable, préparer migration de suppression
- Garder pour compatibilité descendante jusqu'à nettoyage complet

**Tables à créer** :
- `ForumThreads` (voir section 9)
- `ForumPosts` (voir section 9)
- `ForumVotes` (voir section 9)

**Table `Orders`** :
- `paymentMethod VARCHAR(50)` → valeurs possibles : `'mobile_money'` uniquement (hors scope : carte)

---

## 16. Variables d'environnement (état cible)

### Backend .NET (`appsettings.json` / secrets)

```env
# DB
DB_HOST=172.31.20.230
DB_PORT=5432
DB_NAME=winplus_db
DB_USER=...
DB_PASSWORD=...

# JWT (partagé avec Python)
JWT_SECRET_KEY=<32+ chars>
JWT_ISSUER=WinPlusApp
JWT_AUDIENCE=WinPlusUsers

# NotchPay
NOTCHPAY_PUBLIC_KEY=...
NOTCHPAY_PRIVATE_KEY=...
NOTCHPAY_WEBHOOK_SECRET=...
NOTCHPAY_BASE_URL=https://api.notchpay.co

# Resend (emails)
RESEND_API_KEY=...
RESEND_FROM=support@winplus.cm

# ntfy (push)
NTFY_BASE_URL=http://localhost:8080  # ou URL publique AWS
NTFY_ADMIN_TOPIC=winplus-admin
NTFY_AUTH_TOKEN=...  # si ntfy configuré avec auth

# AWS S3
AWS_ACCESS_KEY_ID=...
AWS_SECRET_ACCESS_KEY=...
AWS_BUCKET=winplus-bucket
AWS_REGION=eu-west-1

# Module IA
AI_SERVICE_URL=http://localhost:8000
```

### Module Python FastAPI (`.env`)

```env
DATABASE_URL=postgresql://user:pass@172.31.20.230:5432/winplus_db
JWT_SECRET_KEY=<même clé que .NET>
JWT_ALGORITHM=HS256

DEEPSEEK_API_KEY=...
DEEPSEEK_BASE_URL=https://api.deepseek.com
DEEPSEEK_MODEL=deepseek-chat
DEEPSEEK_STREAM=true

NLP_MODEL=camembert-base
```

### Frontend (`.env`)

```env
VITE_API_URL=http://localhost:5001/api  # dev
# ou
VITE_API_URL=https://api.winplus.cm/api  # prod

VITE_NTFY_URL=https://ntfy.winplus.cm  # ou URL publique ntfy
```

---

## 17. Règles d'architecture retenues

1. **L'ID utilisateur vient toujours du JWT**, jamais d'un paramètre URL côté user.
2. **Tous les paiements passent par NotchPay** — aucun provider direct.
3. **Les emails transactionnels passent par Resend** — pas de SMTP direct.
4. **Les notifications push passent par ntfy** — un topic par utilisateur.
5. **Le streaming chatbot est SSE via FastAPI** — le frontend consomme le stream natif, sans `revealText`.
6. **Le forum existe en base de données** — tables `ForumThreads`, `ForumPosts`, `ForumVotes` à migrer.
7. **Les routes admin incluent l'ID** : `/admin/users/{id}` — les routes user non : `/users/profile`.
8. **La devise est `XAF`** partout (base de données, API, affichage).
9. **S3 est conservé** pour les PDFs et avatars — bucket `winplus-bucket` existant.
10. **Le module Python partage la même BD PostgreSQL** que le .NET — pas de BD séparée.
