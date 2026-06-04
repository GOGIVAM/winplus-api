# 🚀 ENDPOINT IMPLEMENTATION ROADMAP

**Date**: 7 décembre 2025  
**Status**: Planning Phase  
**Global Score**: 26/51 endpoints (51% implémenté)

---

## 📋 RÉSUMÉ GLOBAL

### ✅ DÉJÀ IMPLÉMENTÉ (26 endpoints)

#### **Subjects Controller** - 7/7 ✅
```
✅ GET    /api/subjects                    - GetAll()
✅ GET    /api/subjects/{id}               - GetById(id)
✅ POST   /api/subjects                    - Create(subject)
✅ PUT    /api/subjects/{id}               - Update(id, subject)
✅ DELETE /api/subjects/{id}               - Delete(id)
✅ GET    /api/subjects/search             - Search(q)
✅ GET    /api/subjects/category/{name}    - GetByCategory(name)
```

#### **Cart Controller** - 4/4 ✅
```
✅ GET    /api/cart                        - GetCart()
✅ POST   /api/cart/add                    - AddToCart(item)
✅ DELETE /api/cart/remove/{id}            - RemoveFromCart(id)
✅ POST   /api/cart/clear                  - ClearCart()
```

#### **Orders Controller** - 3/4 ✅
```
✅ POST   /api/orders                      - CreateOrder(paymentMethod)
✅ GET    /api/orders                      - GetOrders()
✅ GET    /api/orders/{id}                 - GetOrderById(id)
❌ POST   /api/payments                    - MISSING
```

#### **Users Controller** - 3/4 ✅
```
✅ GET    /api/users/profile               - GetProfile()
✅ PUT    /api/users/profile               - UpdateProfile(user)
✅ DELETE /api/users/{id}                  - Delete(id)
❌ GET    /api/users/{id}/statistics       - MISSING
```

#### **Favorites Controller** - 3/3 ✅
```
✅ GET    /api/favorites                   - GetFavorites()
✅ POST   /api/favorites/{id}              - AddFavorite(id)
✅ DELETE /api/favorites/{id}              - RemoveFavorite(id)
```

#### **AI Controller** - 2/6 ✅
```
✅ POST   /api/ai/analyze                  - AnalyzeContent(request)
✅ GET    /api/ai/health                   - CheckHealth()
❌ POST   /api/ai/recommendations          - MISSING (voir endpoint GET)
❌ POST   /api/ai/study-plan               - MISSING
❌ POST   /api/ai/predict-success          - MISSING
❌ POST   /api/ai/chat                     - MISSING
```

#### **Auth Controller** - 4/4 ✅
```
✅ POST   /api/auth/signin                 - SignIn(credentials)
✅ POST   /api/auth/signup                 - SignUp(data)
✅ POST   /api/auth/refresh                - RefreshToken()
✅ POST   /api/auth/logout                 - SignOut()
```

---

## ❌ À IMPLÉMENTER (25 endpoints)

### **1️⃣ PHASE 1 - CRITICAL (À commencer IMMÉDIATEMENT)**

#### Payments Controller - 0/5 ❌
**Importance**: 🔴 CRITIQUE - Bloqueant pour les commandes

```csharp
[ApiController]
[Route("api/payments")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;

    [HttpPost]
    public async Task<IActionResult> CreatePayment([FromBody] CreatePaymentRequest request)
    {
        // POST /api/payments
        // Crée une transaction de paiement
        // Utilisée par: CartPage, CheckoutPage
        // Réponse: { id, status, amount, currency, transactionId }
    }

    [HttpPost("{id}/confirm")]
    public async Task<IActionResult> ConfirmPayment(int id)
    {
        // POST /api/payments/{id}/confirm
        // Confirme un paiement en attente
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetPayment(int id)
    {
        // GET /api/payments/{id}
        // Récupère les détails d'un paiement
    }

    [HttpPost("{id}/retry")]
    public async Task<IActionResult> RetryPayment(int id)
    {
        // POST /api/payments/{id}/retry
        // Réessaie un paiement échoué
    }

    [HttpPost("{id}/refund")]
    public async Task<IActionResult> RefundPayment(int id)
    {
        // POST /api/payments/{id}/refund
        // Rembourse un paiement
    }
}
```

