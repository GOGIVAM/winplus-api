# État du Dashboard Admin — WinPlus
> Généré le 2026-06-16 · Branch frontend: `master` · Branch backend: `main`

---

## 1. Architecture générale

```
AdminDashboard.tsx  (layout + routing)
  ├── AdminPeriodProvider     (contexte période : 7d/30d/90d)
  ├── AdminTopBar             (breadcrumb, statut IA, search, notifications)
  ├── Sidebar                 (NAV_GROUPS — 26 entrées, 6 sections)
  ├── <AdminXxx />            (composant actif selon activeView)
  └── AdminChatbot            (floating chatbot — toutes les vues)
```

### Hooks partagés (module-level cache, anti-doublon)
| Hook | Endpoint | Cache |
|------|----------|-------|
| `useAdminAnalytics(period?)` | `GET /admin/analytics?period=` | Map + inflight dedup |
| `useAdminRecentActivities(limit?)` | `GET /admin/activities/recent?limit=` | Map + inflight dedup |

### Chargement initial (`load()`)
Au montage et sur refresh manuel, `AdminDashboard` lance en parallèle :
- `reloadAnalytics()` + `reloadActivities()` (via les hooks)
- `GET /admin/system/health`
- `GET /admin/subjects/pending?limit=1&countOnly=true`
- `GET /admin/winai/stats` → si échec + health OK → chip rouge "Service IA indisponible"

---

## 2. Sidebar — Navigation

| Section | Vue (`AdminView`) | Composant | Icône | Badge |
|---------|-------------------|-----------|-------|-------|
| **Pilotage** | `overview` | AdminOverview | LayoutGrid | — |
| | `map` | AdminMap | MapPin | — |
| | `analytics` | AdminAnalytics | BarChart2 | — |
| | `journey` | AdminJourney | GitBranch | — |
| **Comptes** | `users` | AdminUsers | Users | — |
| | `students` | AdminStudents | GraduationCap | — |
| | `teachers` | AdminTeachers | BookOpen | — |
| | `parents` | AdminParents | Heart | — |
| | `institutions` | AdminInstitutions | Building2 | — |
| **Contenu** | `contents` | AdminContents | FileCheck | `pendingContents` |
| | `catalogue` | AdminCatalogue | Layers | — |
| | `upload` | AdminUpload | UploadCloud | — |
| | `certificates` | AdminCertificates | Award | — |
| **Commerce** | `orders` | AdminOrders | ShoppingBag | `pendingOrders` |
| | `revenues` | AdminRevenues | TrendingUp | — |
| | `promo` | AdminPromoCodes | Tag | — |
| | `subscriptions` | AdminSubscriptions | Repeat | — |
| **Communication** | `announcements` | AdminAnnouncements | Megaphone | — |
| | `emails` | AdminEmails | Mail | — |
| | `winai` | AdminWinAI | Sparkles | dot pulsant |
| **Système** | `health` | AdminSystemHealth | Activity | — |
| | `logs` | AdminLogs | Terminal | — |
| | `audit` | AdminAudit | ClipboardList | — |
| | `settings` | AdminSettings | Settings | — |

> **Note** : La vue `chat` existe dans le type `AdminView` mais n'est plus dans la sidebar. Elle est rendue comme onglet "Modération chat" à l'intérieur de `AdminWinAI`.

---

## 3. Inventaire des composants

### 3.1 AdminOverview
**Données** : reçues en props depuis `AdminDashboard` (analytics + activities + health).  
**Appel propre** : `GET /admin/analytics/active-users` (polling 30 s → compteur "en ligne").  
**Rendu** :
- KPI row : utilisateurs, cours, commandes, revenus
- Graphe activité live (compteur online)
- Tableau activités récentes (10 entrées depuis `useAdminRecentActivities`)
- Carte santé système résumée
- Quick-links vers autres vues

**Statut** : ✅ Opérationnel

---

