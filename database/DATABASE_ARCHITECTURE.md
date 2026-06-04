# 📊 Architecture Base de Données PostgreSQL - REUSSIR

## Vue d'ensemble

Schéma complet optimisé pour une plateforme éducative avec authentification AWS Cognito, adaptée à la méthodologie Agile/Scrum.

---

## 🎯 Principes de Design

### 1. **Intégration Cognito AWS**
- **Pas de table de login** : Cognito gère l'authentification
- Table `users` : contient seulement `cognito_id` (clé d'accès)
- Table `user_profiles` : enrichissement des données utilisateur

### 2. **Scalabilité & Performance**
- ✅ Index stratégiques sur les colonnes fréquemment filtrées
- ✅ Foreign keys avec `ON DELETE CASCADE` ou `SET NULL`
- ✅ Partitioning par date pour gros volumes (analytiques)
- ✅ JSON pour données flexibles (métadonnées, options quizz, etc)

### 3. **Flexibilité Agile**
- Tables `sprints` et `features` pour suivi interne
- Champs `status` dans tous les entités critiques
- `created_at`/`updated_at` pour l'audit
- `JSONB` pour éviter migrations futures

---

## 📋 Structure des Tables

### **SECTION 1 : UTILISATEURS** (5 tables)

#### `users` 👤
Synchronisée avec AWS Cognito. **Source de vérité** pour identité.

| Colonne | Type | Remarques |
|---------|------|----------|
| `id` | SERIAL PK | Clé locale |
| `cognito_id` | VARCHAR(255) UNIQUE | ID AWS Cognito - **point de synchro** |
| `email` | VARCHAR(255) UNIQUE | Email du compte Cognito |
| `first_name`, `last_name` | VARCHAR(100) | Données optionnelles |
| `phone` | VARCHAR(20) | Optionnel |
| `profile_image_url` | VARCHAR(500) | Avatar |
| `is_active` | BOOLEAN | Soft delete possible |
| `email_verified` | BOOLEAN | Reflète status Cognito |
| `last_login` | TIMESTAMP | Audit trail |
| `created_at`, `updated_at` | TIMESTAMP | Timing |

```sql
-- Créer utilisateur après Cognito sign-up
INSERT INTO users (cognito_id, email, first_name, last_name)
VALUES ('aws-cognito-uuid', 'user@example.com', 'Jean', 'Dupont');
```

#### `user_profiles` 📈
Enrichissement du profil : rôle, niveau, objectifs.

| Colonne | Type | Remarques |
|---------|------|----------|
| `user_id` | INT FK UNIQUE | Relation 1:1 avec users |
| `role` | VARCHAR(50) | student, instructor, admin, moderator |
| `level` | VARCHAR(50) | débutant, intermédiaire, avancé, expert |
| `learning_goal` | TEXT | "Maîtriser Python", "Passer le bac", etc |
| `specialization` | VARCHAR(100) | Domaine d'expertise |
| `total_hours_learning` | INT | Agrégé à partir de content_progress |
| `certificates_count` | INT | Compteur rapide |
| `rating` | NUMERIC(3,2) | Note moyenne (0-5) |
| `is_instructor_verified` | BOOLEAN | Status premium |

#### `user_preferences` ⚙️
Stocke les préférences utilisateur sans migration.

| Colonne | Type | Remarques |
|---------|------|----------|
| `user_id` | INT FK UNIQUE | Relation 1:1 |
| `notification_email` | BOOLEAN | Opt-in emails |
| `theme_mode` | VARCHAR(20) | light, dark, auto |
| `language_ui` | VARCHAR(10) | fr, en, es, etc |
| `subtitle_preference` | VARCHAR(50) | Langue sous-titres |
| `marketing_emails` | BOOLEAN | RGPD compliance |

---

### **SECTION 2 : CONTENU & COURS** (6 tables)

#### `categories` 📚
Taxonomie hiérarchique des sujets.

```sql
-- Exemple structure hiérarchique
Programmation (parent_id = NULL)
├── Python (parent_id = 1)
├── JavaScript (parent_id = 1)
└── DevOps (parent_id = 1)

Intelligence Artificielle (parent_id = NULL)
├── Machine Learning (parent_id = 3)
└── NLP (parent_id = 3)
```

| Colonne | Type | Remarques |
|---------|------|----------|
| `slug` | VARCHAR(100) UNIQUE | URL-friendly: "python-avance" |
| `parent_id` | INT FK | Catégorie parente (NULL = racine) |
| `is_active` | BOOLEAN | Déactiver sans supprimer |

#### `courses` 🎓
Cours/sujets principaux - cœur de la plateforme.

| Colonne | Type | Remarques |
|---------|------|----------|
| `slug` | VARCHAR(255) UNIQUE | `/courses/python-poo-avance` |
| `category_id` | INT FK | Lien taxonomie |
| `instructor_id` | INT FK | Créateur du cours |
| `price` | NUMERIC(10,2) | 0 = gratuit |
| `level` | VARCHAR(50) | débutant, intermédiaire, avancé |
| `duration_hours` | INT | Durée totale estimée |
| `enrollment_count` | INT | Compteur rapide (mis à jour à chaque inscription) |
| `completion_count` | INT | Compteur rapide (mis à jour après complétude) |
| `average_rating` | NUMERIC(3,2) | Agrégé de course_reviews |
| `total_ratings` | INT | Nombre d'avis |
| `learning_objectives` | VARCHAR(500) | JSON: ["Maîtriser POO", "Développer apps"] |
| `tags` | VARCHAR(500) | JSON: ["python", "poo", "intermediaire"] |
| `is_featured` | BOOLEAN | Affichage en avant |
| `featured_until` | TIMESTAMP | Quand retirer du featured |

#### `course_sections` 📖
Découpe un cours en sections logiques (chapitres).

```sql
-- Exemple cours "Python POO Avancée"
Course (1): Python POO Avancée
├── Section (1): Fondamentaux POO
├── Section (2): Design Patterns
├── Section (3): Héritage & Polymorphisme
└── Section (4): Projet Final
```

#### `course_contents` 🎬
Contenu granulaire : vidéos, documents, quizz.

| Colonne | Type | Remarques |
|---------|------|----------|
| `type` | VARCHAR(50) | video, document, quiz, exercise, resource |
| `video_duration_seconds` | INT | Durée vidéo |
| `is_preview` | BOOLEAN | Gratuit en aperçu |
| `difficulty` | NUMERIC(3,2) | 0-1, pour recommandation |
| `download_allowed` | BOOLEAN | Permet téléchargement |

#### `course_resources` 📎
Ressources additionnelles : code, templates, PDFs.

| Colonne | Type | Remarques |
|---------|------|----------|
| `resource_type` | VARCHAR(50) | pdf, zip, github, code, template |
| `file_size_mb` | INT | Pour UX (afficher taille) |
| `download_count` | INT | Metrics |

---

### **SECTION 3 : INSCRIPTIONS & PROGRESSION** (3 tables)

#### `enrollments` ✅
Lien utilisateur ↔ cours avec tracking de progression.

| Colonne | Type | Remarques |
|---------|------|----------|
| `progress_percentage` | NUMERIC(5,2) | 0-100, calculé par backend |
| `time_spent_seconds` | INT | Total secondes sur tous contenus |
| `status` | VARCHAR(50) | active, paused, completed, dropped |
| `grade` | NUMERIC(3,2) | Note finale 0-100 (si exam/quiz final) |
| `certificate_url` | VARCHAR(500) | URL du certificat PDF généré |

```sql
-- Vérifier progression utilisateur
SELECT e.*, c.title, 
       ROUND(100.0 * COUNT(cp.id) / COUNT(cc.id), 2) as completion_rate
FROM enrollments e
JOIN courses c ON e.course_id = c.id
LEFT JOIN content_progress cp ON e.id = cp.enrollment_id
LEFT JOIN course_contents cc ON c.id = cc.course_id
WHERE e.user_id = 42
GROUP BY e.id;
```

#### `content_progress` 📊
Tracking granulaire par contenu (vidéo, quiz, etc).

| Colonne | Type | Remarques |
|---------|------|----------|
| `watch_percentage` | NUMERIC(5,2) | % vidéo regardée (0-100) |
| `attempts` | INT | Nombre d'essais (pour quiz) |
| `score` | NUMERIC(5,2) | Score si quiz/exercice |
| `UNIQUE(enrollment_id, content_id)` | CONSTRAINT | Pas de doublons |

#### `learning_history` 📜
Audit trail complet de toutes actions utilisateur.

| Colonne | Type | Remarques |
|---------|------|----------|
| `activity_type` | VARCHAR(50) | view, watch, complete, quiz, download, etc |
| `activity_data` | JSONB | Flexible: `{"score": 85, "time": 1200}` |
| `ip_address` | VARCHAR(45) | IPv4/IPv6 pour analytics |
| `user_agent` | TEXT | Browser info |

---

### **SECTION 4 : QUIZ & EXERCICES** (4 tables)

#### `quizzes` 🧪
Définition des quizz/tests/examens.

| Colonne | Type | Remarques |
|---------|------|----------|
| `type` | VARCHAR(50) | quiz, exam, exercise, assignment |
| `pass_percentage` | NUMERIC(5,2) | Note requise (défaut 70) |
| `time_limit_minutes` | INT | Limite de temps (NULL = illimité) |
| `show_correct_answers` | BOOLEAN | Afficher réponses après ? |
| `allow_retake` | BOOLEAN | Peut-on re-tenter ? |
| `max_attempts` | INT | Nombre de tentatives max |
| `is_graded` | BOOLEAN | Compte pour la note final ? |

#### `quiz_questions` ❓
Questions du quiz.

| Colonne | Type | Remarques |
|---------|------|----------|
| `question_type` | VARCHAR(50) | multiple-choice, true-false, short-answer, essay, matching |
| `points` | NUMERIC(5,2) | Points pour cette question |
| `explanation` | TEXT | Explication si mauvaise réponse |

#### `quiz_question_options` 🔲
Choix de réponse pour questions QCM.

| Colonne | Type | Remarques |
|---------|------|----------|
| `is_correct` | BOOLEAN | Réponse correcte ? |
| `order_index` | INT | Ordre affichage (peut être aléatoire) |

#### `quiz_responses` 📝
Réponses de l'utilisateur.

| Colonne | Type | Remarques |
|---------|------|----------|
| `responses_data` | JSONB | Structure complète des réponses |
| `is_passed` | BOOLEAN | A-t-il obtenu ≥ pass_percentage ? |
| `percentage` | NUMERIC(5,2) | Score en % |

---

### **SECTION 5 : E-COMMERCE** (6 tables)

#### `cart_items` 🛒
Panier utilisateur.

```sql
-- Ajouter au panier
INSERT INTO cart_items (user_id, course_id, price)
VALUES (42, 5, 29.99);
```

#### `orders` 📦
Commandes historiques.

| Colonne | Type | Remarques |
|---------|------|----------|
| `order_number` | VARCHAR(50) UNIQUE | Facture "ORD-2025-001234" |
| `status` | VARCHAR(50) | pending, completed, failed, refunded |
| `payment_provider` | VARCHAR(50) | stripe, paypal, etc |
| `transaction_id` | VARCHAR(255) | ID du fournisseur pour traçabilité |
| `invoice_url` | VARCHAR(500) | PDF de la facture |

#### `order_items` 📋
Articles dans une commande.

#### `coupons` 🎟️
Codes promo et réductions.

| Colonne | Type | Remarques |
|---------|------|----------|
| `discount_type` | VARCHAR(20) | percentage, fixed |
| `applicable_courses` | VARCHAR(500) | JSON: [1, 2, 5] ou NULL = tous |
| `current_uses` | INT | Tracker le nombre d'utilisations |
| `valid_until` | TIMESTAMP | Expiration |

#### `favorites` ⭐
Wishlist utilisateur.

#### `refunds` 💰
Remboursements.

| Colonne | Type | Remarques |
|---------|------|----------|
| `status` | VARCHAR(50) | pending, approved, rejected, completed |
| `reason` | VARCHAR(255) | Motif du remboursement |

---

### **SECTION 6 : SOCIAL & ENGAGEMENT** (5 tables)

#### `course_reviews` ⭐
Avis et notes des utilisateurs.

| Colonne | Type | Remarques |
|---------|------|----------|
| `is_verified_purchase` | BOOLEAN | Vérifier qu'il a acheté |
| `helpful_count` | INT | "Utile" votes |
| `is_approved` | BOOLEAN | Modération |

#### `content_comments` 💬
Commentaires sur des vidéos/contenus.

| Colonne | Type | Remarques |
|---------|------|----------|
| `parent_comment_id` | INT FK | Pour threads de replies |
| `is_pinned` | BOOLEAN | Épingler comment important |

#### `discussion_threads` 💭
Forums par cours.

| Colonne | Type | Remarques |
|---------|------|----------|
| `category` | VARCHAR(50) | questions, announcements, resources, general |
| `is_pinned` | BOOLEAN | Épingler annonce importante |
| `is_closed` | BOOLEAN | Fermer suite (résolu) |

#### `discussion_replies` 🗨️
Réponses aux discussions.

| Colonne | Type | Remarques |
|---------|------|----------|
| `is_instructor_reply` | BOOLEAN | Réponse official du prof ? |
| `is_solution` | BOOLEAN | Marquer comme solution |

#### `notifications` 🔔
Système de notifications.

| Colonne | Type | Remarques |
|---------|------|----------|
| `notification_type` | VARCHAR(50) | course_update, new_message, review, achievement, price_drop |
| `related_entity_type` | VARCHAR(50) | course, comment, review, order |
| `action_url` | VARCHAR(500) | Lien direct vers action |

---

### **SECTION 7 : CERTIFICATS & ACHIEVEMENTS** (3 tables)

#### `certificates` 🏆
Certificats d'accomplissement.

| Colonne | Type | Remarques |
|---------|------|----------|
| `certificate_number` | VARCHAR(100) UNIQUE | "CERT-2025-ABC123-42" |
| `expires_at` | TIMESTAMP | Certificat valable X années |
| `is_revoked` | BOOLEAN | Revoquer un certificat |

#### `badges` 🎖️
Badges/achievements.

| Colonne | Type | Remarques |
|---------|------|----------|
| `criteria_type` | VARCHAR(50) | courses_completed, hours_learned, rating_received |
| `criteria_value` | INT | "20 cours" ou "50 heures" |

#### `user_badges` 🏅
Badges obtenus par utilisateur.

---

### **SECTION 8 : ANALYTIQUES** (3 tables)

#### `analytics_events` 📊
Événements bruts pour analytics.

| Colonne | Type | Remarques |
|---------|------|----------|
| `event_type` | VARCHAR(50) | page_view, click, signup, enroll, purchase, quiz_submit |
| `event_data` | JSONB | Flexible: `{"score": 85, "duration": 1200}` |
| `session_id` | VARCHAR(255) | Tracker session utilisateur |

#### `daily_statistics` 📈
Agrégation quotidienne (dashboards).

| Colonne | Type | Remarques |
|---------|------|----------|
| `stat_date` | DATE UNIQUE | Clé de partition |
| `active_users` | INT | Utilisateurs actifs ce jour |
| `total_revenue` | NUMERIC(12,2) | Revenus du jour |

#### `cohort_analytics` 👥
Analyse par cohorte (utilisateurs par date inscription).

| Colonne | Type | Remarques |
|---------|------|----------|
| `cohort_date` | DATE | Date d'inscription de la cohorte |
| `week_1_retention_percentage` | NUMERIC(5,2) | % actifs après 1 semaine |
| `completion_rate` | NUMERIC(5,2) | % qui complètent course |

---

### **SECTION 9 : MODÉRATION** (1 table)

#### `abuse_reports` ⚠️
Signalements de contenu inapproprié.

| Colonne | Type | Remarques |
|---------|------|----------|
| `status` | VARCHAR(50) | pending, investigating, resolved, dismissed |
| `action_taken` | VARCHAR(100) | warning, suspension, deletion |

---

### **SECTION 10 : ML/RECOMMANDATIONS** (2 tables)

#### `recommendations` 🎯
Recommandations générées pour utilisateurs.

| Colonne | Type | Remarques |
|---------|------|----------|
| `recommendation_engine` | VARCHAR(50) | collaborative_filtering, content_based, trending |
| `recommendation_score` | NUMERIC(3,2) | 0-1, confiance de la recommandation |
| `clicked_at` | TIMESTAMP | Audit: utilisateur a cliqué ? |
| `enrolled_at` | TIMESTAMP | Audit: s'est inscrit ? |

#### `progress_predictions` 🔮
Prédictions de complétude/drop-out.

| Colonne | Type | Remarques |
|---------|------|----------|
| `completion_probability` | NUMERIC(3,2) | 0-1, chance complétude |
| `risk_factor` | VARCHAR(50) | low, medium, high (drop-out) |
| `suggested_actions` | JSONB | ["Envoyer email", "Réduire charge"] |

---

### **SECTION 11 : AGILE/SCRUM** (2 tables)

#### `sprints` 🏃
Sprints de développement.

| Colonne | Type | Remarques |
|---------|------|----------|
| `status` | VARCHAR(50) | planning, active, completed |
| `goal` | TEXT | Objectif du sprint |

#### `features` ✨
Tâches/features à développer.

| Colonne | Type | Remarques |
|---------|------|----------|
| `type` | VARCHAR(50) | feature, bug, improvement, task |
| `story_points` | INT | Effort d'estimation |
| `status` | VARCHAR(50) | todo, in_progress, in_review, done |

---

## 🔗 Relations Principales

```
UTILISATEURS
├── users (base)
├── user_profiles (enrichissement)
└── user_preferences (settings)

CONTENU
├── categories (hiérarchie)
├── courses (sujets)
├── course_sections (chapitres)
└── course_contents (vidéos, docs, quiz)

APPRENTISSAGE
├── enrollments (inscription)
├── content_progress (progression granulaire)
└── learning_history (audit trail)

E-COMMERCE
├── cart_items
├── orders → order_items
└── coupons

ENGAGEMENT
├── course_reviews
├── content_comments
├── discussion_threads → discussion_replies
└── notifications

EVALUATION
├── quizzes
├── quiz_questions → quiz_question_options
└── quiz_responses

CERTIFICATS & RÉCOMPENSES
├── certificates
├── badges → user_badges

ANALYTICS
├── analytics_events
├── daily_statistics
└── cohort_analytics
```

---

## 📊 Statistiques Recommandées

### Queries fréquentes à optimiser :

```sql
-- 1️⃣ Progression utilisateur
SELECT e.id, c.title, e.progress_percentage, e.status
FROM enrollments e
JOIN courses c ON e.course_id = c.id
WHERE e.user_id = ?
ORDER BY e.created_at DESC;

-- 2️⃣ Top courses
SELECT c.id, c.title, c.average_rating, COUNT(e.id) as enrollments
FROM courses c
LEFT JOIN enrollments e ON c.id = e.course_id
WHERE c.is_published = true
GROUP BY c.id
ORDER BY c.average_rating DESC
LIMIT 10;

-- 3️⃣ Utilisateurs actifs (derniers 7 jours)
SELECT COUNT(DISTINCT user_id)
FROM analytics_events
WHERE created_at >= CURRENT_DATE - INTERVAL '7 days';

-- 4️⃣ Revenue
SELECT DATE(order_date), SUM(final_amount)
FROM orders
WHERE status = 'completed'
GROUP BY DATE(order_date)
ORDER BY DATE(order_date) DESC;
```

---

## 🛡️ Sécurité & Conformité RGPD

✅ **Authentification Cognito** : Pas de stockage de mots de passe
✅ **Soft deletes** : `is_active = false` sur utilisateurs
✅ **Audit trail** : `learning_history` pour compliance
✅ **Marketing opt-in** : `user_preferences.marketing_emails`
✅ **Data minimization** : Champs justes nécessaires

### Politique suppression (DROIT À L'OUBLI)
```sql
-- Pseudonymiser au lieu de supprimer
UPDATE users
SET cognito_id = 'DELETED_' || id,
    email = 'deleted_' || id || '@deleted.local',
    first_name = NULL,
    last_name = NULL,
    is_active = false
WHERE id = ?;

-- Learning history reste (anonyme)
```

---

## 📈 Scalabilité Future

### Partitioning (à implémenter après 1M+ rows) :
```sql
-- Analytics events par mois
ALTER TABLE analytics_events PARTITION BY RANGE (DATE_TRUNC('month', created_at));

-- Learning history par trimestre
ALTER TABLE learning_history PARTITION BY RANGE (DATE_TRUNC('quarter', created_at));
```

### Read replicas :
- Copier `analytics_events`, `daily_statistics`, `cohort_analytics` vers read replica
- Laisser writes sur master DB
- Rapports/dashboards sur read replicas

---

## 🚀 Mise en place

### Phase 1 - Créer schema
```bash
psql -h 98.86.67.128 -U gogivam -d winplus_db -f SCHEMA_AGILE_COMPLET.sql
```

### Phase 2 - Données de base
```bash
# Catégories, badges, utilisateurs test
psql -h 98.86.67.128 -U gogivam -d winplus_db -f init_data.sql
```

### Phase 3 - Intégration Cognito
- Créer utilisateurs via Cognito sign-up
- Lambda AWS déclenche insertion dans table `users`
- Frontend récupère `cognito_id` après auth

---

## 📝 Notes Importantes

1. **Cognito = Source de vérité** : Ne jamais modifier `cognito_id` après création
2. **Compteurs rapides** : `enrollment_count`, `completion_count` sur courses doivent être synchronisés (trigger SQL ou backend)
3. **JSON flexibility** : `learning_objectives`, `tags`, `activity_data`, `responses_data` permettent évolution sans migration
4. **Performance** : Créer index sur `user_id`, `course_id` sur tables grosses
5. **Archivage** : Après 2 ans, archiver `analytics_events` → data warehouse

---

**Auteur** : Generated by AI  
**Date** : Janvier 2025  
**Status** : ✅ Production Ready