**Dépendances requises**:
- [ ] Modèle: `Payment` entity
- [ ] Service: `IPaymentService` + `PaymentService`
- [ ] Repository: `IPaymentRepository` + `PaymentRepository`
- [ ] DTO: `CreatePaymentRequest`, `PaymentResponse`
- [ ] Intégration: Stripe/PayPal

---

#### History Controller - 0/4 ❌
**Importance**: 🔴 CRITIQUE - Nécessaire pour DashboardPage

```csharp
[ApiController]
[Route("api/history")]
public class HistoryController : ControllerBase
{
    private readonly IHistoryService _historyService;

    [HttpPost]
    public async Task<IActionResult> AddToHistory([FromBody] AddHistoryRequest request)
    {
        // POST /api/history
        // Ajoute un événement à l'historique
        // Utilisée par: Toutes les pages (tracking)
        // Body: { subjectId, eventType, duration, score, eventDetails }
    }

    [HttpGet]
    public async Task<IActionResult> GetHistory()
    {
        // GET /api/history
        // Récupère l'historique complet de l'utilisateur
        // Utilisée par: DashboardPage, HistoryPage
        // Query: ?limit=50&offset=0
    }

    [HttpGet("{type}")]
    public async Task<IActionResult> GetHistoryByType(string type)
    {
        // GET /api/history/{type}
        // Récupère l'historique filtré par type
        // Types: course_started, course_completed, test_taken, lesson_viewed
    }

    [HttpDelete]
    public async Task<IActionResult> ClearHistory()
    {
        // DELETE /api/history
        // Efface tout l'historique de l'utilisateur
    }
}
```

**Dépendances requises**:
- [ ] Modèle: `LearningHistory` entity
- [ ] Service: `IHistoryService` + `HistoryService`
- [ ] Repository: `IHistoryRepository` + `HistoryRepository`
- [ ] DTO: `AddHistoryRequest`, `HistoryResponse`

---

### **2️⃣ PHASE 2 - IMPORTANT (À commencer rapidement)**

#### Analytics Controller - 0/3 ❌
**Importance**: 🟠 IMPORTANT - Pour DashboardPage et AdminDashboard

```csharp
[ApiController]
[Route("api/analytics")]
public class AnalyticsController : ControllerBase
{
    private readonly IAnalyticsService _analyticsService;

    [HttpPost("track")]
    public async Task<IActionResult> TrackEvent([FromBody] TrackEventRequest request)
    {
        // POST /api/analytics/track
        // Enregistre un événement analytique
        // Utilisée par: Partout (page views, clicks, interactions)
        // Body: { eventType, pageName, eventData, sessionId }
    }

    [HttpGet("session")]
    public async Task<IActionResult> GetSessionAnalytics()
    {
        // GET /api/analytics/session
        // Récupère les analytics de la session courante
        // Réponse: { sessionId, startTime, pageViews, events, duration }
    }

    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetUserAnalytics(int userId)
    {
        // GET /api/analytics/user/{userId}
        // Récupère les analytics d'un utilisateur
        // Réponse: { totalPageViews, totalTime, mostVisitedPages, etc }
    }
}
```

**Dépendances requises**:
- [ ] Modèle: `AnalyticsEvent` entity
- [ ] Service: `IAnalyticsService` + `AnalyticsService`
- [ ] Repository: `IAnalyticsRepository` + `AnalyticsRepository`
- [ ] DTO: `TrackEventRequest`, `AnalyticsResponse`

---

#### Admin Controller - 0/6 ❌
**Importance**: 🟠 IMPORTANT - Pour AdminDashboard

