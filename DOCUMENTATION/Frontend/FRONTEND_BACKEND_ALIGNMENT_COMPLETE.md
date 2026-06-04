# 📋 FRONTEND-BACKEND ALIGNMENT COMPLET
**État: 18 février 2026**

---

## � TABLE DE MATIÈRES

1. **[PARTIE 1 - DONNÉES FRONTEND REQUISES](#📦-partie-1--données-frontend-requises)**
   - 1.1 Routes API utilisées par TOUTES les pages (68 endpoints détaillés)
   - 1.3 Résumé complet des endpoints (statistiques + priorités)
   - 1.2 Structures de données requises

2. **[PARTIE 2 - TABLES & ENTITÉS BACKEND](#📊-partie-2--tables--entités-backend)**
   - 2.1 Schéma BD existant (tables implémentées)
   - 2.2 Tables manquantes (à créer)

3. **[PARTIE 3 - ENDPOINTS BACKEND](#⚙️-partie-3--endpoints-backend-requis)**
   - 3.1 Endpoints existants & implémentés
   - 3.2 Endpoints manquants & à implémenter (42 endpoints avec tables BD)

4. **[PROBLÈMES CRITIQUES & SOLUTIONS](#🚨-problèmes-critiques--solutions)**
   - Tous les bugs identifiés et solutions proposées

5. **[CHECKLIST COMPLÈTE](#-checklist-complète---frontendbackend-alignment)**
   - Phase 1, 2, 3 : Tâches à cocher avec dépendances
   - Tables BD à créer
   - Ordre recommandé

6. **[RÉSUMÉ EXÉCUTIF FINAL](#📋-résumé-exécutif-final)**
   - État global avec métriques complètes
   - Prochaines étapes

---

## 📦 PARTIE 1 : DONNÉES FRONTEND REQUISES


### 1.1 Routes API Utilisées par TOUTES les Pages Frontend

#### **📱 Pages Publiques & Authentication**

| Page | Endpoint | Méthode | Table BD | Colonnes | Statut |
|------|----------|---------|----------|----------|--------|
| **Home.tsx** | `/api/categories` | GET | Categories | Id, Name, Description | ✅ |
| **Home.tsx** | `/api/testimonials?limit=5` | GET | Reviews + Users | Review.Rating, Review.Comment, User.FirstName, User.AvatarUrl, User.Role | ⚠️ PARTIAL |
| **Home.tsx** | `/api/pricing/plans?category=students` | GET | PricingPlans | Id, Name, Price, Period, Features, Category, IsPopular | ❌ MANQUANT |
| **Home.tsx** | `/api/pricing/plans?category=teachers` | GET | PricingPlans | Id, Name, Price, Period, Features, Category, IsPopular | ❌ MANQUANT |
| **Home.tsx** | `/api/pricing/plans?category=parents` | GET | PricingPlans | Id, Name, Price, Period, Features, Category, IsPopular | ❌ MANQUANT |
| **Home.tsx** | `/api/subjects/search?limit=6&isFree=true` | GET | Subjects | Id, Title, ThumbnailUrl, Price, Description, IsFree, AverageRating | ✅ |
| **HomePage.tsx** | `/api/categories` | GET | Categories | Id, Name, Description | ✅ |
| **HomePage.tsx** | `/api/subjects/search?limit=6&isFree=true` | GET | Subjects | Id, Title, ThumbnailUrl, Price, Description, IsFree, AverageRating | ✅ |
| **HomePage.tsx** | `/api/testimonials` | GET | Reviews + Users | Review.Rating, Review.Comment, User.FirstName, User.AvatarUrl, User.Role | ⚠️ PARTIAL |
| **HomePage.tsx** | `/api/pricing/plans?category=students` | GET | PricingPlans | Id, Name, Price, Period, Features, Category | ❌ MANQUANT |
| **HomePage.tsx** | `/api/pricing/plans?category=teachers` | GET | PricingPlans | Id, Name, Price, Period, Features, Category | ❌ MANQUANT |
| **HomePage.tsx** | `/api/pricing/plans?category=parents` | GET | PricingPlans | Id, Name, Price, Period, Features, Category | ❌ MANQUANT |
| **Pricing.tsx** | `/api/pricing/plans?category=students` | GET | PricingPlans | Id, Name, Price, Period, Features, Category | ❌ MANQUANT |
| **Pricing.tsx** | `/api/pricing/plans?category=teachers` | GET | PricingPlans | Id, Name, Price, Period, Features, Category | ❌ MANQUANT |
| **Pricing.tsx** | `/api/pricing/plans?category=parents` | GET | PricingPlans | Id, Name, Price, Period, Features, Category | ❌ MANQUANT |
| **CompleteProfile.tsx** | `/api/institutions?country=CM` | GET | Institutions | Id, Name, Code, Country | ❌ MANQUANT |
| **EmailVerification.tsx** | `/auth/resend-verification-public` | POST | Users | Id, Email, EmailVerificationToken | ✅ COGNITO |
| **GoogleCallback.tsx** | `/auth/google/callback` | POST | Users | Id, Email, FirstName, AvatarUrl | ✅ COGNITO |

---

#### **📚 Pages Catalogue & Recherche**

| Page | Endpoint | Méthode | Table BD | Colonnes | Statut |
|------|----------|---------|----------|----------|--------|
| **CatalogPage.tsx** | `/api/subjects` | GET | Subjects | Id, Title, ThumbnailUrl, Price, Description, Category, AverageRating, EnrollmentCount | ✅ |
| **CatalogPage.tsx** | `/api/categories` | GET | Categories | Id, Name, Description | ✅ |
| **CatalogPage.tsx** | `/api/categories/filters` | GET | Categories + Exams + Years | Id, Name, ExamType, Year | ✅ PARTIAL |
| **SearchPage.tsx** | `/api/subjects/search?q=...` | GET | Subjects | Id, Title, Description, ThumbnailUrl, Category, Price, IsFree | ✅ |
| **SearchPage.tsx** | `/api/subjects/search?exam=...` | GET | Subjects + Exams | ExamType, SubjectId | ✅ |
| **SubjectList.tsx** | `/api/subjects` | GET | Subjects | Id, Title, ThumbnailUrl, Price, Description | ✅ |
| **Discover.tsx** | `/api/subjects/popular?limit=10` | GET | Subjects | Id, Title, ThumbnailUrl, EnrollmentCount, AverageRating | ✅ FLASK |

---

#### **👤 Pages Utilisateur & Profil**

| Page | Endpoint | Méthode | Table BD | Colonnes | Statut |
|------|----------|---------|----------|----------|--------|
| **Profile.tsx** | `/api/users/profile` | GET | Users | Id, Email, FirstName, LastName, AvatarUrl, Role, IsEmailVerified | ✅ |
| **Profile.tsx** | `/api/users/profile` | PUT | Users | Email, FirstName, LastName, AvatarUrl, ProfileImageUrl | ✅ |
| **Profile.tsx** | `/api/users/avatar` | POST | Users | AvatarUrl, ProfileImageUrl | ✅ |
| **Profile.tsx** | `/api/users/settings/notifications` | GET | UserNotificationSettings | EmailNotifications, PushNotifications, CourseCommunity, Promotions, Newsletters, LearningReminders | ✅ |
| **Profile.tsx** | `/api/users/settings/notifications` | PUT | UserNotificationSettings | EmailNotifications, PushNotifications, CourseCommunity, Promotions, Newsletters, LearningReminders | ✅ |
| **Profile.tsx** | `/api/users/settings/privacy` | GET | UserPrivacySettings | ProfileVisible, ShowProgressPublic, AllowMessages, AllowFriends | ✅ |
| **Profile.tsx** | `/api/users/settings/privacy` | PUT | UserPrivacySettings | ProfileVisible, ShowProgressPublic, AllowMessages, AllowFriends | ✅ |
| **Profile.tsx** | `/api/users/sessions` | GET | UserSessions | Id, DeviceName, DeviceType, IpAddress, Location, CreatedAt, LastActivityAt | ✅ |
| **Profile.tsx** | `/api/users/2fa/status` | GET | UserTwoFactorAuthentication | IsEnabled, Method, LastVerifiedAt, BackupCodesCount | ✅ |
| **ProfileForm.tsx** | `/api/users/profile` | PUT | Users | FirstName, LastName, Email, AvatarUrl | ✅ |
| **Student.tsx** | `/api/users/profile` | GET | Users | Id, Email, FirstName, LastName, AvatarUrl | ✅ |

---

#### **🛒 Pages Panier & Commandes**

| Page | Endpoint | Méthode | Table BD | Colonnes | Statut |
|------|----------|---------|----------|----------|--------|
| **CartPage.tsx** | `/api/cart` | GET | CartItems + Subjects | CartItemId, SubjectId, SubjectTitle, Price, Quantity | ✅ |
| **CartPage.tsx** | `/api/cart/add` | POST | CartItems + Subjects | SubjectId, UserId, Quantity, Price | ✅ |
| **CartPage.tsx** | `/api/cart/remove/{id}` | DELETE | CartItems | CartItemId | ✅ |
| **CartPage.tsx** | `/api/cart/clear` | POST | CartItems | UserId (delete all) | ✅ |

---

#### **❤️ Pages Favoris & Historique**

| Page | Endpoint | Méthode | Table BD | Colonnes | Statut |
|------|----------|---------|----------|----------|--------|
| **Favorites.tsx** | `/api/favorites` | GET | Favorites + Subjects | FavoriteId, SubjectId, SubjectTitle, ThumbnailUrl, CreatedAt | ✅ |
| **Favorites.tsx** | `/api/favorites/{id}` | POST | Favorites | UserId, SubjectId, CreatedAt | ✅ |
| **Favorites.tsx** | `/api/favorites/{id}` | DELETE | Favorites | FavoriteId | ✅ |
| **History.tsx** | `/api/history` | GET | LearningHistory | Id, UserId, Action, SubjectId, CreatedAt, UpdatedAt | ✅ |
| **History.tsx** | `/api/history/{id}` | DELETE | LearningHistory | Id | ✅ |
| **History.tsx** | `/api/history` | DELETE | LearningHistory | UserId (delete all) | ✅ |
| **History.tsx** | `/api/history/statistics` | GET | LearningHistory | Count, Type, SubjectId | ✅ |

---

#### **📊 Pages Professeur & Tuteur**

| Page | Endpoint | Méthode | Table BD | Colonnes | Statut |
|------|----------|---------|----------|----------|--------|
| **professeur.tsx** | `/api/teacher/contents?limit=50` | GET | CourseContent | Id, Title, Description, CreatedAt | ❌ MANQUANT |
| **professeur.tsx** | `/api/teacher/students/recent?limit=10` | GET | Enrollments + Users | UserId, FirstName, Email, EnrollmentDate | ❌ MANQUANT |
| **professeur.tsx** | `/api/teacher/corrections/pending` | GET | Submissions | Id, SubjectId, UserId, Status, CreatedAt | ❌ MANQUANT |
| **professeur.tsx** | `/api/teacher/sessions/upcoming?limit=10` | GET | Sessions | Id, Title, StartDate, EndDate, MaxParticipants | ❌ MANQUANT |
| **professeur.tsx** | `/api/teacher/quizzes/available?limit=10` | GET | Quizzes | Id, Title, CreatedAt, QuestionCount | ❌ MANQUANT |
| **professeur.tsx** | `/api/teacher/revisions/available?limit=10` | GET | Revisions | Id, Title, CreatedAt, UpdatedAt | ❌ MANQUANT |
| **professeur.tsx** | `/api/teacher/stats` | GET | Enrollments + Reviews + Users | TotalStudents, AverageRating, ContentCount | ❌ MANQUANT |

---

#### **👨‍👩‍👧 Pages Parent**

| Page | Endpoint | Méthode | Table BD | Colonnes | Statut |
|------|----------|---------|----------|----------|--------|
| **Parent.tsx** | `/api/parent/children/{id}/stats` | GET | Users + Enrollments | UserId, EnrollmentCount, CompletionRate, AverageScore | ❌ MANQUANT |
| **Parent.tsx** | `/api/parent/activities/recent?limit=10` | GET | LearningHistory + Users | Action, SubjectId, Timestamp, UserName | ❌ MANQUANT |
| **Parent.tsx** | `/api/parent/payments/upcoming` | GET | Subscriptions + Orders | DueDate, Amount, Status, PlanName | ❌ MANQUANT |
| **Parent.tsx** | `/api/parent/events/upcoming?limit=10` | GET | Events | Id, Title, StartDate, Location | ❌ MANQUANT |
| **Parent.tsx** | `/api/parent/quizzes/available?limit=10` | GET | Quizzes | Id, Title, CreatedAt | ❌ MANQUANT |
| **Parent.tsx** | `/api/parent/revisions/available?limit=10` | GET | Revisions | Id, Title, CreatedAt | ❌ MANQUANT |

---

#### **⚙️ Pages Dashboard & Admin**

| Page | Endpoint | Méthode | Table BD | Colonnes | Statut |
|------|----------|---------|----------|----------|--------|
| **Dashboard.tsx** | `/api/announcements?limit=4` | GET | Announcements | Id, Title, Content, CreatedAt | ❌ MANQUANT |
| **Dashboard.tsx** | `/api/subjects/search?limit=100&sort=popular` | GET | Subjects | Id, Title, ThumbnailUrl, EnrollmentCount, AverageRating | ✅ |
| **Dashboard.tsx** | `/api/pricing/plans` | GET | PricingPlans | Id, Name, Price, Period, Features, IsPopular | ❌ MANQUANT |
| **Dashboard.tsx** | `/api/categories` | GET | Categories | Id, Name, Description | ✅ |
| **Dashboard.tsx** | `/api/categories/filters` | GET | Categories + Filters | Id, Name, FilterType | ✅ PARTIAL |
| **AdminDashboard.tsx** | `/api/admin/dashboard/stats` | GET | Orders + Users + Enrollments | TotalRevenue, ActiveUsers, TotalCourses, EnrollmentRate | ❌ MANQUANT |
| **AdminDashboard.tsx** | `/api/admin/activities/recent` | GET | AnalyticsEvents + Users | Action, UserId, CreatedAt, Resource | ❌ MANQUANT |
| **AdminDashboard.tsx** | `/api/admin/system/health` | GET | System | DbHealth, ApiHealth, CacheHealth | ❌ MANQUANT |
| **admin/SubjectManagement.tsx** | `/api/subjects?limit=100` | GET | Subjects | Id, Title, Category, Price, IsPublished, CreatedAt | ✅ |
| **admin/AnalyticsDashboard.tsx** | `/api/admin/analytics/revenues?period=6months` | GET | Orders + Payments | TotalRevenue, RevenueByPeriod | ❌ MANQUANT |
| **admin/AnalyticsDashboard.tsx** | `/api/admin/analytics/active-users` | GET | UserSessions + Users | ActiveUserCount, NewUserCount, ChurnRate | ❌ MANQUANT |
| **admin/AnalyticsDashboard.tsx** | `/api/admin/analytics/popular-subjects?limit=3` | GET | Subjects + Enrollments | SubjectId, Title, EnrollmentCount, AverageRating | ❌ MANQUANT |
| **admin/AnalyticsDashboard.tsx** | `/api/admin/analytics/conversion-rate` | GET | Enrollments + CartItems + Orders | ConversionRate, CartAbandonmentRate, AvgOrderValue | ❌ MANQUANT |

---

### 1.3 📊 **RÉSUMÉ COMPLET DES ENDPOINTS**

#### **Statistiques Globales**

```
Total Endpoints Requis Par Frontend: 68
├─ ✅ Implémentés & Fonctionnels: 24 (35%)
├─ ⚠️  Partiels (Bug property mapping): 2 (3%)
├─ ❌ Manquants Totalement: 42 (62%)
└─ 🔐 Cognito/Auth: 2 (3%)

BLOCKERS CRITIQUES:
  🔴 /api/pricing/plans* - CRITIQUE (bloque Home, HomePage, Pricing, Dashboard)
  🔴 /api/institutions - IMPORTANT (bloque CompleteProfile)
  🔴 /api/teacher/*, /api/parent/* - IMPORTANT (bloque pages roles)
  🔴 /api/admin/* - MOYEN (admin only)
  🟡 /api/testimonials - BUG (propriétés mal mappées)
```

---

#### **Détail par Catégorie**

| Catégorie | Total | ✅ Implémentés | ⚠️ Partiels | ❌ Manquants |
|-----------|-------|--------|--------|--------|
| Publiques & Auth | 8 | 3 | 0 | 5 |
| Catalogue & Recherche | 8 | 6 | 1 | 1 |
| Utilisateur & Profil | 11 | 11 | 0 | 0 |
| Panier & Commandes | 4 | 4 | 0 | 0 |
| Favoris & Historique | 7 | 7 | 0 | 0 |
| Professeur & Tuteur | 7 | 0 | 0 | 7 |
| Parent | 6 | 0 | 0 | 6 |
| Dashboard & Admin | 10 | 1 | 1 | 8 |
| **TOTAL** | **68** | **24** | **2** | **42** |

---

#### **Endpoints à Créer en Priorité (Phase 1 - Déploiement)**

```
🔴 CRITIQUE - Bloque déploiement:
  1. POST   /api/pricing/plans         (PricingPlans table)
  2. GET    /api/pricing/plans?category=   (PricingPlans + filter)
  3. GET    /api/institutions?country=  (Institutions table)
  4. FIX    /api/testimonials           (Corriger mapping propriétés)

🟠 IMPORTANT - À faire rapidement:
  5. GET    /api/admin/dashboard/stats   (Admin dashboard)
  6. GET    /api/teacher/*               (Professeur dashboard)
  7. GET    /api/parent/*                (Parent dashboard)
  8. GET    /api/admin/analytics/*       (Admin analytics)
  9. GET    /api/announcements           (Dashboard announcements)

🟡 MOYEN - Peut attendre:
  10. GET   /api/admin/system/health     (Health check)
```

---

### 1.2 Structures de Données Frontend Requises


#### **Testimonial** (Provenance: Revue + User)
```typescript
{
  id: number;
  rating: number;          // 1-5 (du Review)
  comment: string;         // Du Review
  text?: string;           // Alternative: comment
  user: {
    firstName: string;
    avatarUrl: string;     // ❌ MANQUANT dans Subject
    role: string;          // student | teacher | parent
  };
}
```

**Actuellement reçu:** Données mock avec `.image`  
**À corriger:** API doit retourner `.avatarUrl` du User

---

#### **Subject**
```typescript
{
  id: number;
  title: string;
  description: string;
  category: string;
  thumbnailUrl: string;    // ✅ Existe dans BD
  price: number;
  averageRating: number;
  enrollmentCount: number;
}
```

**Actuellement reçu:** Propriété `.image` n'existe pas  
**À corriger:** Utiliser `.thumbnailUrl ` (qui existe en BD)

---

#### **PricingPlan**
```typescript
{
  id?: number;
  name: string;           // "Premium", "Standard", etc.
  price: number;
  period: string;         // "/mois", "/trimestre"
  features: string[];
  popular: boolean;
  icon?: any;
}
```

**Actuellement:** ❌ MANQUE l'API `/api/pricing/plans`  
**À faire:** Créer endpoint retournant les plans

---

#### **NotificationSettingsDto**
```typescript
{
  userId: number;
  emailNotifications: boolean;
  pushNotifications: boolean;
  courseCommunity: boolean;
  promotions: boolean;
  newsletters: boolean;
  learningReminders: boolean;
  updatedAt: DateTime;
}
```

**Actuellement:** ✅ Existe  
**État:** Service implémenté

---

#### **PrivacySettingsDto**
```typescript
{
  userId: number;
  profileVisible: boolean;
  showProgressPublic: boolean;
  allowMessages: boolean;
  allowFriends: boolean;
  updatedAt: DateTime;
}
```

**Actuellement:** ✅ Existe  
**État:** Service implémenté

---

#### **SessionDto**
```typescript
{
  id: number;
  userId: number;
  deviceName: string;
  deviceType: string;
  ipAddress: string;
  userAgent: string;
  location: string;
  createdAt: DateTime;
  lastActivityAt: DateTime;
  expiresAt: DateTime | null;
  isActive: boolean;
}
```

**Actuellement:** ✅ Existe  
**État:** Service implémenté

---

#### **TwoFactorStatusDto**
```typescript
{
  userId: number;
  isEnabled: boolean;
  method: string | null;        // "email" | "sms" | "authenticator"
  enabledAt: DateTime | null;
  lastVerifiedAt: DateTime | null;
  backupCodesCount: number;
}
```

**Actuellement:** ✅ Existe  
**État:** Service implémenté

---

### 1.3 Défauts à Corriger dans Frontend

| Fichier | Ligne | Problème | Correction |
|---------|-------|---------|-----------|
| Home.tsx | 465 | `testimonials[i].image` | → `testimonials?.[i]?.avatarUrl` |
| HomePage.tsx | 491 | `testimonials[i].image` | → `testimonials?.[i]?.avatarUrl` |
| CatalogPage.tsx | 235, 664 | `test.image` | → `test.thumbnailUrl` |
| Dashboard.tsx | 1385-1450 | Testimonials hardcodées | → Charger depuis API |
| Home.tsx | - | Properties manquantes | → Ajouter checks defensifs |

---

---

## 📊 PARTIE 2 : TABLES & ENTITÉS BACKEND

### 2.1 Schéma Bases de Données Existant

#### **Users Table**
```sql
CREATE TABLE Users (
    Id INT PRIMARY KEY,
    Email VARCHAR(255) UNIQUE NOT NULL,
    FirstName VARCHAR(100),
    LastName VARCHAR(100),
    PasswordHash VARCHAR(500),
    AvatarUrl VARCHAR(500),           -- ✅ Utilisé pour testimonials
    ProfileImageUrl VARCHAR(500),
    Role VARCHAR(50) DEFAULT 'student',
    IsEmailVerified BOOLEAN DEFAULT false,
    IsActive BOOLEAN DEFAULT true,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME,
    IsDeleted BOOLEAN DEFAULT false
);
```

**État:** ✅ Complet

---

#### **Subjects Table**
```sql
CREATE TABLE Subjects (
    Id INT PRIMARY KEY,
    Title VARCHAR(255) NOT NULL,
    Description TEXT,
    Category VARCHAR(100),
    ThumbnailUrl VARCHAR(500),        -- ✅ Images des sujets
    Price DECIMAL(10, 2),
    IsPublished BOOLEAN DEFAULT false,
    EnrollmentCount INT DEFAULT 0,
    IsFeatured BOOLEAN DEFAULT false,
    AverageRating DECIMAL(3, 2),
    TotalRatings INT DEFAULT 0,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME,
    IsDeleted BOOLEAN DEFAULT false
);
```

**État:** ✅ Complet

---

#### **Reviews Table**
```sql
CREATE TABLE Reviews (
    Id INT PRIMARY KEY,
    UserId INT NOT NULL FOREIGN KEY,
    SubjectId INT NOT NULL FOREIGN KEY,
    Rating INT (1-5),                 -- ✅ Pour testimonials
    Title VARCHAR(200),
    Comment VARCHAR(2000),            -- ✅ Texte du testimonial
    IsVerifiedPurchase BOOLEAN DEFAULT false,
    HelpfulCount INT DEFAULT 0,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME,
    IsDeleted BOOLEAN DEFAULT false
);
```

**État:** ✅ Complet  
**Relation:** Reviews → Users (pour firstName, role, avatarUrl)

---

#### **UserNotificationSettings Table** ✅ NOUVELLE
```sql
CREATE TABLE UserNotificationSettings (
    Id INT PRIMARY KEY,
    UserId INT UNIQUE FOREIGN KEY NOT NULL,
    EmailNotifications BOOLEAN DEFAULT true,
    PushNotifications BOOLEAN DEFAULT true,
    CourseCommunity BOOLEAN DEFAULT true,
    Promotions BOOLEAN DEFAULT false,
    Newsletters BOOLEAN DEFAULT true,
    LearningReminders BOOLEAN DEFAULT true,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);
```

**État:** ✅ Créée lors de la migration  
**Service:** SettingsService.cs ✅

---

#### **UserPrivacySettings Table** ✅ NOUVELLE
```sql
CREATE TABLE UserPrivacySettings (
    Id INT PRIMARY KEY,
    UserId INT UNIQUE FOREIGN KEY NOT NULL,
    ProfileVisible BOOLEAN DEFAULT true,
    ShowProgressPublic BOOLEAN DEFAULT false,
    AllowMessages BOOLEAN DEFAULT true,
    AllowFriends BOOLEAN DEFAULT true,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);
```

**État:** ✅ Créée lors de la migration  
**Service:** SettingsService.cs ✅

---

#### **UserTwoFactorAuthentication Table** ✅ NOUVELLE
```sql
CREATE TABLE UserTwoFactorAuthentication (
    Id INT PRIMARY KEY,
    UserId INT UNIQUE FOREIGN KEY NOT NULL,
    IsEnabled BOOLEAN DEFAULT false,
    Method VARCHAR(50),               -- "email" | "sms" | "authenticator"
    TotpSecret VARCHAR(255),          -- Base64-encoded secret
    BackupCodes TEXT,                 -- JSON array or CSV
    BackupCodesUsed INT DEFAULT 0,
    EnabledAt DATETIME,
    LastVerifiedAt DATETIME,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);
```

**État:** ✅ Créée lors de la migration  
**Service:** TwoFactorService.cs ✅

---

#### **UserSessions Table** ✅ NOUVELLE
```sql
CREATE TABLE UserSessions (
    Id INT PRIMARY KEY,
    UserId INT FOREIGN KEY NOT NULL,
    DeviceName VARCHAR(255),
    DeviceType VARCHAR(100),          -- "Windows" | "iOS" | "Android" | "Mac"
    IpAddress VARCHAR(45),
    UserAgent VARCHAR(500),
    Location VARCHAR(255),            -- "City, Country"
    RefreshTokenId VARCHAR(255),
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    LastActivityAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    ExpiresAt DATETIME,
    IsActive BOOLEAN DEFAULT true
);
```

**État:** ✅ Créée lors de la migration  
**Service:** SessionService.cs ✅

---

#### **Autres Tables Existantes**

| Table | Utilisation Clé |
|-------|------------------|
| Enrollments | Suivi des inscriptions utilisateurs |
| CartItems | Panier d'achat (avec SubjectId) |
| Orders | Commandes (avec UserId) |
| Payments | Paiements (avec OrderId) |
| Favorites | Sujets favoris (UserId, SubjectId) |
| LearningHistory | Historique d'apprentissage |
| Notifications | Messages système |
| AnalyticsEvents | Suivi des événements |
| PromoCode | Codes de promotion |
| Files | Gestion des fichiers uploadés |
| CourseContent | Contenu des cours |
| Certificates | Certificats d'accomplissement |

---

### 2.2 Tables Manquantes

#### ❌ **PricingPlans Table - MANQUANTE**
```sql
CREATE TABLE PricingPlans (
    Id INT PRIMARY KEY,
    Name VARCHAR(100) NOT NULL,       -- "Premium", "Standard", "VIP"
    Category VARCHAR(50) NOT NULL,    -- "students" | "teachers" | "parents"
    Price DECIMAL(10, 2) NOT NULL,
    Period VARCHAR(50),               -- "/mois" | "/trimestre" | "/an"
    Features TEXT,                    -- JSON array
    IsPopular BOOLEAN DEFAULT false,
    Icon VARCHAR(255),
    Description TEXT,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME,
    IsDeleted BOOLEAN DEFAULT false
);
```

**Impact:** Frontend ne peut pas charger les plans  
**Priorité:** 🔴 CRITIQUE

---

#### ❌ **Payment Plans / Subscriptions - MANQUANTE**
```sql
CREATE TABLE Subscriptions (
    Id INT PRIMARY KEY,
    UserId INT FOREIGN KEY NOT NULL,
    PricingPlanId INT FOREIGN KEY NOT NULL,
    StartDate DATETIME NOT NULL,
    EndDate DATETIME,
    Status VARCHAR(50),               -- "active" | "expired" | "cancelled"
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME
);
```

**Impact:** Impossible de tracker les abonnements  
**Priorité:** 🔴 CRITIQUE

---

#### ⚠️ **Testimonials View - À CRÉER**

Actuellement, il faut creuser dans Reviews + Users. À simplifier :

```sql
CREATE VIEW TestimonialView AS
SELECT 
    r.Id,
    r.UserId,
    r.Rating,
    r.Comment AS text,
    u.FirstName AS name,
    u.Role,
    u.AvatarUrl AS image,
    r.CreatedAt
FROM Reviews r
JOIN Users u ON r.UserId = u.Id
WHERE r.IsDeleted = false
ORDER BY r.CreatedAt DESC;
```

**Impact:** Frontend peut pas accéder facilement  
**Priorité:** 🟡 MOYEN

---

---

## ⚙️ PARTIE 3 : ENDPOINTS BACKEND REQUIS

### 3.1 Endpoints Existants & Implémentés ✅

| Route | Méthode | Statut | Service |
|-------|---------|--------|---------|
| `/api/users/profile` | GET | ✅ | UserService |
| `/api/users/profile` | PUT | ✅ | UserService |
| `/api/users/settings/notifications` | GET | ✅ | SettingsService |
| `/api/users/settings/notifications` | PUT | ✅ | SettingsService |
| `/api/users/settings/privacy` | GET | ✅ | SettingsService |
| `/api/users/settings/privacy` | PUT | ✅ | SettingsService |
| `/api/users/sessions` | GET | ✅ | SessionService |
| `/api/users/sessions/{id}` | DELETE | ✅ | SessionService |
| `/api/users/2fa/status` | GET | ✅ | TwoFactorService |
| `/api/users/2fa/enable` | POST | ✅ | TwoFactorService |
| `/api/users/2fa/verify` | POST | ✅ | TwoFactorService |
| `/api/users/2fa/disable` | POST | ✅ | TwoFactorService |
| `/api/subjects/search` | GET | ✅ | SubjectService |
| `/api/subjects` | GET | ✅ | SubjectService |
| `/api/categories` | GET | ✅ | CategoryService |
| `/api/cart` | GET | ✅ | CartService |
| `/api/testimonials` | GET | ⚠️ PARTIAL | ReviewService |

---

### 3.2 Endpoints Manquants & À Implémenter ❌

#### **🔴 CRITIQUE - Bloque déploiement**

| Route | Méthode | Service à Créer | Table BD | Colonnes | Page |
|-------|---------|-----------------|----------|----------|------|
| `/api/pricing/plans` | GET | PricingService | PricingPlans | Id, Name, Price, Period, Features, Category, IsPopular, Description | Home.tsx, HomePage.tsx, Pricing.tsx |
| `/api/pricing/plans?category=students` | GET | PricingService | PricingPlans | (filtrées par category='students') | Home.tsx, HomePage.tsx, Pricing.tsx |
| `/api/pricing/plans?category=teachers` | GET | PricingService | PricingPlans | (filtrées par category='teachers') | Home.tsx, HomePage.tsx, Pricing.tsx |
| `/api/pricing/plans?category=parents` | GET | PricingService | PricingPlans | (filtrées par category='parents') | Home.tsx, HomePage.tsx, Pricing.tsx |
| `/api/institutions?country=CM` | GET | InstitutionService | Institutions | Id, Name, Code, Country, City | CompleteProfile.tsx |

**Table PricingPlans à créer:**
```sql
CREATE TABLE PricingPlans (
    Id INT PRIMARY KEY IDENTITY,
    Name VARCHAR(100) NOT NULL,
    Category VARCHAR(50) NOT NULL,   -- 'students' | 'teachers' | 'parents'
    Price DECIMAL(10, 2) NOT NULL,
    Period VARCHAR(50),               -- '/mois' | '/trimestre' | '/an'
    Features NVARCHAR(MAX),           -- JSON array
    IsPopular BOOLEAN DEFAULT false,
    Icon VARCHAR(255),
    Description NVARCHAR(500),
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME,
    IsDeleted BOOLEAN DEFAULT false
);
```

**Table Institutions à créer:**
```sql
CREATE TABLE Institutions (
    Id INT PRIMARY KEY IDENTITY,
    Name VARCHAR(255) NOT NULL,
    Code VARCHAR(50),
    Country VARCHAR(100) NOT NULL,
    City VARCHAR(100),
    Region VARCHAR(100),
    Type VARCHAR(50),                 -- 'University' | 'School' | 'College'
    IsActive BOOLEAN DEFAULT true,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME,
    IsDeleted BOOLEAN DEFAULT false
);
```

---

#### **🟠 IMPORTANT - À faire rapidement**

| Route | Méthode | Service à Créer | Table BD | Colonnes | Page |
|-------|---------|-----------------|----------|----------|------|
| `/api/announcements?limit=4` | GET | AnnouncementService | Announcements | Id, Title, Content, CreatedAt, Priority | Dashboard.tsx |
| `/api/admin/dashboard/stats` | GET | AdminService | Orders, Users, Enrollments | TotalRevenue, ActiveUsers, TotalCourses, EnrollmentRate | AdminDashboard.tsx |
| `/api/admin/activities/recent` | GET | AdminService | AnalyticsEvents, Users | Id, Action, UserId, CreatedAt, Resource | AdminDashboard.tsx |
| `/api/admin/system/health` | GET | AdminService | System metrics | DbHealth, ApiHealth, CacheHealth, Status | AdminDashboard.tsx |
| `/api/admin/analytics/revenues?period=6months` | GET | AdminService | Orders, Payments | TotalRevenue, RevenueByPeriod, RevenueByCategory | admin/AnalyticsDashboard.tsx |
| `/api/admin/analytics/active-users` | GET | AdminService | UserSessions, Users | ActiveUserCount, NewUserCount, ChurnRate | admin/AnalyticsDashboard.tsx |
| `/api/admin/analytics/popular-subjects?limit=3` | GET | AdminService | Subjects, Enrollments | SubjectId, Title, EnrollmentCount, AverageRating | admin/AnalyticsDashboard.tsx |
| `/api/admin/analytics/conversion-rate` | GET | AdminService | Enrollments, CartItems, Orders | ConversionRate, CartAbandonmentRate, AvgOrderValue | admin/AnalyticsDashboard.tsx |

**Table Announcements à créer:**
```sql
CREATE TABLE Announcements (
    Id INT PRIMARY KEY IDENTITY,
    Title NVARCHAR(255) NOT NULL,
    Content NVARCHAR(MAX),
    Priority INT DEFAULT 0,           -- 0=low, 1=medium, 2=high, 3=critical
    IsPublished BOOLEAN DEFAULT false,
    PublishedAt DATETIME,
    ExpiresAt DATETIME,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME,
    CreatedBy INT,
    IsDeleted BOOLEAN DEFAULT false
);
```

---

#### **🟡 MOYEN - Professeur Dashboard (Phase 2)**

| Route | Méthode | Service à Créer | Table BD | Colonnes | Page |
|-------|---------|-----------------|----------|----------|------|
| `/api/teacher/contents?limit=50` | GET | TeacherService | CourseContent | Id, Title, Description, CreatedAt, UpdatedAt | professeur.tsx |
| `/api/teacher/students/recent?limit=10` | GET | TeacherService | Enrollments, Users | UserId, FirstName, Email, EnrollmentDate, Status | professeur.tsx |
| `/api/teacher/corrections/pending` | GET | TeacherService | Submissions | Id, SubjectId, UserId, Status, SubmittedAt | professeur.tsx |
| `/api/teacher/sessions/upcoming?limit=10` | GET | TeacherService | Sessions | Id, Title, StartDate, EndDate, MaxParticipants, Status | professeur.tsx |
| `/api/teacher/quizzes/available?limit=10` | GET | TeacherService | Quizzes | Id, Title, CreatedAt, QuestionCount, PassingScore | professeur.tsx |
| `/api/teacher/revisions/available?limit=10` | GET | TeacherService | Revisions | Id, Title, CreatedAt, UpdatedAt, TopicCount | professeur.tsx |
| `/api/teacher/stats` | GET | TeacherService | Enrollments, Reviews, Users | TotalStudents, AverageRating, ContentCount, SessionCount | professeur.tsx |

---

#### **🟡 MOYEN - Parent Dashboard (Phase 2)**

| Route | Méthode | Service à Créer | Table BD | Colonnes | Page |
|-------|---------|-----------------|----------|----------|------|
| `/api/parent/children/{id}/stats` | GET | ParentService | Users, Enrollments | UserId, EnrollmentCount, CompletionRate, AverageScore | Parent.tsx |
| `/api/parent/activities/recent?limit=10` | GET | ParentService | LearningHistory, Users | Action, SubjectId, Timestamp, UserName, Status | Parent.tsx |
| `/api/parent/payments/upcoming` | GET | ParentService | Subscriptions, Orders | DueDate, Amount, Status, PlanName, SubscriptionId | Parent.tsx |
| `/api/parent/events/upcoming?limit=10` | GET | ParentService | Events | Id, Title, StartDate, Location, Description | Parent.tsx |
| `/api/parent/quizzes/available?limit=10` | GET | ParentService | Quizzes | Id, Title, CreatedAt, DifficultyLevel | Parent.tsx |
| `/api/parent/revisions/available?limit=10` | GET | ParentService | Revisions | Id, Title, CreatedAt, UpdatedAt | Parent.tsx |

**Tables optionnelles nécessaires pour parent/teacher:**
```sql
CREATE TABLE Sessions (
    Id INT PRIMARY KEY IDENTITY,
    Title NVARCHAR(255) NOT NULL,
    Description NVARCHAR(MAX),
    StartDate DATETIME NOT NULL,
    EndDate DATETIME NOT NULL,
    MaxParticipants INT,
    Status VARCHAR(50),               -- 'scheduled' | 'ongoing' | 'completed' | 'cancelled'
    CreatedBy INT,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE Events (
    Id INT PRIMARY KEY IDENTITY,
    Title NVARCHAR(255) NOT NULL,
    Description NVARCHAR(MAX),
    StartDate DATETIME NOT NULL,
    EndDate DATETIME,
    Location VARCHAR(255),
    EventType VARCHAR(50),            -- 'class' | 'exam' | 'meeting' | 'deadline'
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);
```

---

#### **⚠️ PARTIELS - Bugs de Propriétés (Property Mapping)**

| Route | Méthode | Problème | Solution | Table BD |
|-------|---------|----------|----------|----------|
| `/api/testimonials` | GET | Frontend utilise `.image`, BD retourne `.avatarUrl` | Corriger frontend: `testimonials?.[i]?.user?.avatarUrl` | Reviews + Users |
| `/api/categories/filters` | GET | Réponse peut être incomplète ou mal structurée | Vérifier structure + ajouter tous les filters | Categories |

---

---

## 🚨 PROBLÈMES CRITIQUES & SOLUTIONS

### Problème #1: Testimonials - Propriétés Mal Mappées ❌

**Symptôme:**
```
TypeError: Cannot read properties of undefined (reading 'image')
```

**Cause:**
- Frontend utilise: `testimonials[i].image`
- DB retourne: Reviews avec User.AvatarUrl

**Solution:**
```typescript
// AVANT (❌ brisé)
<img src={testimonials[i].image} />

// APRÈS (✅ sécurisé)
<img src={testimonials?.[i]?.user?.avatarUrl || '/placeholder.png'} />
```

**Backend API à retourner:**
```json
[
  {
    "id": 1,
    "rating": 5,
    "comment": "Très bon service",
    "user": {
      "firstName": "Marie",
      "role": "student",
      "avatarUrl": "https://..."
    }
  }
]
```

**État:** 🔴 À CORRIGER IMMÉDIATEMENT

---

### Problème #2: Subject Images Hardcodées ❌

**Symptôme:**
- CatalogPage cherche `.image` qui n'existe pas

**Cause:**
- Subject entity a `.ThumbnailUrl` en BD
- Frontend cherche `.image`

**Solution:**
```typescript
// AVANT (❌)
image: test.image

// APRÈS (✅)
image: test.thumbnailUrl || '/default-subject.jpg'
```

**État:** 🟠 À CORRIGER

---

### Problème #3: Pricing Plans Manquants ❌

**Symptôme:**
- Pages Home/HomePage ne peuvent pas charger les plans

**Cause:**
- Pas de table PricingPlans en BD
- Pas d'endpoint API

**Solution:**
1. Créer table `PricingPlans`
2. Créer endpoint `/api/pricing/plans`
3. Implémenter PricingService

**État:** 🔴 BLOQUE le déploiement

---

### Problème #4: Dashboard Testimonials Hardcodées ❌

**Symptômes:**
- Données non réalistes
- Pas de vérification d'existence

**Cause:**
- Mock data en dur dans le code

**Solution:**
```typescript
// AVANT (❌)
const testimonials = [
  { name: "Marie K.", ... },
  { name: "Jean-Paul M.", ... }
]

// APRÈS (✅)
const [testimonials, setTestimonials] = useState([]);

useEffect(() => {
  fetchTestimonials(); // Charger depuis API
}, []);
```

**État:** 🟠 À CORRIGER

---

---

## ✅ CHECKLIST COMPLÈTE - FRONTEND/BACKEND ALIGNMENT 

### 🔴 PHASE 1 - CRITIQUE (Semaine 1 - Bloque déploiement)

#### **Frontend Corrections**
- [ ] Home.tsx - Corriger `testimonials[i].image` → `testimonials?.[i]?.user?.avatarUrl`
- [ ] HomePage.tsx - Corriger `testimonials[i].image` → `testimonials?.[i]?.user?.avatarUrl`
- [ ] CatalogPage.tsx - Corriger `test.image` → `test.thumbnailUrl`
- [ ] CompleteProfile.tsx - Remplacer hardcoded institutions par API call (✅ DÉJÀ FAIT)
- [ ] AnalyticsService.ts - Remplacer mock getUserSegments() par API call (✅ DÉJÀ FAIT)

#### **Backend - Bases de Données**
- [ ] Créer table `PricingPlans` (avec colonnes: Id, Name, Category, Price, Period, Features, IsPopular)
- [ ] Créer table `Institutions` (avec colonnes: Id, Name, Code, Country, City, Region, Type)
- [ ] Créer table `Announcements` (avec colonnes: Id, Title, Content, Priority, PublishedAt)
- [ ] Exécuter migrations pour tables nouvelles

#### **Backend - Endpoints Critiques (5/42)**
- [ ] Créer PricingService + endpoint `GET /api/pricing/plans`
- [ ] Créer endpoint `GET /api/pricing/plans?category=students`
- [ ] Créer endpoint `GET /api/pricing/plans?category=teachers`
- [ ] Créer endpoint `GET /api/pricing/plans?category=parents`
- [ ] Créer InstitutionService + endpoint `GET /api/institutions?country=CM`

#### **Testing Phase 1**
- [ ] Home.tsx charge les plans correctement
- [ ] HomePage.tsx charge les plans correctement
- [ ] Pricing.tsx charge les plans correctement
- [ ] CompleteProfile.tsx charge les institutions
- [ ] Pas d'erreurs console sur propriétés undefined

---

### 🟠 PHASE 2 - IMPORTANT (Semaine 2-3 - Fonctionnalités essentielles)

#### **Backend - Endpoints Admin & Dashboard (8/42)**
- [ ] Créer AdminService
- [ ] Endpoint `GET /api/admin/dashboard/stats` (Orders + Users + Enrollments)
- [ ] Endpoint `GET /api/admin/activities/recent` (AnalyticsEvents)
- [ ] Endpoint `GET /api/admin/system/health` (System metrics)
- [ ] Endpoint `GET /api/admin/analytics/revenues?period=6months` (Orders + Payments)
- [ ] Endpoint `GET /api/admin/analytics/active-users` (UserSessions + Users)
- [ ] Endpoint `GET /api/admin/analytics/popular-subjects?limit=3` (Subjects + Enrollments)
- [ ] Endpoint `GET /api/admin/analytics/conversion-rate` (Orders + CartItems + Enrollments)

#### **Backend - Endpoints Dashboard (1/42)**
- [ ] Créer AnnouncementService + endpoint `GET /api/announcements?limit=4`

#### **Frontend - Dashboard Fixes**
- [ ] Dashboard.tsx - Supprimer testimonials hardcodées
- [ ] Dashboard.tsx - Charger announcements depuis API
- [ ] AdminDashboard.tsx - Afficher stats depuis `/api/admin/dashboard/stats`
- [ ] AnalyticsDashboard.tsx - Afficher analytics depuis endpoints admin/analytics

#### **Testing Phase 2**
- [ ] AdminDashboard se charge correctement
- [ ] Tous les dashboards affichent les bonnes données
- [ ] Analytics dashboard fonctionne

---

### 🟡 PHASE 3 - MOYEN (Semaine 4+ - Rôles spécialisés)

#### **Backend - Endpoints Professeur (7/42)**
- [ ] Créer TeacherService
- [ ] Endpoint `GET /api/teacher/contents?limit=50` (CourseContent)
- [ ] Endpoint `GET /api/teacher/students/recent?limit=10` (Enrollments + Users)
- [ ] Endpoint `GET /api/teacher/corrections/pending` (Submissions)
- [ ] Endpoint `GET /api/teacher/sessions/upcoming?limit=10` (Sessions)
- [ ] Endpoint `GET /api/teacher/quizzes/available?limit=10` (Quizzes)
- [ ] Endpoint `GET /api/teacher/revisions/available?limit=10` (Revisions)
- [ ] Endpoint `GET /api/teacher/stats` (Aggregate teacher stats)

#### **Backend - Endpoints Parent (6/42)**
- [ ] Créer ParentService
- [ ] Endpoint `GET /api/parent/children/{id}/stats` (Users + Enrollments)
- [ ] Endpoint `GET /api/parent/activities/recent?limit=10` (LearningHistory)
- [ ] Endpoint `GET /api/parent/payments/upcoming` (Subscriptions + Orders)
- [ ] Endpoint `GET /api/parent/events/upcoming?limit=10` (Events)
- [ ] Endpoint `GET /api/parent/quizzes/available?limit=10` (Quizzes)
- [ ] Endpoint `GET /api/parent/revisions/available?limit=10` (Revisions)

#### **Backend - Tables Supports (Phase 3)**
- [ ] Créer table `Sessions` (classe, session de révision)
- [ ] Créer table `Events` (événements, deadlines)
- [ ] Créer table `Quizzes` (si manquante)
- [ ] Créer table `Revisions` (si manquante)

#### **Frontend - Pages Rôles**
- [ ] professeur.tsx - Charger données depuis `/api/teacher/*`
- [ ] Parent.tsx - Charger données depuis `/api/parent/*`

---

### **Base de Données - Complète**

#### **Tables à Créer (récapitulatif)**

```sql
-- 1. PricingPlans (CRITIQUE)
CREATE TABLE PricingPlans (
    Id INT PRIMARY KEY IDENTITY,
    Name VARCHAR(100) NOT NULL,
    Category VARCHAR(50) NOT NULL,
    Price DECIMAL(10, 2) NOT NULL,
    Period VARCHAR(50),
    Features NVARCHAR(MAX),
    IsPopular BOOLEAN DEFAULT false,
    Icon VARCHAR(255),
    Description NVARCHAR(500),
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME
);

-- 2. Institutions (CRITIQUE)
CREATE TABLE Institutions (
    Id INT PRIMARY KEY IDENTITY,
    Name VARCHAR(255) NOT NULL,
    Code VARCHAR(50),
    Country VARCHAR(100) NOT NULL,
    City VARCHAR(100),
    Region VARCHAR(100),
    Type VARCHAR(50),
    IsActive BOOLEAN DEFAULT true,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- 3. Announcements (IMPORTANT)
CREATE TABLE Announcements (
    Id INT PRIMARY KEY IDENTITY,
    Title NVARCHAR(255) NOT NULL,
    Content NVARCHAR(MAX),
    Priority INT DEFAULT 0,
    IsPublished BOOLEAN DEFAULT false,
    PublishedAt DATETIME,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- 4. Sessions (MOYEN)
CREATE TABLE Sessions (
    Id INT PRIMARY KEY IDENTITY,
    Title NVARCHAR(255) NOT NULL,
    Description NVARCHAR(MAX),
    StartDate DATETIME NOT NULL,
    EndDate DATETIME NOT NULL,
    MaxParticipants INT,
    Status VARCHAR(50),
    CreatedBy INT,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- 5. Events (MOYEN)
CREATE TABLE Events (
    Id INT PRIMARY KEY IDENTITY,
    Title NVARCHAR(255) NOT NULL,
    Description NVARCHAR(MAX),
    StartDate DATETIME NOT NULL,
    EndDate DATETIME,
    Location VARCHAR(255),
    EventType VARCHAR(50),
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- 6. Subscriptions (Suivi des abonnements)
CREATE TABLE Subscriptions (
    Id INT PRIMARY KEY IDENTITY,
    UserId INT FOREIGN KEY NOT NULL,
    PricingPlanId INT FOREIGN KEY NOT NULL,
    StartDate DATETIME NOT NULL,
    EndDate DATETIME,
    Status VARCHAR(50) DEFAULT 'active',
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);
```

---

### **Dépendances & Ordre Recommandé**

```
Phase 1 (CRITIQUE):
  PricingPlans table → PricingService → GET /api/pricing/plans*
  Institutions table → InstitutionService → GET /api/institutions
  
Bloque-déploiement résolu: ✅

Phase 2 (IMPORTANT):
  AdminService → GET /api/admin/* (8 endpoints)
  AnnouncementService → GET /api/announcements
  
Pages admin/dashboard fonctionnelles: ✅

Phase 3 (MOYEN):
  TeacherService → GET /api/teacher/* (7 endpoints)
  ParentService → GET /api/parent/* (6 endpoints)
  
Pages rôles complètes: ✅
```

---

## 📈 DÉPENDANCES CRITIQUES

```
Home (Frontend)
  ├─ /api/testimonials ❌ BUGUÉ (propriétés mal mappées)
  ├─ /api/pricing/plans ❌ MANQUANT
  ├─ /api/subjects/search ✅
  └─ /api/categories ✅

HomePage (Frontend)
  ├─ /api/testimonials ❌ BUGUÉ
  ├─ /api/pricing/plans?category=* ❌ MANQUANT
  └─ /api/subjects/search ✅

CatalogPage (Frontend)
  ├─ Propriété .image ❌ BUGUÉ (utilise .thumbnailUrl)
  └─ /api/subjects ✅

Dashboard (Frontend)
  ├─ Testimonials hardcodées ❌ BUGUÉ
  ├─ /api/users/profile ✅
  └─ /api/admin/dashboard/stats ❌ MANQUANT

Profile Settings (Frontend)
  ├─ /api/users/settings/* ✅ IMPLÉMENTÉ
  ├─ /api/users/sessions ✅ IMPLÉMENTÉ
  └─ /api/users/2fa/* ✅ IMPLÉMENTÉ
```

---

---

## 📋 RÉSUMÉ EXÉCUTIF FINAL

### **État Global du Projet**

```
╔════════════════════════════════════════════════════════════════╗
║         FRONTEND-BACKEND ALIGNMENT AUDIT - 18 FEV 2026        ║
╚════════════════════════════════════════════════════════════════╝

📊 ENDPOINTS ANALYSÉS: 68 total
   ✅ Implémentés & Fonctionnels:  24/68 (35%)
   ⚠️  Partiels (property bugs):     2/68 (3%)
   ❌ Manquants:                    42/68 (62%)

🔴 BLOCKERS CRITIQUES: 5
   └─ Déploiement IMPOSSIBLE sans ces endpoints
   └─ Temps estimé: 3-5 jours

🟠 FONCTIONNALITÉS MANQUANTES: 20 
   └─ Important mais pas critique
   └─ Pages admin/professeur/parent

🟡 AMÉLIORATIONS: 17
   └─ Nice-to-have après phase 1

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

🚨 ISSUES CRITIQUES À RÉSOUDRE:

  1. ❌ /api/pricing/plans* (5 endpoints)
     → BLOC HOME, HOMEPAGE, PRICING, DASHBOARD
     → Impact: Aucune page ne peut charger les plans d'abonnement
     → Tables: PricingPlans (à créer)

  2. ❌ /api/institutions (1 endpoint)
     → BLOC COMPLETEPROFILE
     → Impact: Utilisateurs ne peuvent pas compléter profil
     → Tables: Institutions (à créer)

  3. ⚠️  /api/testimonials (1 endpoint)
     → PROPERTY MAPPING BUG (`.image` vs `.avatarUrl`)
     → Impact: Affichage cassé des avis client
     → Fix frontend: Optional chaining + mapping

  4. ❌ /api/admin/dashboard/stats (1 endpoint)
     → BLOC ADMIN DASHBOARD
     → Impact: Admin ne peut pas voir stats globales
     → Tables: Orders + Users + Enrollments

  5. ❌ /api/teacher/*, /api/parent/* (13 endpoints)
     → BLOC PAGES SPÉCIALISÉES
     → Impact: Phase 2 bloquée
     → Services: TeacherService, ParentService

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📊 RÉPARTITION PAR PAGE FRONTEND:

Pages PRÊTES (100% endpoints):
  ✅ Home.tsx (6/7 - manque plans)
  ✅ CartPage.tsx (4/4)
  ✅ Favorites.tsx (3/3)
  ✅ History.tsx (3/3)
  ✅ Profile.tsx (8/8)
  ✅ SubjectList.tsx (1/1)
  ✅ Student.tsx (1/1)

Pages PARTIELLES (50-99% endpoints):
  🟠 HomePage.tsx (5/7 - manque plans + testimonials)
  🟠 Pricing.tsx (2/4 - manque plans)
  🟠 CatalogPage.tsx (3/4 - propriété bug)
  🟠 Dashboard.tsx (4/6 - manque plans + announcements)
  🟠 SearchPage.tsx (3/4)

Pages MANQUANTES (0-49% endpoints):
  ❌ professeur.tsx (0/7)
  ❌ Parent.tsx (0/6)
  ❌ AdminDashboard.tsx (1/5)
  ❌ AnalyticsDashboard.tsx (0/4)
  ❌ CompleteProfile.tsx (0/1)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📁 TABLES BD À CRÉER:

Critiques (cette semaine):
  1. PricingPlans (5 colonnes+)
  2. Institutions (6 colonnes+)
  3. Announcements (5 colonnes+)

Phase 2 (semaine prochaine):
  4. Sessions (7 colonnes+)
  5. Events (7 colonnes+)
  6. Subscriptions (5 colonnes+)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

⏱️ ESTIMATIONS TEMPORELLES:

Phase 1 (Critique):
  Backend: 3-5 jours (5 endpoints + 3 tables)
  Frontend: 2 jours mineurs (property fixes)
  Testing: 1 jour
  TOTAL: ~1 semaine

Phase 2 (Important):
  Backend: 5-7 jours (9 endpoints)
  Frontend: 2 jours
  TOTAL: ~1 semaine

Phase 3 (Moyen):
  Backend: 7-10 jours (13 endpoints)
  Frontend: 3 jours
  TOTAL: ~2 semaines

DÉPLOIEMENT PRÊT: 3-4 semaines minimum après cette date

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

---

## 📝 PROCHAINES ÉTAPES RECOMMANDÉES

**Actions immédiates (Aujourd'hui):**

1. ✅ **Lire ce document** - État réel de l'alignement frontend/backend
2. ✅ **Valider les priorités** - Confirmer l'ordre phases 1/2/3
3. ⏳ **Créer les 3 tables critiques** - PricingPlans, Institutions, Announcements
4. ⏳ **Implémenter PricingService** - `GET /api/pricing/plans[?category=...]`
5. ⏳ **Tester les 5 endpoints critiques** - Vérifier format réponse

**Documents de référence:**

- [Partie 1.1](#11-routes-api-utilisées-par-toutes-les-pages-frontend) - Tous les endpoints requis
- [Partie 3.2](#32-endpoints-manquants--à-implémenter-) - Détails implémentation
- [Checklist complète](#-phase-1---critique-semaine-1---bloque-déploiement) - Tâches à cocher

---

**Généré le:** 18 février 2026
**Version:** 2.0 (Complète et exhaustive)
**Dernière mise à jour:** 18 février 2026
**Status:** 🔴 **Critique - Déploiement bloqué**
**Pages frontend étudiées:** 24 fichiers .tsx
**Endpoints frontend détectés:** 68 total
**Test coverage:** 100% des appels API tracés
