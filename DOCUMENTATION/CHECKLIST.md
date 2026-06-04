# WinPlus — Checklist d'alignement complet

> Générée le 2026-06-04  
> Couvre : Frontend (F) · Backend .NET (B) · Module IA Python (P) · Base de données (BD) · Infrastructure (I)  
> Légende : 🔴 Critique (bloque le fonctionnement) · 🟠 Important (bloque une feature) · 🟡 Secondaire (amélioration)

---

## 1. 🔐 Auth & Sessions

### Corrections urgentes
- [ ] 🔴 **F** — `authService.ts` : changer `/auth/refresh-token` → `/auth/refresh` (chemin incorrect)
- [ ] 🔴 **BD** — Supprimer la contrainte `CognitoId UNIQUE` ou la rendre nullable (colonne orpheline, Cognito supprimé)
- [ ] 🟠 **B** — Supprimer toute référence à AWS Cognito dans le code .NET (SDK, config, middlewares)
- [ ] 🟡 **BD** — Préparer migration de suppression de la colonne `CognitoId` (après vérification qu'aucun user en prod n'en dépend)

### Vérifications
- [ ] 🟠 **B** — Vérifier que `POST /api/auth/refresh` retourne bien `{ accessToken, refreshToken }` (format attendu par le frontend)
- [ ] 🟠 **B** — Vérifier que le token JWT inclut les claims : `user_id`, `email`, `role`, `email_verified`
- [ ] 🟡 **F** — Ajouter `authLoading` dans App.jsx pour éviter le flash LoginPrompt au démarrage ✅ (déjà fait)

---

## 2. 💳 Paiements — NotchPay

### Supprimer (backend)
- [ ] 🔴 **B** — Supprimer `MtnMomoService.cs` et toute sa configuration
- [ ] 🔴 **B** — Supprimer `OrangeMoneyService.cs` et toute sa configuration
- [ ] 🔴 **B** — Supprimer `WaveService.cs` et toute sa configuration
- [ ] 🔴 **B** — Supprimer `StripeService.cs` et toute sa configuration
- [ ] 🔴 **B** — Supprimer `PayPalService.cs` et toute sa configuration
- [ ] 🔴 **B** — Retirer les packages NuGet Stripe, PayPal du `.csproj`
- [ ] 🟠 **appsettings** — Supprimer les sections `MtnMomo`, `OrangeMoney`, `Wave`, `Stripe`, `PayPal`

### Créer — NotchPayService
- [ ] 🔴 **B** — Créer `NotchPayService.cs` avec :
  - `InitiatePaymentAsync(phone, amount, orderId, description)` → appelle `POST https://api.notchpay.co/payments`
  - `GetTransactionStatusAsync(reference)` → appelle `GET https://api.notchpay.co/payments/{reference}`
  - `VerifyWebhookSignature(payload, signature)` → HMAC-SHA256 avec `NOTCHPAY_WEBHOOK_SECRET`
- [ ] 🔴 **B** — Créer `POST /api/payments/initiate` (remplace les anciens endpoints MTN/Orange/Wave)
  - Body : `{ orderId, phone, currency: "XAF" }`
  - Retour : `{ paymentId, reference, status: "pending" }`
- [ ] 🔴 **B** — Créer `POST /api/payments/webhook/notchpay`
  - Vérification HMAC-SHA256 de la signature NotchPay
  - Mise à jour `Payment.status` + `Order.status`
  - Envoi notification ntfy si paiement confirmé
  - Envoi email Resend si paiement confirmé
- [ ] 🔴 **B** — Créer `GET /api/payments/{id}/status` (polling frontend)
- [ ] 🟠 **B** — Créer job de réconciliation (toutes les 15 min) : vérifier les paiements `pending` > 5 min via `GetTransactionStatus`
- [ ] 🟠 **B** — Créer job d'expiration (toutes les heures) : passer à `expired` les paiements `pending` > 24h

