# 📍 CORRESPONDANCE ENDPOINTS - TABLEAU COMPLET

**Date**: 7 décembre 2025  
**Global Status**: 26/51 endpoints = 51% ✅

---

## 🟢 ENDPOINTS CORRESPONDANTS - DÉJÀ IMPLÉMENTÉS

### **AUTHENTICATION - 4/4 ✅ COMPLET**

| Frontend Attendu | Backend Actuel | Status | Controller | Méthode |
|---|---|---|---|---|
| `POST /api/auth/signin` | `POST /api/auth/signin` | ✅ | AuthController | SignIn() |
| `POST /api/auth/signup` | `POST /api/auth/signup` | ✅ | AuthController | SignUp() |
| `POST /api/auth/refresh` | `POST /api/auth/refresh` | ✅ | AuthController | RefreshToken() |
| `POST /api/auth/logout` | `POST /api/auth/logout` | ✅ | AuthController | SignOut() |

---

### **SUBJECTS/COURSES - 7/7 ✅ COMPLET**

| Frontend Attendu | Backend Actuel | Status | Controller | Méthode |
|---|---|---|---|---|
| `GET /api/subjects` | `GET /api/subjects` | ✅ | SubjectsController | GetAll() |
| `GET /api/subjects/:id` | `GET /api/subjects/{id}` | ✅ | SubjectsController | GetById(id) |
| `POST /api/subjects` | `POST /api/subjects` | ✅ | SubjectsController | Create() |
| `PUT /api/subjects/:id` | `PUT /api/subjects/{id}` | ✅ | SubjectsController | Update() |
| `DELETE /api/subjects/:id` | `DELETE /api/subjects/{id}` | ✅ | SubjectsController | Delete() |
| `GET /api/subjects/search?q=...` | `GET /api/subjects/search` | ✅ | SubjectsController | Search(q) |
| `GET /api/subjects/category/:name` | `GET /api/subjects/category/{name}` | ✅ | SubjectsController | GetByCategory() |

---

### **CART - 4/4 ✅ COMPLET**

| Frontend Attendu | Backend Actuel | Status | Controller | Méthode |
|---|---|---|---|---|
| `POST /api/cart/add` | `POST /api/cart/add` | ✅ | CartController | AddToCart() |
| `DELETE /api/cart/remove/:id` | `DELETE /api/cart/remove/{id}` | ✅ | CartController | RemoveFromCart() |
| `GET /api/cart` | `GET /api/cart` | ✅ | CartController | GetCart() |
| `POST /api/cart/clear` | `POST /api/cart/clear` | ✅ | CartController | ClearCart() |

---

### **ORDERS - 3/4 ⚠️ PARTIEL**

| Frontend Attendu | Backend Actuel | Status | Controller | Méthode |
|---|---|---|---|---|
| `POST /api/orders` | `POST /api/orders` | ✅ | OrdersController | CreateOrder() |
| `GET /api/orders` | `GET /api/orders` | ✅ | OrdersController | GetOrders() |
| `GET /api/orders/:id` | `GET /api/orders/{id}` | ✅ | OrdersController | GetOrderById() |
| `POST /api/payments` | ❌ MANQUANT | ❌ | **À créer** | - |

---

### **USER PROFILE - 3/4 ⚠️ PARTIEL**

| Frontend Attendu | Backend Actuel | Status | Controller | Méthode |
|---|---|---|---|---|
| `GET /api/users/profile` | `GET /api/users/profile` | ✅ | UsersController | GetProfile() |
| `PUT /api/users/profile` | `PUT /api/users/profile` | ✅ | UsersController | UpdateProfile() |
| `DELETE /api/users/:id` | `DELETE /api/users/{id}` | ✅ | UsersController | Delete() |
| `GET /api/users/:id/statistics` | ❌ MANQUANT | ❌ | **À créer** | - |

---

### **FAVORITES - 3/3 ✅ COMPLET**

| Frontend Attendu | Backend Actuel | Status | Controller | Méthode |
|---|---|---|---|---|
| `POST /api/favorites/:id` | `POST /api/favorites/{id}` | ✅ | FavoritesController | AddFavorite() |
| `DELETE /api/favorites/:id` | `DELETE /api/favorites/{id}` | ✅ | FavoritesController | RemoveFavorite() |
| `GET /api/favorites` | `GET /api/favorites` | ✅ | FavoritesController | GetFavorites() |

