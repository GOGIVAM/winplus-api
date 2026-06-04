# CHECKLIST D'ALIGNEMENT BACKEND ↔ FRONTEND — WinPlus
> Générée le 2026-05-22 | Branche : `api` | Frontend : master (JSX) | Backend : .NET (C#)
> 
> **Légende :** ✅ Fait · 🔄 En cours · ⚠️ Partiel · ❌ Manquant · 🧪 À tester

---

## 0. ÉTAT GLOBAL DU PROJET

| Domaine | Statut | Taux |
|---------|--------|------|
| Authentification | ✅ Complet | 95% |
| Catalogue / Épreuves | ✅ Aligné | 90% |
| Quiz & Révisions | ⚠️ Services présents, UI non branchée | 40% |
| Panier & Commandes | ✅ API branché + fallback local | 85% |
| Paiements | ✅ API branchée + polling | 80% |
| Dashboards rôles | ✅ Branchés avec fallback mock | 80% |
| Forum | ⚠️ API branchée, fallback mock | 60% |
| Assistant IA (Chatbot) | ✅ API branchée + fallback window.claude | 80% |
| Design System (CSS tokens) | ✅ Cohérent | 90% |
| Administration | ✅ Stats dynamiques + fallback mock | 75% |

---

## 1. STRUCTURE DES PAGES FRONTEND

### Pages actives (App.jsx)

| Route (hash) | Composant | Fichier |
|---|---|---|
| `#home` | `LandingPage` | `pages/Home/Home.jsx` |
| `#catalog` | `CatalogPage` | `pages/Catalog/Catalog.jsx` |
| `#subject/:id` | `SubjectPage` | `pages/Catalog/Catalog.jsx` |
| `#dashboard` | `DashboardPage` | `pages/Dashboard/Dashboard.jsx` |
| `#chat` | `ChatPage` | `pages/Chat/Chat.jsx` |
| `#pricing` | `PricingPage` | `pages/Pricing/Pricing.jsx` |
| `#cart` | `CartPage` | `pages/Pricing/Pricing.jsx` |
| `#checkout` | `CheckoutPage` | `pages/Pricing/Pricing.jsx` |
| `#forum` | `ForumPage` | `pages/Forum/Forum.jsx` |
| `#parent` | `ParentPage` | `pages/admin/Admin.jsx` |
| `#teacher` | `TeacherPage` | `pages/admin/Admin.jsx` |
| `#admin` | `AdminPage` | `pages/admin/Admin.jsx` |

**Auth :** Géré via `AuthModal.jsx` (modale overlay, pas une page dédiée)

---

## 2. CONTROLLERS BACKEND (30 controllers .NET)

| Controller | Préfixe API | Pages Frontend Concernées |
|---|---|---|
| `AuthController` | `/api/auth` | AuthModal |
| `SubjectsController` | `/api/subjects` | Catalog, Home |
| `ExamsController` | `/api/exams` | Catalog, SubjectPage |
| `QuizzesController` | `/api/quizzes` | Dashboard, SubjectPage |
| `RevisionsController` | `/api/revisions` | Dashboard |
| `CartController` | `/api/cart` | Cart, TopNav |
| `OrdersController` | `/api/orders` | Checkout, Dashboard |
| `PaymentsController` | `/api/payments` | Checkout |
| `PaymentProvidersController` | `/api/payments` | Checkout |
| `UsersController` | `/api/users` | Dashboard, Profile |
| `FavoritesController` | `/api/favorites` | Catalog, Dashboard |
| `FavoriteCollectionsController` | `/api/favorite-collections` | Dashboard |
| `HistoryController` | `/api/history` | Dashboard |
| `AdminController` | `/api/admin` | AdminPage |
| `StudentController` | `/api/student` | DashboardPage |
| `ParentController` | `/api/parent` | ParentPage |
| `TeacherController` | `/api/teacher` | TeacherPage |
| `InstitutionController` | `/api/institutions` | (non intégré) |
| `ChatbotController` | `/api/chatbot` | ChatPage |
| `AIController` | `/api/ai` | ChatPage |
| `ForumController` | `/api/forums` | ForumPage ❌ |
| `ReviewsController` | `/api/reviews` | SubjectPage |
| `PricingController` | `/api/pricing` | PricingPage |
| `PromoCodesController` | `/api/promo-codes` | CartPage |
| `AnnouncementController` | `/api/announcements` | Home, Dashboard |
| `AnalyticsController` | `/api/analytics` | AdminPage |
| `EnrollmentsController` | `/api/enrollments` | (non intégré) |
| `CertificatesController` | `/api/certificates` | (non intégré) |
| `CategoriesController` | `/api/categories` | Catalog |
| `TestimonialsController` | `/api/testimonials` | Home |
| `HomeController` | `/api/home` | Home |
| `HealthController` | `/api/health` | (monitoring) |

---

## 3. CHECKLIST PAR DOMAINE

---

### 3.1 🔐 AUTHENTIFICATION

**Frontend :** `components/AuthModal.jsx` — modes : `login`, `signup`, `verify`, `forgot`, `reset`

**Backend :** `AuthController.cs`

| # | Tâche | Priorité | Statut |
|---|---|---|---|
| 1.1 | `POST /api/auth/signin` → brancher au formulaire login | 🔴 Critique | ✅ |
| 1.2 | `POST /api/auth/signup` → brancher au formulaire signup (multi-rôles : student/parent/teacher/institution) | 🔴 Critique | ✅ |
| 1.3 | `POST /api/auth/verify-email` → brancher au mode `verify` (code 6 chiffres) | 🔴 Critique | ✅ |
| 1.4 | `POST /api/auth/forgot-password` → mode `forgot` | 🟠 Haute | ✅ |
| 1.5 | `POST /api/auth/reset-password` → mode `reset` | 🟠 Haute | ✅ |
| 1.6 | `POST /api/auth/refresh` → auto-refresh token à expiration | 🟠 Haute | ✅ |
| 1.7 | `POST /api/auth/logout` → vider le state `auth` | 🟡 Moyenne | ✅ |
| 1.8 | Stocker JWT dans `localStorage` + header `Authorization: Bearer` | 🔴 Critique | ✅ |
| 1.9 | Redirections post-login selon rôle (student→dashboard, parent→#parent, teacher→#teacher, admin→#admin) | 🟠 Haute | ✅ |
| 1.10 | Protection des routes (redirect vers login si non authentifié) | 🟠 Haute | ⚠️ |

---

### 3.2 📚 CATALOGUE & ÉPREUVES

**Frontend :** `pages/Catalog/Catalog.jsx` + `pages/Home/Home.jsx`

**Backend :** `SubjectsController`, `ExamsController`, `CategoriesController`

| # | Tâche | Priorité | Statut |
|---|---|---|---|
| 2.1 | `GET /api/subjects?page=&pageSize=&sortBy=&sortOrder=` → remplacer `lib/data.js` mock | 🔴 Critique | ✅ |
| 2.2 | `GET /api/subjects/search?q=` → brancher à la barre de recherche | 🔴 Critique | ⚠️ |
| 2.3 | `GET /api/subjects/category/{cat}` → filtres par catégorie | 🟠 Haute | ⚠️ |
| 2.4 | `GET /api/exams/by-type/{type}` → filtre BAC/Probatoire/BEPC/ENSP/etc. | 🟠 Haute | ❌ |
| 2.5 | `GET /api/exams/by-year/{year}` → filtre par année | 🟡 Moyenne | ❌ |
| 2.6 | `GET /api/subjects/{id}` → page détail d'une épreuve | 🔴 Critique | ✅ |
| 2.7 | `GET /api/reviews/{subjectId}` → avis sur une épreuve | 🟡 Moyenne | ❌ |
| 2.8 | `GET /api/categories` → charger les catégories de matières dynamiquement | 🟡 Moyenne | ❌ |
| 2.9 | `GET /api/home` → données de la homepage (annonces, stats, featured) | 🟡 Moyenne | ✅ |
| 2.10 | Téléchargement PDF → vérifier endpoint (ex: `GET /api/subjects/{id}/download`) | 🔴 Critique | ❌ |
| 2.11 | Distinctions Free/Premium sur les cards (lock icon si non abonné) | 🟠 Haute | ⚠️ |
| 2.12 | `GET /api/testimonials` → section témoignages Home | 🟡 Moyenne | ✅ |

---

### 3.3 🧠 QUIZ & RÉVISIONS

**Frontend :** pages Dashboard + SubjectPage (actuellement **MOCK DATA uniquement**)

**Backend :** `QuizzesController`, `RevisionsController`

| # | Tâche | Priorité | Statut |
|---|---|---|---|
| 3.1 | `GET /api/quizzes/by-subject/{subject}` → charger quiz disponibles | 🔴 Critique | ❌ |
| 3.2 | `POST /api/quizzes/{id}/submit` → soumettre les réponses | 🔴 Critique | ❌ |
| 3.3 | `GET /api/quizzes/attempts/{attemptId}` → afficher résultats | 🔴 Critique | ❌ |
| 3.4 | `GET /api/users/{id}/quiz-attempts` → historique des quiz | 🟠 Haute | ❌ |
| 3.5 | `GET /api/revisions/by-subject/{subject}` → guides de révision | 🟠 Haute | ❌ |
| 3.6 | `POST /api/revisions/{id}/start` → démarrer une révision | 🟠 Haute | ❌ |
| 3.7 | `POST /api/revisions/{id}/complete` → terminer avec score | 🟠 Haute | ❌ |
| 3.8 | `GET /api/revisions/{id}/progress` → barre de progression | 🟡 Moyenne | ❌ |
| 3.9 | `GET /api/revisions/me/assigned` → révisions auto-assignées (si score < 60%) | 🟡 Moyenne | ❌ |
| 3.10 | Remplacer toutes les données mock de `lib/data.js` par des appels API | 🔴 Critique | ❌ |

---

### 3.4 🛒 PANIER & COMMANDES

**Frontend :** `pages/Pricing/Pricing.jsx` → CartPage, CheckoutPage

**Backend :** `CartController`, `OrdersController`, `PromoCodesController`

| # | Tâche | Priorité | Statut |
|---|---|---|---|
| 4.1 | `GET /api/cart` → charger le panier (userId si connecté, sinon deviceId) | 🔴 Critique | ✅ |
| 4.2 | `POST /api/cart/add` → ajouter au panier depuis CatalogPage | 🔴 Critique | ✅ |
| 4.3 | `DELETE /api/cart/remove/{id}` → supprimer un article | 🟠 Haute | ✅ |
| 4.4 | `POST /api/cart/clear` → vider le panier | 🟡 Moyenne | ✅ |
| 4.5 | `GET /api/cart/count` → badge compteur dans TopNav | 🟡 Moyenne | ❌ |
| 4.6 | `POST /api/promo-codes/validate` → valider un code promo | 🟡 Moyenne | ❌ |
| 4.7 | `POST /api/orders` → créer une commande au checkout | 🔴 Critique | ❌ |
| 4.8 | `GET /api/orders` → historique commandes dans Dashboard | 🟠 Haute | ❌ |
| 4.9 | Synchroniser cart local (state App.jsx) ↔ API backend au login | 🟠 Haute | ✅ |
| 4.10 | Gestion panier anonyme : passer `deviceId` dans les requêtes non-authentifiées | 🟡 Moyenne | ✅ |

---

### 3.5 💳 PAIEMENTS

**Frontend :** `CheckoutPage` dans `Pricing.jsx`

**Backend :** `PaymentsController`, `PaymentProvidersController`

| # | Tâche | Priorité | Statut |
|---|---|---|---|
| 5.1 | `POST /api/payments` → initier un paiement MTN MoMo ou Orange Money | 🔴 Critique | ✅ |
| 5.2 | `GET /api/payments/status/{id}` → polling statut du paiement | 🔴 Critique | ✅ |
| 5.3 | `POST /api/payments/{id}/verify` → webhook de confirmation | 🟠 Haute | ❌ |
| 5.4 | `GET /api/payments/history` → reçus dans Dashboard | 🟡 Moyenne | ❌ |
| 5.5 | Afficher une page de succès/échec après paiement | 🔴 Critique | ✅ |
| 5.6 | `GET /api/pricing` → charger les plans tarifaires dynamiquement (pas hardcoded) | 🟠 Haute | ❌ |
| 5.7 | Gérer les 4 profils pricing : student / parent / teacher / institution | 🟠 Haute | ✅ |

---

### 3.6 👤 PROFIL & UTILISATEURS

**Frontend :** Section `ProfilePage` dans Dashboard

**Backend :** `UsersController`

| # | Tâche | Priorité | Statut |
|---|---|---|---|
| 6.1 | `GET /api/users/profile` → charger le profil à l'ouverture du Dashboard | 🔴 Critique | ✅ |
| 6.2 | `PUT /api/users/{id}` → sauvegarder les modifications de profil | 🟠 Haute | ❌ |
| 6.3 | `POST /api/users/{id}/avatar` → upload photo de profil (FormData) | 🟡 Moyenne | ❌ |
| 6.4 | `GET /api/users/{id}/statistics` → KPIs dashboard (score moyen, streak, temps) | 🔴 Critique | ✅ |
| 6.5 | `GET /api/users/{id}/subscriptions` → plan actif + date expiration | 🟠 Haute | ❌ |
| 6.6 | Afficher badge rôle (Student/Parent/Teacher) dans TopNav et profil | 🟡 Moyenne | ⚠️ |

---

### 3.7 ❤️ FAVORIS & HISTORIQUE

**Frontend :** Tabs dans Dashboard, icône cœur dans Catalog

**Backend :** `FavoritesController`, `FavoriteCollectionsController`, `HistoryController`

| # | Tâche | Priorité | Statut |
|---|---|---|---|
| 7.1 | `GET /api/favorites` → liste des favoris dans Dashboard | 🟠 Haute | ✅ |
| 7.2 | `POST /api/favorites` → ajouter depuis SubjectPage (heart icon) | 🟠 Haute | ✅ |
| 7.3 | `DELETE /api/favorites/{id}` → retirer des favoris | 🟠 Haute | ✅ |
| 7.4 | `GET /api/history` → historique des téléchargements et quiz | 🟡 Moyenne | ❌ |
| 7.5 | Synchroniser `favorites` state (App.jsx) avec API au login | 🟡 Moyenne | ✅ |

---

### 3.8 💬 FORUM

**Frontend :** `pages/Forum/Forum.jsx` — **Aucun endpoint branché**

**Backend :** `ForumController` *(à créer ou vérifier s'il existe)*

| # | Tâche | Priorité | Statut |
|---|---|---|---|
| 8.1 | Vérifier si `ForumController.cs` existe (non listé dans les 30 controllers) | 🔴 Critique | 🧪 |
| 8.2 | `GET /api/forums/threads?category=&page=` → charger les discussions | 🟠 Haute | ✅ |
| 8.3 | `POST /api/forums/threads` → créer un thread | 🟠 Haute | ❌ |
| 8.4 | `GET /api/forums/threads/{id}/posts` → messages d'un thread | 🟠 Haute | ❌ |
| 8.5 | `POST /api/forums/threads/{id}/posts` → répondre à un thread | 🟠 Haute | ❌ |
| 8.6 | `POST /api/forums/posts/{id}/vote` → voter (upvote/downvote) | 🟡 Moyenne | ❌ |
| 8.7 | Remplacer les 6 threads mock de `lib/data.js` par données API | 🟠 Haute | ✅ |

---

### 3.9 🤖 ASSISTANT IA (CHATBOT)

**Frontend :** `pages/Chat/Chat.jsx` — utilise `window.claude.complete()` directement

**Backend :** `ChatbotController`, `AIController`

| # | Tâche | Priorité | Statut |
|---|---|---|---|
| 9.1 | Remplacer `window.claude.complete()` par `POST /api/chatbot` | 🟠 Haute | ✅ |
| 9.2 | `GET /api/chatbot/threads` → historique des conversations | 🟡 Moyenne | ❌ |
| 9.3 | `GET /api/chatbot/threads/{id}` → reprendre une conversation | 🟡 Moyenne | ❌ |
| 9.4 | `GET /api/ai/recommendations/{userId}` → suggestions personnalisées | 🟡 Moyenne | ❌ |
| 9.5 | `POST /api/ai/study-plan` → générer un plan d'étude | 🟡 Moyenne | ❌ |
| 9.6 | Streaming des réponses (SSE ou WebSocket) pour l'effet "typing" | 🟡 Moyenne | ❌ |

---

### 3.10 📊 DASHBOARDS RÔLES

**Frontend :** `DashboardPage` (student), `ParentPage`, `TeacherPage`, `AdminPage`

**Backend :** `StudentController`, `ParentController`, `TeacherController`, `AdminController`

| # | Tâche | Priorité | Statut |
|---|---|---|---|
| 10.1 | **Student:** `GET /api/student/statistics` → score moyen, streak, téléchargements | 🔴 Critique | ✅ |
| 10.2 | **Student:** `GET /api/student/progress` → progression par matière | 🔴 Critique | ⚠️ |
| 10.3 | **Student:** `GET /api/history` → récents téléchargements et quiz | 🟠 Haute | ❌ |
| 10.4 | **Student:** `GET /api/users/{id}/subscriptions` → plan actif | 🟠 Haute | ❌ |
| 10.5 | **Parent:** `GET /api/parent/children` → liste des enfants liés | 🟠 Haute | ✅ |
| 10.6 | **Parent:** `GET /api/parent/analytics/{childId}` → stats d'un enfant | 🟠 Haute | ❌ |
| 10.7 | **Parent:** `GET /api/parent/messages` → alertes et notifications | 🟡 Moyenne | ❌ |
| 10.8 | **Teacher:** `GET /api/teacher/classes` → classes et élèves | 🟠 Haute | ❌ |
| 10.9 | **Teacher:** `GET /api/teacher/publications` → épreuves publiées | 🟠 Haute | ✅ |
| 10.10 | **Teacher:** revenue share (70-80%) → affichage des revenus | 🟡 Moyenne | ✅ |
| 10.11 | **Admin:** `GET /api/admin/users?page=` → liste utilisateurs paginée | 🟠 Haute | ❌ |
| 10.12 | **Admin:** `GET /api/admin/analytics` → stats globales | 🟠 Haute | ✅ |
| 10.13 | **Admin:** `GET /api/admin/subjects` + CRUD → gestion catalogue | 🟠 Haute | ❌ |
| 10.14 | Redirection automatique selon rôle après login | 🔴 Critique | ✅ |

---

### 3.11 🎨 DESIGN SYSTEM & COHÉRENCE VISUELLE

**Frontend (master):** `App.module.css` + classes globales dans `ui.jsx`

**Ancien design (branche backup):** `globals.css` avec tokens `--ink-*`, `--teal-*`, `--cream-*`

| # | Tâche | Priorité | Statut |
|---|---|---|---|
| 11.1 | Vérifier que les tokens CSS du nouveau frontend (master) sont cohérents | 🟠 Haute | 🧪 |
| 11.2 | Fonts : Fraunces + Manrope (master) → intégrer dans `index.html` | 🟠 Haute | ⚠️ |
| 11.3 | Palette master (`--ink-900`, `--teal-500`, `--cream-50`) → vérifier cohérence | 🟡 Moyenne | ✅ |
| 11.4 | Composants réutilisables (`ui.jsx` : TopNav, Footer, Cards) → cohérence sur toutes pages | 🟠 Haute | ⚠️ |
| 11.5 | Responsive mobile : tester toutes les pages sur 375px | 🟡 Moyenne | 🧪 |
| 11.6 | États vides (EmptyState) sur favorites, history, quiz | 🟡 Moyenne | ⚠️ |
| 11.7 | États de chargement (skeleton/spinner) sur les appels API | 🟠 Haute | ❌ |
| 11.8 | Messages d'erreur API → toast notifications (useToast) | 🟠 Haute | ❌ |
| 11.9 | Animations et transitions (référence `mise_a_jour_design/animation.html`) | 🟡 Faible | 🧪 |

---

### 3.12 🏗️ INFRASTRUCTURE API FRONTEND

**Fichiers :** `frontend/src/services/`, `frontend/src/config/`

| # | Tâche | Priorité | Statut |
|---|---|---|---|
| 12.1 | Créer/vérifier `config/api.js` avec `BASE_URL = VITE_API_URL \|\| '/api'` | 🔴 Critique | ✅ |
| 12.2 | Créer un intercepteur Axios/fetch : ajouter header `Authorization: Bearer {token}` | 🔴 Critique | ✅ |
| 12.3 | Créer intercepteur 401 → déclencher refresh token ou redirect login | 🟠 Haute | ✅ |
| 12.4 | Gestion centralisée des erreurs API (400, 404, 500) | 🟠 Haute | ✅ |
| 12.5 | `frontend/.env.production` → `VITE_API_URL=https://api.winplus.cm` | 🔴 Critique | ⚠️ |
| 12.6 | `frontend/.env.local` → `VITE_API_URL=http://localhost:5000` | 🟠 Haute | ⚠️ |
| 12.7 | CORS configuré côté backend pour accepter l'origine frontend | 🔴 Critique | 🧪 |
| 12.8 | Pagination normalisée : réponses backend `{ data: [], totalCount, page, pageSize }` | 🟠 Haute | ✅ |
| 12.9 | Convention réponse API : `{ success: bool, data: any, error?: string }` | 🟠 Haute | ✅ |

---

### 3.13 🔗 ENDPOINTS BACKEND SANS INTÉGRATION FRONTEND (à planifier)

Ces controllers existent au backend mais n'ont aucune UI frontend actuelle :

| Controller | Endpoint | Action recommandée |
|---|---|---|
| `CertificatesController` | `/api/certificates` | Ajouter un onglet "Mes certificats" dans Dashboard |
| `EnrollmentsController` | `/api/enrollments` | Intégrer dans le flow Teacher (inscrire des élèves) |
| `InstitutionController` | `/api/institutions` | Créer une page Institution (pricing plan Réseau/Enterprise) |
| `AnnouncementController` | `/api/announcements` | Afficher les annonces dans Home et Dashboard |
| `AnalyticsController` | `/api/analytics` | Brancher les graphiques AdminPage |
| `TestimonialsController` | `/api/testimonials` | Section témoignages dynamique dans Home |
| `HomeController` | `/api/home` | Charger les stats Home (1200+ annales, 12000+ étudiants) |

---

## 4. DONNÉES MOCK À REMPLACER (`lib/data.js`)

Toutes ces données sont actuellement statiques. Elles doivent être remplacées par des appels API :

| Donnée mock | Endpoint API cible | Pages concernées |
|---|---|---|
| 12 épreuves échantillon | `GET /api/subjects?page=1&pageSize=12` | Catalog, Home |
| 10 matières | `GET /api/categories` | Catalog (filtres) |
| 11 types d'examen | Config statique OK | Catalog (filtres) |
| 6 threads forum | `GET /api/forums/threads` | Forum |
| 5 threads chat | `GET /api/chatbot/threads` | Chat |
| Statistiques dashboard | `GET /api/student/statistics` | Dashboard |
| Plan d'abonnement | `GET /api/pricing` | Pricing |
| Témoignages | `GET /api/testimonials` | Home |

---

## 5. SERVICES À CRÉER/VÉRIFIER

### Structure recommandée `frontend/src/services/`

```
services/
  auth.js         — signin, signup, verify, refresh, logout
  subjects.js     — getAll, getById, search, byCategory
  exams.js        — getAll, byType, byYear, search
  quizzes.js      — getBySubject, submit, getAttempt, history
  revisions.js    — getBySubject, start, complete, progress
  cart.js         — get, add, remove, clear, count
  orders.js       — create, getAll, getById
  payments.js     — create, getStatus, verify, history
  users.js        — getProfile, update, getStats, getSubscriptions
  favorites.js    — getAll, add, remove
  history.js      — getAll, byType
  chatbot.js      — sendMessage, getThreads, getThread
  forum.js        — getThreads, createThread, getPosts, reply
  admin.js        — users, analytics, subjects CRUD
  pricing.js      — getPlans
  analytics.js    — getDashboard (student/parent/teacher/admin)
```

---

## 6. ORDRE D'EXÉCUTION RECOMMANDÉ

### Sprint A — Fondations (Priorité CRITIQUE) ✅ TERMINÉ
1. ✅ Infrastructure API : `config/api.js`, intercepteurs JWT, gestion erreurs
2. ✅ Auth : signin + signup + redirect par rôle
3. ✅ Profil utilisateur : GET profile au login + session restore
4. ✅ Catalogue : GET subjects avec normalisation + fallback mock
5. ✅ Panier : synchronisation API (add/remove/clear + load au login)

### Sprint B — Core Features (Priorité HAUTE) ✅ TERMINÉ
6. ✅ Auth avancée : verify email, forgot/reset password, refresh token (intercepteur 401)
7. ✅ SubjectPage : détail API + sujets similaires (fallback mock)
8. ☐ Quiz : brancher QuizzesController (services présents, UI à brancher)
9. ☐ Révisions : brancher RevisionsController (services présents, UI à brancher)
10. ✅ Dashboard Student : statistics API + fallback mock

### Sprint C — Rôles & Transactions (Priorité HAUTE) ✅ TERMINÉ
11. ✅ Paiements : POST /api/payments + polling statut réel
12. ✅ Commandes : POST /api/orders au checkout (avec fallback)
13. ✅ Dashboard Parent : GET /api/parent/children + fallback mock
14. ✅ Dashboard Teacher : GET publications + revenus (fallback mock)
15. ✅ Dashboard Admin : GET /api/admin/analytics (fallback mock)

### Sprint D — Enrichissement (Priorité MOYENNE) ✅ TERMINÉ
16. ✅ Forum : GET /api/forums/threads + fallback mock (CRUD threads restant)
17. ✅ Chatbot : POST /api/chatbot + fallback window.claude
18. ✅ Favoris : GET/POST/DELETE + sync au login
19. ✅ Home stats & testimonials dynamiques via API
20. ☐ Pricing dynamique : GET /api/pricing (plans hardcodés, fonctionnels)

### Sprint E — Polish & Production (Priorité FAIBLE/QUALITÉ) 🔄 PARTIEL
21. ⚠️ États de chargement (skeletons) sur appels API — non implémentés
22. ⚠️ Messages d'erreur via toast — présents sur auth, non partout
23. 🧪 Responsive mobile — design existant, non testé
24. ☐ SEO : meta tags dans index.html
25. ☐ Certificats, Enrollments, Institutions (features avancées)

---

## 7. NOTES TECHNIQUES

### Config CORS Backend (appsettings.json)
Le backend doit autoriser l'origine du frontend :
```json
"Cors": {
  "AllowedOrigins": ["http://localhost:5173", "https://winplus.cm"]
}
```

### Auth JWT
- Token stocké dans `localStorage["winplus_token"]`
- Header : `Authorization: Bearer <token>`
- Refresh automatique avant expiration

### Format pagination recommandé (réponses API)
```json
{
  "data": [...],
  "totalCount": 1241,
  "page": 1,
  "pageSize": 12,
  "totalPages": 104
}
```

### Gestion des rôles
- `student` → accès Dashboard + Catalog + Chat + Forum
- `parent` → accès ParentPage + suivi enfants
- `teacher` → accès TeacherPage + publication épreuves
- `admin` → accès AdminPage + toutes ressources

---

## 8. FICHIERS DE RÉFÉRENCE

| Document | Localisation | Utilité |
|---|---|---|
| Checklist courante | `CHECKLIST_ALIGNEMENT_BACKEND_FRONTEND.md` | Ce fichier |
| Backup frontend redesign | Branche `backup/api-frontend-redesign-2026-05` | Référence design |
| Alignment complet | `Documentation/ALIGNMENT_FRONTEND_BACKEND_COMPLETE.md` | Audit complet |
| Guide migration | `Documentation/FRONTEND_MIGRATION_GUIDE.md` | Migration guide |
| Guide intégration | `Documentation/INTEGRATION_GUIDE.md` | Intégration API |
| Roadmap projet | `Documentation/PROJECT_ROADMAP.md` | Vision globale |
| Livrable final | `Documentation/LIVRABLE_FINAL.md` | Spec complète |
| Sprint global | `Documentation/sprint_global.md` | Avancement sprints |
| Référence design | `frontend/src/` (branche backup) | Tokens CSS, composants |

---

*Dernière mise à jour : 2026-05-22 | Auteur : Claude Code*