### Gestion d'erreurs NotchPay
- [ ] 🔴 **B** — Mapper les codes d'erreur NotchPay :
  - `invalid_phone` → 400 + message utilisateur
  - `insufficient_balance` → 402 + message utilisateur
  - `operator_unavailable` → 503 + retry automatique (x3)
  - `transaction_expired` → 408 + message utilisateur
  - `duplicate_transaction` → 409 + message utilisateur
- [ ] 🟠 **B** — Logger toutes les erreurs NotchPay avec le référence de transaction
- [ ] 🟠 **B** — Alerter `winplus-admin` via ntfy en cas d'erreur 5xx NotchPay

### Modifications Base de données
- [ ] 🔴 **BD** — Migration : ajouter colonne `NotchpayReference VARCHAR(255)` dans `Payments`
- [ ] 🔴 **BD** — Migration : ajouter colonne `PhoneNumber VARCHAR(20)` dans `Payments`
- [ ] 🔴 **BD** — Migration : ajouter colonne `Operator VARCHAR(50)` dans `Payments` (`mtn`|`orange`)
- [ ] 🔴 **BD** — Migration : changer `Currency DEFAULT 'EUR'` → `DEFAULT 'XAF'` dans `Payments`
- [ ] 🟠 **BD** — Supprimer les colonnes Stripe/PayPal spécifiques si elles existent

### Frontend
- [ ] 🔴 **F** — `CheckoutPage` : retirer le formulaire carte bancaire (hors scope)
- [ ] 🔴 **F** — `CheckoutPage` : formulaire de paiement = saisie numéro de téléphone uniquement
- [ ] 🔴 **F** — `paymentService.ts` : remplacer tous les appels providers par `POST /api/payments/initiate`
- [ ] 🔴 **F** — `CheckoutPage` : implémenter le polling `GET /api/payments/{id}/status` avec spinner
- [ ] 🟠 **F** — `CheckoutPage` : afficher les messages d'erreur NotchPay (numéro invalide, solde insuffisant, etc.)
- [ ] 🟠 **F** — `paymentService.ts` : supprimer `GET /users/{id}/payment-methods` (pas de carte = pas de méthodes sauvegardées)
- [ ] 🟡 **F** — Ajouter un timer visuel côté checkout ("Attendez le prompt sur votre téléphone…" avec countdown 3 min)

---

## 3. 🔔 Notifications — ntfy + Resend

### ntfy (push in-app)
- [ ] 🔴 **I** — Installer et configurer ntfy sur l'instance AWS (port 8080, derrière nginx)
- [ ] 🔴 **I** — Configurer l'authentification ntfy (token ou basic auth)
- [ ] 🔴 **B** — Créer `NtfyService.cs` :
  - `PublishAsync(topic, title, message, priority, tags[])` → HTTP POST vers ntfy
  - Topics : `winplus-user-{userId}` + `winplus-admin`
- [ ] 🔴 **B** — Appeler `NtfyService` après chaque paiement confirmé (webhook)
- [ ] 🟠 **B** — Appeler `NtfyService` quand une réponse arrive sur un fil forum de l'utilisateur
- [ ] 🟠 **B** — Appeler `NtfyService` sur `winplus-admin` en cas d'erreur critique (service IA down, transaction échouée > seuil)
- [ ] 🟠 **B** — Endpoints notifications :
  - `GET /api/notifications` — liste BD (paginated, non lues en premier)
  - `PUT /api/notifications/{id}/read` — marquer comme lue
  - `PUT /api/notifications/read-all` — tout marquer comme lu
- [ ] 🟡 **F** — Connecter le frontend au topic ntfy de l'utilisateur (EventSource ou fetch streaming) pour les alertes en temps réel
- [ ] 🟡 **F** — Icône cloche dans TopNav avec badge compteur non lues

### Resend (emails)
- [ ] 🔴 **B** — Ajouter SDK Resend (`Resend` NuGet package)
- [ ] 🔴 **B** — Créer `ResendEmailService.cs` (remplace `SendGrid EmailService.cs`)
- [ ] 🔴 **B** — Migrer tous les appels `EmailService` vers `ResendEmailService` :
  - Vérification email (OTP)
  - Reset mot de passe
  - Paiement confirmé (reçu)
  - Alerte nouveau device
  - Rappel abonnement expirant