---

### **AI - 2/6 ⚠️ PARTIEL**

| Frontend Attendu | Backend Actuel | Status | Controller | Méthode |
|---|---|---|---|---|
| `POST /api/ai/recommendations` | `POST /api/ai/recommendations` | ✅ | AIController | GetRecommendations() |
| `POST /api/ai/analyze` | `POST /api/ai/analyze` | ✅ | AIController | AnalyzeContent() |
| `GET /api/ai/recommendations/:id` | ❌ MANQUANT | ❌ | **À créer** | - |
| `POST /api/ai/study-plan` | ❌ MANQUANT | ❌ | **À créer** | - |
| `POST /api/ai/predict-success` | ❌ MANQUANT | ❌ | **À créer** | - |
| `POST /api/ai/chat` | ❌ MANQUANT | ❌ | **À créer** | - |

---

## 🔴 ENDPOINTS MANQUANTS - À CRÉER

### **HISTORY - 0/4 ❌ À IMPLÉMENTER**

| Endpoint Frontend | Backend Attendu | Status | Controller | Notes |
|---|---|---|---|---|
| `POST /api/history` | `POST /api/history` | ❌ | **HistoryController** | Ajouter événement |
| `GET /api/history` | `GET /api/history` | ❌ | **HistoryController** | Lister historique |
| `GET /api/history/:type` | `GET /api/history/{type}` | ❌ | **HistoryController** | Filtrer par type |
| `DELETE /api/history` | `DELETE /api/history` | ❌ | **HistoryController** | Effacer historique |

---

### **PAYMENTS - 0/5 ❌ À IMPLÉMENTER**

| Endpoint Frontend | Backend Attendu | Status | Controller | Notes |
|---|---|---|---|---|
| `POST /api/payments` | `POST /api/payments` | ❌ | **PaymentsController** | Créer paiement |
| - | `POST /api/payments/confirm` | ❌ | **PaymentsController** | Confirmer paiement |
| - | `GET /api/payments/{id}` | ❌ | **PaymentsController** | Détails paiement |
| - | `POST /api/payments/{id}/retry` | ❌ | **PaymentsController** | Réessayer |
| - | `POST /api/payments/{id}/refund` | ❌ | **PaymentsController** | Rembourser |

---

### **ANALYTICS - 0/3 ❌ À IMPLÉMENTER**

| Endpoint Frontend | Backend Attendu | Status | Controller | Notes |
|---|---|---|---|---|
| - | `POST /api/analytics/track` | ❌ | **AnalyticsController** | Tracker événement |
| - | `GET /api/analytics/session` | ❌ | **AnalyticsController** | Analytics session |
| - | `GET /api/analytics/user/{userId}` | ❌ | **AnalyticsController** | Analytics utilisateur |

---

### **ADMIN - 0/6 ❌ À IMPLÉMENTER**

| Endpoint Frontend | Backend Attendu | Status | Controller | Notes |
|---|---|---|---|---|
| `GET /api/admin/users` | `GET /api/admin/users` | ❌ | **AdminController** | Tous les users |
| `GET /api/admin/subjects` | `GET /api/admin/subjects` | ❌ | **AdminController** | Tous les courses |
| `GET /api/admin/orders` | `GET /api/admin/orders` | ❌ | **AdminController** | Toutes les commandes |
| `POST /api/admin/analytics` | `POST /api/admin/analytics` | ❌ | **AdminController** | Générer analytics |
| - | `GET /api/admin/statistics` | ❌ | **AdminController** | Stats globales |
| - | `GET /api/admin/dashboard` | ❌ | **AdminController** | Dashboard admin |

---

## 📊 TABLEAU DE SYNTHÈSE PAR PAGE FRONTEND

### **HomePage.tsx** ✅ 70% Fonctionnel
```
✅ GET /api/subjects                    - Afficher cours populaires
✅ GET /api/subjects/category           - Afficher catégories
❌ GET /api/users/:id/statistics        - Afficher stats utilisateur
❌ GET /api/analytics/user/{userId}     - Afficher données analytiques
```

### **SubjectDetailsPage.tsx** ✅ 100% Fonctionnel
```
✅ GET /api/subjects/{id}               - Détails du cours
✅ POST /api/favorites/{id}             - Ajouter favori
✅ DELETE /api/favorites/{id}           - Retirer favori
```