```csharp
[ApiController]
[Route("api/admin")]
[Authorize(Roles = "ADMIN")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _adminService;

    [HttpGet("users")]
    public async Task<IActionResult> GetAllUsers([FromQuery] int page = 1, [FromQuery] int limit = 50)
    {
        // GET /api/admin/users
        // Liste tous les utilisateurs (Admin seulement)
        // Réponse: { users: [{ id, email, createdAt, isActive }], total }
    }

    [HttpGet("subjects")]
    public async Task<IActionResult> GetAllSubjects([FromQuery] int page = 1)
    {
        // GET /api/admin/subjects
        // Liste tous les cours (Admin seulement)
    }

    [HttpGet("orders")]
    public async Task<IActionResult> GetAllOrders([FromQuery] int page = 1)
    {
        // GET /api/admin/orders
        // Liste toutes les commandes (Admin seulement)
    }

    [HttpPost("analytics")]
    public async Task<IActionResult> GenerateAnalytics([FromBody] GenerateAnalyticsRequest request)
    {
        // POST /api/admin/analytics
        // Génère un rapport analytique
    }

    [HttpGet("statistics")]
    public async Task<IActionResult> GetStatistics()
    {
        // GET /api/admin/statistics
        // Récupère les stats globales
        // Réponse: { totalUsers, totalCourses, totalRevenue, etc }
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard()
    {
        // GET /api/admin/dashboard
        // Récupère tous les données du dashboard
    }
}
```

**Dépendances requises**:
- [ ] Service: `IAdminService` + `AdminService`
- [ ] DTO: `AdminUserResponse`, `AdminStatisticsResponse`
- [ ] Authorization: [Authorize(Roles = "ADMIN")]

---

### **3️⃣ PHASE 3 - AMÉLIORATIONS (Après MVP)**

#### AI Controller Enhancements - 4/6 ❌
**Importance**: 🟡 IMPORTANTE - Fonctionnalités IA avancées

```csharp
[ApiController]
[Route("api/ai")]
public class AIController : ControllerBase
{
    // Endpoints existants:
    // ✅ POST /api/ai/analyze
    // ✅ GET  /api/ai/health

    [HttpPost("study-plan")]
    public async Task<IActionResult> GenerateStudyPlan([FromBody] GenerateStudyPlanRequest request)
    {
        // POST /api/ai/study-plan
        // Génère un plan d'étude personnalisé avec IA
        // Utilisée par: DashboardPage
    }

    [HttpPost("predict-success")]
    public async Task<IActionResult> PredictSuccess([FromBody] PredictSuccessRequest request)
    {
        // POST /api/ai/predict-success
        // Prédit la probabilité de succès
    }

    [HttpGet("recommendations/{userId}")]
    public async Task<IActionResult> GetRecommendations(int userId)
    {
        // GET /api/ai/recommendations/{userId}
        // Récupère les recommandations personnalisées
    }

    [HttpPost("chat")]
    public async Task<IActionResult> ChatWithAI([FromBody] ChatRequest request)
    {
        // POST /api/ai/chat
        // Chat interactif avec l'IA
        // Utilisée par: ChatPage
    }
}
```

---

#### Advanced Features ⭐
**À implémenter APRÈS le MVP complet**

```
Subjects:
  GET    /api/subjects/{id}/similar       - Suggestions similaires
  GET    /api/subjects/popular            - Top cours populaires
  GET    /api/subjects/recent             - Courses récemment ajoutés

Cart:
  PUT    /api/cart/update/{id}            - Mettre à jour quantité
  POST   /api/cart/sync                   - Synchroniser multi-device
  POST   /api/cart/promo                  - Appliquer code promo
  DELETE /api/cart/promo                  - Retirer code promo

Orders:
  POST   /api/orders/{id}/cancel          - Annuler commande
  POST   /api/orders/{id}/refund          - Rembourser
  GET    /api/orders/{id}/status          - Statut détaillé
  GET    /api/orders/{id}/invoice         - Télécharger facture
  POST   /api/orders/summary              - Résumé panier->order
  GET    /api/orders/statistics           - Stats utilisateur
  GET    /api/orders/search               - Rechercher commandes

Favorites:
  GET    /api/favorites/{subjectId}       - Vérifier si favori
  POST   /api/favorites/sync              - Synchroniser favoris
  POST   /api/favorites/lists             - Créer liste personnalisée
  GET    /api/favorites/lists             - Lister mes listes
  POST   /api/favorites/lists/{id}/items  - Ajouter à liste
  DELETE /api/favorites/lists/{id}/items  - Retirer de liste
  DELETE /api/favorites/lists/{id}        - Supprimer liste
  PATCH  /api/favorites/lists/{id}        - Renommer liste
  GET    /api/favorites/stats             - Stats des favoris

Users:
  GET    /api/users/{id}/statistics       - Stats utilisateur
  GET    /api/users/{id}/payment-methods  - Mes méthodes de paiement
  POST   /api/users/{id}/payment-methods  - Ajouter méthode
  DELETE /api/users/{id}/payment-methods/{methodId} - Retirer méthode

AI Advanced:
  GET    /api/ai/study-habits             - Analyser habits d'étude
  POST   /api/ai/adaptive-quiz            - Quiz adaptatif avec IA
  GET    /api/ai/content-recommendations  - Contenu recommandé
  GET    /api/ai/performance-analysis     - Analyse performances
```