### 3.2 AdminMap
**Appel** : `GET /admin/analytics/geographic`  
**Fallback** : si l'endpoint échoue → liste de villes camerounaises avec données mock  
**Rendu** : carte Leaflet interactive, markers cercles proportionnels, classement villes  
**Statut** : ✅ Opérationnel (fallback géographique intégré)

---

### 3.3 AdminAnalytics
**Appels** :
- `GET /admin/analytics/revenues?period={N}months`
- `GET /admin/analytics/active-users`
- `GET /admin/analytics/popular-subjects?limit=5`
- `GET /admin/analytics/conversion-rate`

**Rendu** : graphe revenus mensuel, utilisateurs actifs, cours populaires, taux conversion  
**Statut** : ✅ Opérationnel (fallback gracieux sur chaque endpoint)

---

### 3.4 AdminJourney
**Appel** : `GET /admin/analytics`  
**Rendu** : KPI row (totalUsers, subscribedUsers, revenue, conversionRate, newUsersThisWeek, activeSubjects) + funnel visualisation pleine largeur  
**Note** : La carte géographique Cameroun a été **supprimée** dans cette session — seul le funnel est affiché.  
**Statut** : ✅ Opérationnel

---

### 3.5 AdminUsers / AdminStudents / AdminTeachers / AdminParents / AdminInstitutions
**Appels** : `GET /admin/users`, `GET /admin/students`, etc.  
**Rendu** : tableaux paginés avec search, filtres, actions (suspend/restore/delete)  
**Statut** : ✅ Opérationnel

---

### 3.6 AdminContents
**Appel** : `GET /admin/subjects/pending`  
**Rendu** : liste sujets en attente de validation, boutons Approuver / Rejeter  
**Badge** : `pendingContents` dans la sidebar  
**Statut** : ✅ Opérationnel

---

### 3.7 AdminCatalogue / AdminUpload
**Rendu** : catalogue cours publiés / formulaire upload  
**Statut** : ✅ Opérationnel

---

### 3.8 AdminCertificates
**Appel** : `GET /admin/certificates?limit=200`  
**Rendu** : tableau certificats (étudiant, cours, score, date, lien PDF), pagination 20/page, stats (total, ce mois, score moyen)  
**404 handling** : affiche "en cours de déploiement" si endpoint absent  
**Statut** : ✅ Frontend prêt · ✅ Backend livré (`ad76668`)

---

### 3.9 AdminOrders
**Appel** : `GET /admin/payments?limit=50`  
**Rendu** : tableau commandes/paiements (numéro, client, montant, statut, date, méthode), pagination 15/page, filtres statut + search  
**Statut** : ✅ Opérationnel (endpoint déjà paginé côté PaymentsController)

---

### 3.10 AdminRevenues
**Appel** : `GET /admin/analytics?period={period}`  
**Rendu** : KPIs revenus, graphe mensuel barre, top produits  
**404 handling** : "en cours de déploiement"  
**Statut** : ✅ Opérationnel

---

### 3.11 AdminPromoCodes
**Appels** : `GET /admin/promo-codes`, `POST`, `DELETE`  
**Statut** : ✅ Opérationnel

---

### 3.12 AdminSubscriptions
**Appels** :
- `GET /admin/subscriptions/plans`
- `GET /admin/subscriptions/stats`

**Rendu** : cards plans (nom, prix, période, features, abonnés actifs), stats globales (total/actif/résilié/churn)  
**404 handling** : "en cours de déploiement" si 404  
**Statut** : ✅ Frontend prêt · ✅ Backend livré (`ad76668`)

---

### 3.13 AdminAnnouncements / AdminEmails
**Appels** : CRUD annonces, `POST /admin/emails/send`  
**Statut** : ✅ Opérationnel

---

### 3.14 AdminWinAI
**Onglets** : `overview` · `conversations` · `topics` · `modération chat` · `config`