### **CartPage.tsx** ✅ 100% Fonctionnel
```
✅ GET /api/cart                        - Afficher panier
✅ POST /api/cart/add                   - Ajouter au panier
✅ DELETE /api/cart/remove/{id}         - Retirer du panier
✅ POST /api/cart/clear                 - Vider panier
❌ POST /api/payments                   - Passer commande (step 2)
```

### **CheckoutPage** ❌ 0% Fonctionnel
```
❌ POST /api/payments                   - Créer paiement
❌ POST /api/payments/confirm           - Confirmer paiement
❌ POST /api/orders                     - Créer commande
```

### **DashboardPage.tsx** ⚠️ 40% Fonctionnel
```
❌ GET /api/users/:id/statistics        - Stats utilisateur
❌ GET /api/history                     - Historique apprentissage
❌ GET /api/analytics/user/{userId}     - Données analytiques
✅ GET /api/subjects                    - Recommandations (partiel)
```

### **Discover.tsx** ✅ 100% Fonctionnel
```
✅ GET /api/subjects                    - Tous les cours
✅ GET /api/subjects/category           - Filtrer par catégorie
✅ GET /api/subjects/search             - Rechercher
```

### **SearchPage.tsx** ✅ 100% Fonctionnel
```
✅ GET /api/subjects/search             - Rechercher cours
```

### **Profile.tsx** ⚠️ 75% Fonctionnel
```
✅ GET /api/users/profile               - Profil utilisateur
✅ PUT /api/users/profile               - Modifier profil
❌ GET /api/users/:id/statistics        - Stats utilisateur
✅ DELETE /api/users/{id}               - Supprimer compte
```

### **AdminDashboard.tsx** ❌ 0% Fonctionnel
```
❌ GET /api/admin/users                 - Liste utilisateurs
❌ GET /api/admin/subjects              - Liste cours
❌ GET /api/admin/orders                - Liste commandes
❌ GET /api/admin/statistics            - Stats globales
❌ POST /api/admin/analytics            - Analytics
```

### **Favorites.tsx** ✅ 100% Fonctionnel
```
✅ GET /api/favorites                   - Lister favoris
✅ POST /api/favorites/{id}             - Ajouter favori
✅ DELETE /api/favorites/{id}           - Retirer favori
```

### **History.tsx** ❌ 0% Fonctionnel
```
❌ POST /api/history                    - Ajouter historique
❌ GET /api/history                     - Lister historique
❌ GET /api/history/{type}              - Filtrer historique
❌ DELETE /api/history                  - Effacer historique
```

---

## 🎯 RÉSUMÉ FINAL

### **Score par Catégorie**:
```
Authentication:    4/4  (100%) ✅
Subjects:          7/7  (100%) ✅
Cart:              4/4  (100%) ✅
Favorites:         3/3  (100%) ✅
Orders:            3/4  (75%)  ⚠️
Users:             3/4  (75%)  ⚠️
AI:                2/6  (33%)  ⚠️
History:           0/4  (0%)   ❌
Analytics:         0/3  (0%)   ❌
Admin:             0/6  (0%)   ❌
Payments:          0/5  (0%)   ❌
_______________________________________________
TOTAL:            26/51 (51%)  ⚠️
```

### **Pages Frontend Impact**:
```
100% Fonctionnelles:    3/11  (HomePage*, SubjectDetails, Discover, Search, Favorites)
75% Fonctionnelles:     2/11  (Profile, Cart)
50% Fonctionnelles:     1/11  (Dashboard)
25% Fonctionnelles:     0/11
0% Fonctionnelles:      5/11  (Checkout, History, Admin, Analytics, AI Chat)
```

### **Priorité d'Implémentation**:
```
🔴 CRITIQUE (Bloque MVP):
   - Payments (5 endpoints)
   - History (4 endpoints)
   Total: 9 endpoints = 1-2 semaines

🟠 IMPORTANT (Pour features complètes):
   - Analytics (3 endpoints)
   - Admin (6 endpoints)
   - AI Advanced (4 endpoints)
   Total: 13 endpoints = 2-3 semaines

🟡 NICE TO HAVE (Après MVP):
   - Advanced features (cart promo, favorites lists, etc)
   Total: 20+ endpoints = 2+ semaines
```

---

**Status**: Prêt pour implémentation 🚀