- [ ] 🔴 **B** — Configurer le domaine `winplus.cm` dans Resend (DNS SPF/DKIM)
- [ ] 🟠 **B** — Supprimer la dépendance `SendGrid` du `.csproj`
- [ ] 🟠 **appsettings** — Remplacer config `SendGrid` → `Resend` (`RESEND_API_KEY`, `RESEND_FROM`)

---

## 4. 🤖 Chatbot — SSE Streaming

### Module Python FastAPI
- [ ] 🔴 **P** — `deepseek_client.py` : passer `"stream": True` dans l'appel à DeepSeek
- [ ] 🔴 **P** — `deepseek_client.py` : ajouter méthode `chat_stream()` qui yield les chunks
- [ ] 🔴 **P** — `chatbot_routes.py` : créer `POST /api/chatbot/stream`
  - Auth JWT
  - Rate limit : 30/minute
  - `StreamingResponse(generator, media_type="text/event-stream")`
  - Format émis : `data: {"delta": "...", "tokens_used": N}\n\n` puis `data: [DONE]\n\n`
- [ ] 🟠 **P** — Gérer la déconnexion client en cours de stream (catch `GeneratorExit`)
- [ ] 🟠 **P** — Persister le message complet en BD à la fin du stream

### Backend .NET
- [ ] 🔴 **B** — `ChatbotController` : ajouter `POST /api/chatbot/stream`
  - Crée/récupère la conversation en BD
  - Crée le message utilisateur en BD
  - Proxy SSE vers FastAPI `/api/chatbot/stream` avec `HttpCompletionOption.ResponseHeadersRead`
  - Re-émet les chunks SSE vers le client
  - À la fin : crée le message assistant en BD avec le contenu accumulé
- [ ] 🟠 **B** — Ajouter l'endpoint `/api/chatbot/context` (GET) et `/api/chatbot/context/sync` (POST) s'ils sont absents