**Appel principal** : `GET /admin/winai/stats`  
**Données affichées** :
- Total conversations, utilisateurs actifs, avg messages/session, satisfaction rate
- Top topics, conversations récentes
- Métriques plateforme croisées (activeUsers, publishedContent, conversionRate depuis props)

**Onglet "Modération chat"** : rend `<AdminChat />` directement  
**404 handling** : Set module-level `notImplemented404` — évite les doubles appels après remount  
**Statut** : ✅ Frontend prêt · ✅ Backend livré (`ad76668`)

---

### 3.15 AdminChat (intégré dans AdminWinAI)
**Appels** :
- `GET /admin/chat/sessions?limit=100`
- `GET /admin/chat/stats`
- `GET /admin/chat/sessions/{id}/messages`
- `DELETE /admin/chat/messages/{id}`
- `PATCH /admin/chat/sessions/{id}/close`
- `POST /admin/chat/sessions/{id}/messages` ← admin reply (non encore implémenté côté backend)

**Rendu** : liste sessions (user, titre, nb messages, statut), vue messages avec suppression, fermeture session  
**404 handling** : Set module-level `chatEndpoints404`  
**Statut** : ✅ Frontend prêt · ✅ Backend livré partiellement — `POST /admin/chat/sessions/{id}/messages` manquant

---

### 3.16 AdminSystemHealth
**Appel** : `GET /admin/system/health` (auto-refresh 60 s)  
**Rendu** : statut global, jauges (charge, mémoire, temps réponse, uptime), tableau services  
**Statut** : ✅ Opérationnel

---

### 3.17 AdminLogs
**Appels** :
- `GET /admin/logs?limit=200`
- `PATCH /admin/logs/{id}/resolve`

**Rendu** : tableau logs (horodatage, sévérité, message, source, résolu), filtre par level, pagination 20/page  
**404 handling** : `api.isEndpointNotImplemented()`  
**Statut** : ✅ Frontend prêt · ✅ Backend livré (`ad76668`) · ⚠️ Requiert migration SQL + redémarrage

---

### 3.18 AdminAudit
**Appel** : `GET /admin/activities/recent?limit=200`  
**Note** : utilise le même endpoint que `useAdminRecentActivities` mais le lit directement (sans le hook partagé)  
**Rendu** : journal d'audit coloré par type (user/order/content/system/login/payment)  
**Statut** : ✅ Opérationnel

---