---

## 📊 PLAN D'ACTION DÉTAILLÉ

### **SPRINT 1 - SEMAINE 1 (Jours 1-5)**
**Objectif**: Implémentation des endpoints CRITIQUES (Payments + History)

**Tâches**:
- [ ] Créer les entités: `Payment`, `LearningHistory`
- [ ] Créer DbContext migrations
- [ ] Implémenter `IPaymentRepository` et `PaymentRepository`
- [ ] Implémenter `IHistoryRepository` et `HistoryRepository`
- [ ] Créer `IPaymentService` et `PaymentService`
- [ ] Créer `IHistoryService` et `HistoryService`
- [ ] Implémenter `PaymentsController` complet
- [ ] Implémenter `HistoryController` complet
- [ ] Tests unitaires basiques
- [ ] Documenter endpoints Swagger

**Livrable**: Payments et History 100% fonctionnels

---

### **SPRINT 2 - SEMAINE 2 (Jours 6-10)**
**Objectif**: Analytics + Admin Controllers

**Tâches**:
- [ ] Créer entité `AnalyticsEvent`
- [ ] Implémenter `IAnalyticsRepository` et `AnalyticsRepository`
- [ ] Créer `IAnalyticsService` et `AnalyticsService`
- [ ] Implémenter `AnalyticsController` complet
- [ ] Créer `IAdminService` et `AdminService`
- [ ] Implémenter `AdminController` complet
- [ ] Authorization middleware [Authorize(Roles = "ADMIN")]
- [ ] Tests unitaires
- [ ] Documenter endpoints Swagger

**Livrable**: Analytics et Admin 100% fonctionnels + Sécurité

---

### **SPRINT 3 - SEMAINE 3 (Jours 11-15)**
**Objectif**: AI Enhancements + Refinements

**Tâches**:
- [ ] Implémenter `/api/ai/study-plan`
- [ ] Implémenter `/api/ai/predict-success`
- [ ] Implémenter `/api/ai/recommendations/{userId}`
- [ ] Implémenter `/api/ai/chat`
- [ ] Intégration avec FastApi AI Service
- [ ] Tests intégration
- [ ] Documenter endpoints Swagger
- [ ] Tests de charge

**Livrable**: AI Controller 100% implémenté

---

### **SPRINT 4 - SEMAINE 4 (Jours 16-20)**
**Objectif**: Features avancées + Polish

**Tâches**:
- [ ] Implémenter Advanced Features (Cart promo, Favorites lists, etc)
- [ ] Tests complets
- [ ] Documentation API complète
- [ ] Performance optimization
- [ ] Security audit
- [ ] Bug fixes

**Livrable**: MVP 100% complet et testé

---

## ✨ RÉSUMÉ DES MODIFICATIONS

### Avant
```
26/51 endpoints implémentés = 51%
❌ Payments (critique)
❌ History (critique)
❌ Analytics
❌ Admin
❌ AI Advanced
```

### Après (4 semaines)
```
51/51 endpoints implémentés = 100%
✅ Toutes les fonctionnalités MVP
✅ Dashboard utilisateur complet
✅ Admin panel complet
✅ Analytics tracking
✅ AI assistants
```

---

## 🎯 KPIs DE SUCCÈS

- [ ] Tous les endpoints testés et documentés
- [ ] Coverage tests > 80%
- [ ] Response time < 500ms (95e percentile)
- [ ] Uptime > 99.9%
- [ ] Zero critical security issues
- [ ] Frontend intégré avec succès

---

**Status**: Ready to implement 🚀