### Frontend
- [ ] 🟠 **F** — `Chat.jsx` : supprimer `revealText()` (désormais inutile, le vrai stream remplace l'animation) ✅ (déjà fait côté SSE, mais `revealText` reste pour le fallback)
- [ ] 🟡 **F** — Afficher un indicateur "streaming en cours" (curseur clignotant) pendant la réception

---

## 5. 👤 Profil utilisateur — Routes canoniques

### Backend
- [ ] 🔴 **B** — Vérifier que `PUT /api/users/profile` accepte : `firstName`, `lastName`, `phone`, `bio`, `level`, `city`
- [ ] 🔴 **B** — Vérifier/créer `POST /api/users/profile/avatar` (multipart, upload S3)
- [ ] 🔴 **B** — Créer `GET /api/users/profile/statistics` : téléchargements total, quizzes tentés, score moyen, streak
- [ ] 🔴 **B** — Créer `GET /api/users/profile/subscriptions` : plan actif, features, date expiration, historique invoices

### Frontend
- [ ] 🔴 **F** — `Dashboard.jsx` ProfileTab : changer `PUT /users/${id}` → `PUT /users/profile`
- [ ] 🔴 **F** — `Dashboard.jsx` ProfileTab : changer `POST /users/${id}/avatar` → `POST /users/profile/avatar`
- [ ] 🔴 **F** — `usersService.js` : aligner toutes les routes sur `/users/profile` (sans ID)
- [ ] 🟠 **F** — `Dashboard.jsx` SubscriptionTab : changer `GET /users/${id}/subscriptions` → `GET /users/profile/subscriptions`
- [ ] 🟠 **F** — `dashboardService.ts` : changer `GET /users/${id}/statistics` → `GET /users/profile/statistics`

---

## 6. 📊 Dashboard & Statistiques

### Backend
- [ ] 🔴 **B** — Enrichir `GET /api/student/stats` pour inclure `priorities`, `goals`, `upcomingEvents` (éviter les endpoints séparés)
- [ ] 🟠 **B** — Vérifier/créer `GET /api/student/learning/continue`
- [ ] 🟠 **B** — Vérifier/créer `GET /api/student/exams/recommended`
- [ ] 🟠 **B** — `GET /api/history` : s'assurer qu'il supporte le param `type` (`all`|`downloads`|`quiz`|`revisions`)
- [ ] 🟠 **B** — `GET /api/payments/history` : créer cet endpoint (user depuis JWT, paginé)

### Frontend
- [ ] 🟠 **F** — `dashboardService.ts` : retirer les appels séparés `/student/priorities/today`, `/student/events/upcoming`, `/student/goals` (fusionnés dans `/student/stats`)
- [ ] 🟠 **F** — `Dashboard.jsx` PaymentsTab : utiliser `GET /api/payments/history` ✅ (déjà configuré)

---

## 7. 💰 Pricing & Abonnements

### Backend
- [ ] 🟠 **B** — Vérifier que `GET /api/pricing/plans` existe et retourne la liste complète avec features
- [ ] 🟠 **B** — Créer `GET /api/pricing/promotions` : retourne les codes promo actifs (publics)
- [ ] 🟠 **B** — Créer `GET /api/pricing/compare` : tableau comparatif structuré des plans
- [ ] 🟡 **B** — Cron job : vérifier quotidiennement les abonnements expirant dans 7 jours → email Resend de rappel

### Frontend
- [ ] 🟠 **F** — `pricingService.js` : changer `GET /pricing` → `GET /pricing/plans` ✅ (à confirmer dans le fichier)
- [ ] 🟡 **F** — `Pricing.jsx` : ajouter section promotions si `GET /pricing/promotions` retourne des données

### Base de données
- [ ] 🟠 **BD** — Vérifier que la table `PricingPlans` contient les 4 plans (Découverte/Standard/Premium/Ultime) avec les features JSON
- [ ] 🟠 **BD** — Vérifier que la table `Subscriptions` a bien : userId, pricingPlanId, startDate, endDate, status, renewalCount

---

## 8. 📚 Sujets & Catalogue

### Backend
- [ ] 🔴 **B** — Créer/vérifier `GET /api/subjects/{id}/download` : retourne `{ downloadUrl (S3 signed), filename }`
- [ ] 🔴 **B** — Vérifier que `GET /api/subjects` accepte les query params : `q` (search), `category`, `difficulty`, `page`, `pageSize`, `sortBy`
- [ ] 🟠 **B** — Créer `GET /api/subjects/{id}/similar` : délègue au module Python `/api/recommendations/{id}`
- [ ] 🟠 **B** — Vérifier que `GET /api/subjects/categories` existe
- [ ] 🟠 **B** — Vérifier que `POST /api/admin/subjects/{id}/pdf` upload vers S3 et met à jour `DocumentUrl` en BD

### Frontend
- [ ] 🟠 **F** — `subjectsService.js` : vérifier que `search()` utilise le param `q` sur `/subjects` (pas une route `/subjects/search` séparée)
- [ ] 🟠 **F** — `Catalog.jsx` : vérifier que le toast download utilise bien le format `toast.push("...", "type")` ✅ (corrigé)

---

## 9. 🏛️ Forum

### Base de données (CRITIQUE — tables absentes)
- [ ] 🔴 **BD** — Créer migration EF Core : table `ForumThreads`
  - `Id`, `UserId` (FK), `Title VARCHAR(255)`, `Content TEXT`, `Category VARCHAR(100)`, `Tag VARCHAR(100)`, `IsPinned BOOL DEFAULT false`, `IsSolved BOOL DEFAULT false`, `ViewsCount INT DEFAULT 0`, `RepliesCount INT DEFAULT 0`, `Upvotes INT DEFAULT 0`, `CreatedAt`, `UpdatedAt`, `IsDeleted BOOL DEFAULT false`
- [ ] 🔴 **BD** — Créer migration EF Core : table `ForumPosts`
  - `Id`, `ThreadId` (FK), `UserId` (FK), `Content TEXT`, `Upvotes INT DEFAULT 0`, `IsAccepted BOOL DEFAULT false`, `CreatedAt`, `UpdatedAt`, `IsDeleted BOOL DEFAULT false`
- [ ] 🔴 **BD** — Créer migration EF Core : table `ForumVotes`
  - `Id`, `PostId` (FK), `UserId` (FK), `Type VARCHAR(10)` (`up`|`down`), `CreatedAt` — UNIQUE(`PostId`, `UserId`)

### Backend
- [ ] 🔴 **B** — Créer les entités EF Core `ForumThread`, `ForumPost`, `ForumVote`
- [ ] 🔴 **B** — Implémenter `ForumService` avec la logique métier complète
- [ ] 🔴 **B** — Implémenter `ForumController` :
  - `GET /api/forums/threads` (avec `category`, `page`, `pageSize`)
  - `POST /api/forums/threads`
  - `GET /api/forums/threads/{id}/posts`
  - `POST /api/forums/threads/{id}/posts`
  - `POST /api/forums/posts/{id}/vote`
  - `POST /api/forums/posts/{id}/accept`
  - `DELETE /api/forums/threads/{id}`
- [ ] 🟠 **B** — Incrémenter `RepliesCount` automatiquement lors d'un nouveau post
- [ ] 🟠 **B** — Incrémenter `ViewsCount` lors d'un accès au thread
- [ ] 🟠 **B** — Notifier via ntfy quand quelqu'un répond au thread de l'utilisateur

### Frontend
- [ ] 🟠 **F** — `forumService.js` : aligner l'URL vote sur `POST /forums/posts/{id}/vote` (l'URL actuelle pointe sur une mauvaise route)