### 3.19 AdminSettings
**Rendu** : formulaire paramètres plateforme (général, sécurité, email, IA, paiement)  
**Statut** : ✅ Opérationnel (local state, pas d'API critique)

---

### 3.20 AdminTopBar
**Aucun appel** — affiche les props passées par `AdminDashboard` :
- Breadcrumb de la vue active
- Chip "Service IA indisponible" (rouge pulsant) si `iaDown === true`
- Compteur commandes en attente
- Search (navigue vers la vue correspondante)
- Menu admin (profil, navigation)

---

### 3.21 AdminChatbot (floating)
**Rendu** : bulle chatbot admin flottante accessible depuis toutes les vues  
**Connecté à** : `stats`, `activeView`, `onNavigate`  
**Statut** : ✅ Opérationnel

---

## 4. Tableau des endpoints — Statut complet

| Endpoint | Méthode | Composant(s) | Backend | Notes |
|----------|---------|--------------|---------|-------|
| `/admin/analytics` | GET | AdminJourney, AdminRevenues, `useAdminAnalytics` | ✅ | Données DB pures |
| `/admin/analytics/active-users` | GET | AdminOverview, AdminAnalytics | ✅ | Polling 30s |
| `/admin/analytics/revenues` | GET | AdminAnalytics | ✅ | |
| `/admin/analytics/popular-subjects` | GET | AdminAnalytics | ✅ | |
| `/admin/analytics/conversion-rate` | GET | AdminAnalytics | ✅ | |
| `/admin/analytics/geographic` | GET | AdminMap | ✅ | Fallback mock si 404 |
| `/admin/activities/recent` | GET | AdminAudit, `useAdminRecentActivities` | ✅ | |
| `/admin/system/health` | GET | AdminSystemHealth, Dashboard | ✅ | Auto-refresh 60s |
| `/admin/subjects/pending` | GET | AdminContents, Dashboard | ✅ | |
| `/admin/subjects/{id}/approve` | POST | AdminContents | ✅ | |
| `/admin/subjects/{id}/reject` | POST | AdminContents | ✅ | |
| `/admin/users` | GET | AdminUsers | ✅ | |
| `/admin/users/{id}/role` | PUT | AdminUsers | ✅ | |
| `/admin/users/{id}/suspend` | POST | AdminUsers | ✅ | |
| `/admin/users/{id}/restore` | POST | AdminUsers | ✅ | |
| `/admin/users/{id}/delete` | POST | AdminUsers | ✅ | |
| `/admin/payments` | GET | AdminOrders | ✅ | Paginé (page + limit + status) |
| `/admin/emails/send` | POST | AdminEmails | ✅ | |
| `/admin/winai/stats` | GET | AdminWinAI, Dashboard | ✅ **NOUVEAU** | Livré `ad76668` |
| `/admin/chat/sessions` | GET | AdminChat | ✅ **NOUVEAU** | Livré `ad76668` |
| `/admin/chat/stats` | GET | AdminChat | ✅ **NOUVEAU** | Livré `ad76668` |
| `/admin/chat/sessions/{id}/messages` | GET | AdminChat | ✅ **NOUVEAU** | Livré `ad76668` |
| `/admin/chat/sessions/{id}/close` | PATCH | AdminChat | ✅ **NOUVEAU** | Livré `ad76668` |
| `/admin/chat/messages/{id}` | DELETE | AdminChat | ✅ **NOUVEAU** | Livré `ad76668` |
| `/admin/chat/sessions/{id}/messages` | POST | AdminChat | ❌ **MANQUANT** | Reply admin — non implémenté |
| `/admin/subscriptions/plans` | GET | AdminSubscriptions | ✅ **NOUVEAU** | Livré `ad76668` |
| `/admin/subscriptions/stats` | GET | AdminSubscriptions | ✅ **NOUVEAU** | Livré `ad76668` |
| `/admin/certificates` | GET | AdminCertificates | ✅ **NOUVEAU** | Livré `ad76668` |
| `/admin/logs` | GET | AdminLogs | ✅ **NOUVEAU** | Livré `ad76668` · migration SQL requise |
| `/admin/logs/{id}/resolve` | PATCH | AdminLogs | ✅ **NOUVEAU** | Livré `ad76668` |

---

## 5. Actions à faire côté serveur

### 5.1 — SQL à exécuter (migration ApplicationLogs)
```sql
CREATE TABLE IF NOT EXISTS "ApplicationLogs" (
    "Id"          SERIAL PRIMARY KEY,
    "Level"       VARCHAR(20)  NOT NULL DEFAULT 'Error',
    "Category"    VARCHAR(200) NOT NULL DEFAULT '',
    "Message"     TEXT         NOT NULL,
    "Exception"   TEXT,
    "StackTrace"  TEXT,
    "RequestPath" VARCHAR(500),
    "UserId"      INTEGER,
    "CreatedAt"   TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    "IsResolved"  BOOLEAN      NOT NULL DEFAULT FALSE,
    "ResolvedAt"  TIMESTAMPTZ
);
CREATE INDEX IF NOT EXISTS "IX_ApplicationLogs_CreatedAt"  ON "ApplicationLogs" ("CreatedAt");
CREATE INDEX IF NOT EXISTS "IX_ApplicationLogs_IsResolved" ON "ApplicationLogs" ("IsResolved");
CREATE INDEX IF NOT EXISTS "IX_ApplicationLogs_Level"      ON "ApplicationLogs" ("Level");
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260615_AddApplicationLogs', '8.0.0')
ON CONFLICT DO NOTHING;
```

### 5.2 — appsettings.Production.json (section AIService)
```json
"AIService": {
  "BaseUrl": "http://172.31.1.71:8000",
  "TimeoutSeconds": 5,
  "RetryAttempts": 2,
  "EnableCircuitBreaker": true,
  "CircuitBreakerFailureThreshold": 3,
  "CircuitBreakerBreakDuration": "00:01:00"
}
```

### 5.3 — Redémarrer les services
```bash
sudo systemctl restart winplus-api
sudo systemctl restart winplus-fastapi
```

---

## 6. Ce qui reste à faire (backlog)

| Priorité | Item | Type |
|----------|------|------|
| 🔴 Critique | `POST /admin/chat/sessions/{id}/messages` — reply admin dans chat | Backend |
| 🔴 Critique | Investiguer 500 sur `/admin/analytics` et `/admin/activities/recent` en prod (colonnes GuestEmail/GuestName de la migration `20260614` appliquée ?) | Diagnostic |
| 🟠 Haute | Écrire dans `ApplicationLogs` depuis le middleware d'erreur .NET | Backend |
| 🟠 Haute | FastAPI : `/admin/winai/stats` probe — s'assurer que c'est le même port 8000 que `AIService:BaseUrl` | Config |
| 🟡 Moyenne | AdminAudit : migrer vers `useAdminRecentActivities` pour bénéficier du cache partagé | Frontend |
| 🟡 Moyenne | `AdminChat` : onglet "Signalements" (messages reportés) — endpoint non défini | Backend + Frontend |
| 🟡 Moyenne | Pagination serveur pour `/admin/certificates` (actuellement `limit=200`) | Backend |
| 🟢 Basse | `AdminMap` : connecter à `/admin/analytics/geographic` (actuellement différent de `/admin/analytics/geo`) — aligner les URLs | Frontend ou Backend |
| 🟢 Basse | `AdminSettings` : persister les paramètres via API | Backend |
| 🟢 Basse | Tests E2E des nouveaux endpoints chat/winai/subscriptions | QA |

---

## 7. Branding — État des remplacements

| Fichier | Occurrences "DeepSeek/Claude/GPT/OpenAI" | Statut |
|---------|------------------------------------------|--------|
| `src/pages/Home/Home.jsx` | 4 → remplacées par "WinAI" | ✅ |
| `src/pages/LandingPageLegacy.jsx` | 4 → remplacées | ✅ |
| `src/pages/AdminPage.jsx` | 2 → remplacées | ✅ |
| `src/components/admin.jsx` | 2 → remplacées | ✅ |
| `src/components/landing.jsx` | 4 → remplacées | ✅ |
| `src/pages/LandingPage.tsx` | 4 → remplacées | ✅ |
| `src/components/Chatbot/ChatWindow.tsx` | badge modèle supprimé | ✅ |
| `src/components/dashboard/admin/AdminWinAI.tsx` | noms modèles config remplacés | ✅ |
| `python/schemas.py` | `model: str` supprimé de `ChatResponse` | ✅ |
| `python/schemas.py` | `models_used` supprimé de `RecommendationsListResponse` | ✅ |
| `python/schemas.py` | `deepseek` → `ai_service` dans `ChatbotHealthResponse` | ✅ |
| `python/routes/chatbot_routes.py` | champ `model` retiré des réponses | ✅ |
| `python/app.py` | `models_used` retiré des réponses | ✅ |
| `python/services/deepseek_client.py` | Nom interne — **intentionnellement conservé** | ℹ️ |
| `src/hooks/useChatbot.ts` | Types internes — **intentionnellement conservés** | ℹ️ |

---

*Dernière mise à jour : 2026-06-16 · Commits : frontend `5efe153` · backend `ad76668`*