---

## 10. 🔧 Admin

### Backend
- [ ] 🟠 **B** — Vérifier/créer `PUT /api/admin/users/{id}/status` avec body `{ isActive: bool }` (actuellement endpoint `ban/unban` — à unifier)
- [ ] 🟠 **B** — Vérifier que `PUT /api/admin/users/{id}/role` existe avec body `{ role: string }`
- [ ] 🟠 **B** — Vérifier que `GET /api/admin/users` accepte le param `q` (recherche textuelle) avec debounce côté front ✅
- [ ] 🟡 **B** — `GET /api/admin/analytics` : s'assurer que la réponse inclut revenus totaux, nouveaux users (semaine), sujets actifs, taux de conversion

### Frontend
- [ ] 🟡 **F** — `Admin.jsx` : s'assurer que le modal de changement de rôle liste les 4 rôles : `student`, `parent`, `teacher`, `admin`

---

## 11. 🗄️ Base de données — Nettoyage & Migrations

### Migrations à créer (ordre suggéré)
1. [ ] 🔴 **BD** — `AddNotchpayFields` : colonnes `NotchpayReference`, `PhoneNumber`, `Operator` dans `Payments`, `currency DEFAULT 'XAF'`
2. [ ] 🔴 **BD** — `AddForumTables` : `ForumThreads`, `ForumPosts`, `ForumVotes`
3. [ ] 🟠 **BD** — `MakeCognitoIdNullable` : s'assurer que `CognitoId` est nullable (déjà le cas selon l'analyse — confirmer)
4. [ ] 🟠 **BD** — `AddUserProfileFields` : `Level VARCHAR(100)`, `City VARCHAR(100)` dans `Users` (si absents)
5. [ ] 🟡 **BD** — `RemoveLegacyPaymentColumns` : supprimer colonnes Stripe/PayPal non utilisées

### Vérifications BD
- [ ] 🟠 **BD** — Index manquant : `idx_forum_threads_category` sur `ForumThreads.Category`
- [ ] 🟠 **BD** — Index manquant : `idx_forum_posts_thread` sur `ForumPosts.ThreadId`
- [ ] 🟠 **BD** — Index manquant : `idx_payments_notchpay_ref` sur `Payments.NotchpayReference`
- [ ] 🟡 **BD** — Vérifier que `Payments.Currency` vaut bien `'XAF'` sur toutes les lignes existantes

---

## 12. 🏗️ Infrastructure & Configuration

### Variables d'environnement
- [ ] 🔴 **I** — Ajouter dans les secrets AWS : `NOTCHPAY_PUBLIC_KEY`, `NOTCHPAY_PRIVATE_KEY`, `NOTCHPAY_WEBHOOK_SECRET`
- [ ] 🔴 **I** — Ajouter : `RESEND_API_KEY`, `RESEND_FROM`
- [ ] 🔴 **I** — Ajouter : `NTFY_BASE_URL`, `NTFY_AUTH_TOKEN`, `NTFY_ADMIN_TOPIC`
- [ ] 🟠 **I** — Retirer des secrets : `SENDGRID_API_KEY`
- [ ] 🟠 **I** — Retirer des secrets : `MTN_*`, `ORANGE_*`, `WAVE_*`, `STRIPE_*`, `PAYPAL_*`

### ntfy (self-hosted)
- [ ] 🔴 **I** — Installer ntfy sur l'instance AWS (`apt install ntfy` ou Docker)
- [ ] 🔴 **I** — Configurer nginx reverse proxy vers ntfy (HTTPS)
- [ ] 🔴 **I** — Configurer l'authentification ntfy (fichier de config ou token)
- [ ] 🟠 **I** — Exposer ntfy sur un sous-domaine : `ntfy.winplus.cm`

### CORS
- [ ] 🟠 **B** — `appsettings.json` : ajouter le domaine de production dans `AllowedOrigins` : `https://winplus.cm`, `https://www.winplus.cm`
- [ ] 🟠 **P** — `app.py` Python : restreindre CORS (remplacer `["*"]` par les domaines autorisés en production)

### Sécurité
- [ ] 🟠 **B** — Vérifier que le webhook NotchPay valide bien la signature HMAC avant tout traitement
- [ ] 🟠 **B** — Ajouter idempotency key sur le webhook (éviter le double traitement si NotchPay retente)
- [ ] 🟡 **B** — Rate limiting sur `/api/payments/initiate` : max 5 tentatives / 10 min / user

---

## 13. 🔍 Module IA Python

- [ ] 🔴 **P** — Implémenter `POST /api/chatbot/stream` (voir section 4)
- [ ] 🔴 **P** — `deepseek_client.py` : activer `stream=True`
- [ ] 🟠 **P** — Persister le message final en BD à la fin du stream (via SQLAlchemy)
- [ ] 🟠 **P** — Restreindre CORS en production
- [ ] 🟡 **P** — Implémenter `get_personalized_recommendations` avec le vrai historique d'enrollments (actuellement retourne les sujets populaires)
- [ ] 🟡 **P** — `recommender.py` : brancher `get_similar_subjects()` sur les données réelles PostgreSQL (table `Subjects`)

---

## Récapitulatif par priorité

### 🔴 Critique (à faire en premier — bloquant en production)
1. NotchPay : supprimer anciens providers + créer `NotchPayService` + webhook + endpoints
2. Resend : remplacer SendGrid
3. Forum : créer les tables BD + entités + contrôleur
4. SSE chatbot : activer dans FastAPI
5. Routes profil : aligner sur `/users/profile` (sans ID)
6. BD : `Currency DEFAULT 'XAF'`, ajout colonnes NotchPay
7. `authService.ts` : corriger `/auth/refresh-token` → `/auth/refresh`

### 🟠 Important (à faire avant la beta)
8. ntfy : installer + configurer + brancher sur les événements clés
9. `GET /api/subjects/{id}/download` (URL S3 signée)
10. `GET /api/subjects/{id}/similar` (proxy vers Python)
11. Enrichir `/student/stats` avec priorities/goals/events
12. `GET /api/payments/history` et `/users/profile/statistics`
13. `PUT /api/admin/users/{id}/status` (activer/désactiver)
14. Pricing : `/pricing/plans`, `/pricing/promotions`, `/pricing/compare`
15. Abonnements : vérifier la table `Subscriptions` et le cron d'expiration

### 🟡 Secondaire (post-lancement)
16. Recommandations personnalisées (vrai historique enrollments)
17. Nettoyage `CognitoId` de la BD
18. Suppression colonnes Stripe/PayPal orphelines
19. Rate limiting paiements
20. Monitoring : alertes ntfy admin sur erreurs critiques
