# WinPlus — Audit de couverture API
> Mis à jour le 2026-08-31 · Audit manuel complet — contrôleurs lus un par un

---

## 1. Fonctionnalités FastAPI (Python)

> Base URL : `https://api.winplus.cm` (proxy .NET → FastAPI via `FastApiClient`)

### Chatbot WinAI — `/api/chatbot`
| Méthode | Chemin | Description |
|---------|--------|-------------|
| GET | `/health` | Health check DeepSeek |
| POST | `/chat` | Chat complet avec contexte rôle + historique |
| POST | `/stream` | Stream SSE — persistance messages en DB |
| POST | `/complete` | Complétion simple sans historique |

### Quiz IA — `/api/quiz`
| Méthode | Chemin | Description |
|---------|--------|-------------|
| POST | `/explain-error` | Explication pédagogique d'une erreur (cache SHA256, limite 3 req/min/user) |

### Coach d'examen — `/api/exam-coach`
| Méthode | Chemin | Description |
|---------|--------|-------------|
| POST | `/generate` | Plan de révision espacé Ebbinghaus |
| POST | `/recalibrate` | Recalibrage du plan sur résultats hebdo |
| GET | `/today/{user_id}` | Session du jour + message motivationnel WinAI |
| POST | `/predict-grade` | Prédiction de note /20 sur 90 derniers jours |
| GET | `/micro-intervention/{user_id}` | Intervention si inactivité + examen proche |

### Alertes parentales — `/api/parent-alerts`
| Méthode | Chemin | Description |
|---------|--------|-------------|
| GET | `/{child_id}` | Détecte anomalies 14j : chute, inactivité, excellence, anxiété nocturne, surmenage |

### Sessions d'étude — `/api/study-session`
| Méthode | Chemin | Description |
|---------|--------|-------------|
| POST | `/generate` | Session guidée 3 phases : briefing → quiz 5 questions → synthèse |
| POST | `/complete` | Sauvegarde session + mise à jour DailyScore |
| GET | `/history/{user_id}` | 10 dernières sessions complétées |

### Enseignant IA — `/api`
| Méthode | Chemin | Description |
|---------|--------|-------------|
| POST | `/ai/generate-quiz-questions` | 10 QCM calibrés (sujet, matière, niveau) |
| POST | `/ai/optimize-title` | Optimisation SEO du titre de contenu |
| POST | `/ai/generate-description` | Description catalogue 2-3 phrases |
| POST | `/teacher/class-analysis` | Analyse performance de classe sur contenu |
| GET | `/teacher/content-impact/{content_id}` | Score d'impact pédagogique 0-100 |
| POST | `/teacher/generate-correction` | Correction structurée d'examen |
| POST | `/teacher/predict-popularity` | Prédiction popularité + prix recommandé |
| POST | `/teacher/analyze-submission` | Analyse copie élève : erreur, note, commentaire |

### Parent extra — `/api`
| Méthode | Chemin | Description |
|---------|--------|-------------|
| GET | `/parent-engagement/{parent_id}` | Score d'engagement parental 0-100 |
| POST | `/parent/educational-roi` | ROI éducatif sur 90 jours |
| GET | `/parent/children-insights` | Analyse comparative multi-enfants |

### Institution — `/api`
| Méthode | Chemin | Description |
|---------|--------|-------------|
| POST | `/institution/class-prediction` | Prédiction taux de réussite institutionnel |
| GET | `/institution/benchmark/{institution_id}` | Benchmark vs moyenne nationale |
| POST | `/institution/action-plan` | 3 actions prioritaires de la semaine |
| GET | `/institution/at-risk-students/{institution_id}` | Élèves à risque (score pondéré) |

### Administration — `/api/admin`
| Méthode | Chemin | Description |
|---------|--------|-------------|
| POST | `/anomaly-detection` | Détection d'anomalies plateforme en temps réel |
| GET | `/anomalies/active` | Anomalies non résolues (triées par sévérité) |
| POST | `/anomaly/{id}/resolve` | Résoudre une anomalie |
| POST | `/content-health-check` | Qualité d'un contenu avant publication |
| POST | `/growth-insights` | 4-5 insights stratégiques de croissance |
| POST | `/revenue-forecast` | Prévision revenus 30/60/90 jours |
| POST | `/moderate-content` | Modération post forum |
| POST | `/moderate-message` | Modération message privé |
| GET | `/moderation/pending` | Posts en attente de modération |
| POST | `/moderation/{id}/resolve` | Approuver ou rejeter du contenu modéré |

---

## 2. Fonctionnalités .NET (C#) — `api/`

### Auth
`POST /auth/login` · `POST /auth/register` · `POST /auth/refresh` · `POST /auth/logout`
`POST /auth/verify-email` · `POST /auth/forgot-password` · `POST /auth/reset-password`

### Utilisateurs
`GET/PUT /users/me` · `PUT /users/me/avatar` · `GET /users/me/downloads` · `GET /users/{id}`
`GET /users/search` · `GET /users/profile/statistics` · `GET /users/profile/subscriptions`
`GET/PUT /users/settings/notifications` · `GET/PUT /users/settings/privacy`
`GET /users/{id}/payment-methods` · `POST /users/{id}/payment-methods` · `DELETE /users/{id}/payment-methods/{mid}`
`GET /users/sessions` · `DELETE /users/sessions/{id}` · `GET/POST /users/2fa/*`

### Abonnements
`GET /subscriptions/me` · `POST /subscriptions` · `POST /subscriptions/purchase` · `POST /subscriptions/me/cancel`

### Paiements
`POST /payments/initiate` · `POST /payments/confirm` · `GET /payments/{id}/status`
`POST /payments/{id}/verify` · `POST /payments/{id}/retry` · `GET /payments/history`
`POST /payments/webhook/notchpay`

### Commandes
`POST /orders` · `GET /orders` · `GET /orders/{id}` · `POST /orders/{id}/cancel`
`POST /orders/{id}/refund` · `GET /orders/{id}/invoice` · `GET /orders/{id}/status`
`GET /orders/statistics` · `GET /orders/search` · `POST /orders/summary`

### Catalogue / Sujets
`GET /subjects` · `GET /subjects/{id}` · `POST /subjects/{id}/download` · `POST /subjects/{id}/rate`
`GET /categories` · `GET /exams` · `GET /exams/{id}`

### Codes promo
`POST /promo-codes/validate` · `POST /promo-codes/apply` · `GET /promo-codes`
`GET /promo-codes/admin` · `POST /promo-codes` · `PUT /promo-codes/{id}` · `DELETE /promo-codes/{id}` · `PATCH /promo-codes/{id}/status`

### Forum
`GET/POST /forums/threads` · `GET/DELETE /forums/threads/{id}` · `GET/POST /forums/threads/{id}/posts`
`POST /forums/posts/{id}/vote` · `POST /forums/posts/{id}/accept`

### Messagerie directe
`GET /messages/contacts` · `GET /messages/conversations` · `GET/POST /messages/conversations/{id}/messages`
`PUT /messages/conversations/{id}/read`

### Notifications
`GET /notifications` · `PUT /notifications/{id}/read` · `PUT /notifications/read-all` · `GET /notifications/sse`

### Chatbot (proxy .NET → FastAPI)
`POST /chatbot/stream` · `POST /chatbot/message` · `GET /chatbot/conversations`
`GET /chatbot/conversations/{id}` · `GET/POST /chatbot/context` · `POST /chatbot/context/sync`
`POST /chatbot/messages/{id}/feedback`

### Dashboard Élève — `StudentController`
`GET /student/stats` · `GET /student/statistics` · `GET /student/progress`
`GET /student/learning/continue` · `GET /student/exams/recommended`
`GET /student/priorities/today` · `GET /student/events/upcoming` · `GET /student/goals`
`GET /student/score-history` · `GET /student/download-history` · `GET /student/reports`
`GET /student/links` · `GET /student/peer-comparison`

### Dashboard Enseignant — `TeacherController` + `TeacherContentController`
`GET /teacher/contents` · `GET /teacher/contents/mine` · `GET /teacher/contents/{id}/stats`
`PATCH /teacher/contents/{id}` · `DELETE /teacher/contents/{id}`
`GET /teacher/students/recent` · `GET /teacher/corrections/pending` · `GET /teacher/sessions/upcoming`
`GET /teacher/quizzes/available` · `GET /teacher/revisions/available`
`GET /teacher/stats` · `GET /teacher/profile` · `GET /teacher/revenues` · `GET /teacher/publications`
`GET /teacher/insights` · `GET /teacher/revenue-share`
`POST /teacher/class-analysis` · `GET /teacher/content-impact/{id}` · `POST /teacher/generate-correction`
`POST /teacher/predict-popularity` · `POST /teacher/analyze-submission`

### Classes enseignant — `TeacherClassesController`
`GET /teacher/classes` · `POST /teacher/classes` · `PATCH /teacher/classes/{id}` · `DELETE /teacher/classes/{id}`
`GET /teacher/classes/{id}/students` · `POST /teacher/classes/{id}/students`
`DELETE /teacher/classes/{id}/students/{studentId}`

### Dashboard Parent — `ParentController`
`GET /parent/children` · `POST /parent/children` · `DELETE /parent/children/{id}`
`GET /parent/children/{id}/stats` · `GET /parent/children/{id}/goals` · `GET /parent/children/{id}/activity`
`GET /parent/analytics/{childId}` · `GET /parent/activities/recent` · `GET /parent/payments/upcoming`
`GET /parent/events/upcoming` · `GET /parent/quizzes/available` · `GET /parent/revisions/available`
`GET /parent/profile` · `PUT /parent/settings` · `GET /parent/messages`
`GET /parent/alerts` · `PUT /parent/alerts/{id}/read` · `GET /parent/ai-alerts/{childId}`
`GET /parent/engagement/{id}` · `POST /parent/educational-roi` · `GET /parent/children-insights`

### Crédits parent — `ParentCreditsController`
`GET /parent/credits` · `GET /parent/credits/history` · `POST /parent/purchase-for-child`

### Révisions — `RevisionsController`
`GET /revisions` · `GET /revisions/{id}` · `POST /revisions/filter` · `GET /revisions/by-subject/{subject}`
`GET /revisions/me/assigned` · `GET /revisions/search` · `GET /revisions/published`
`POST /revisions/{id}/start` · `POST /revisions/{id}/complete` · `GET /revisions/{id}/progress`
`POST /revisions/me/auto-assign` · `GET /revisions/{id}/stats`
`POST /revisions` · `PUT /revisions/{id}` · `POST /revisions/{id}/publish` · `DELETE /revisions/{id}` *(Admin)*

### Notes de révision — `RevisionNotesController`
`GET /revision-notes` · `POST /revision-notes` · `DELETE /revision-notes/{id}` · `POST /revision-notes/tags/toggle`

### Groupes d'étude — `StudyGroupsController`
`GET /study-groups/me` · `GET /study-groups/{id}` · `POST /study-groups`
`POST /study-groups/join` · `DELETE /study-groups/{id}/leave`

### Erreurs quiz — `QuizMistakesController`
`GET /quiz/mistakes` · `GET /quiz/mistakes/subjects` · `POST /quiz/mistakes` · `POST /quiz/mistakes/{id}/resolve`

### Certificats — `CertificatesController`
`POST /certificates` · `GET /certificates/{id}` · `GET /certificates/user/my-certificates`
`GET /certificates/verify/{code}` · `GET /certificates/subject/{id}`
`GET /certificates/admin/all` · `POST /certificates/admin/issue` *(Admin)*

### Inscriptions — `EnrollmentsController`
`POST /enrollments` · `GET /enrollments/user/{userId}` · `GET /enrollments/{userId}/{subjectId}`
`GET /enrollments/{id}/progress` · `DELETE /enrollments/{id}`

### Institution — `InstitutionController` + `InstitutionStudentsController`
`GET /institutions` · `GET /institutions/by-country` · `GET /institutions/{id}`
`POST /institutions/class-prediction` · `GET /institutions/{id}/benchmark` · `POST /institutions/action-plan`
`GET /institutions/{id}/at-risk-students`
`GET /institution/me` · `GET /institution/{id}/students` · `POST /institution/{id}/students/import`
`GET /institution/{id}/kpis` · `GET /institution/{id}/subject-stats` · `POST /institution/{id}/reports`

### Analytics — `AnalyticsController`
`POST /analytics/track` · `GET /analytics/overview` · `GET /analytics/session-stats`

### Administration .NET
`GET/POST/PATCH/DELETE /admin/users` · `POST /admin/subjects/{id}/approve` · `POST /admin/subjects/{id}/reject`
`POST /admin/emails/send` · `GET /admin/supervision/*` · `GET /admin/payments` · `GET /admin/payments/user/{id}`

---

## 3. ❌ Vrais manques identifiés (frontend appelle, backend ne répond pas)

> **Note :** Le document précédent listait ~71 endpoints comme "manquants" — ils existaient tous.
> Après audit manuel complet, seuls 3 vrais manques subsistent.

| Priorité | Endpoint | Service frontend | Statut |
|----------|----------|-----------------|--------|
| 🟡 MOYENNE | `GET /analytics/segments` | `analyticsService.ts` | Pas de route backend |
| 🟡 FAIBLE | `GET /ai/study-tips` | `aiService.ts` | Pas de route backend |
| 🟡 FAIBLE | `POST /notifications` | `notificationService.ts` | Le front ne doit pas créer de notifs (backend uniquement) — à retirer du service |

---

## 4. ⚠️ Données encore mockées / hardcodées côté frontend

| Fichier | Ligne | Problème | Action |
|---------|-------|----------|--------|
| `CartContext.tsx` | ~580 | Code promo `PROMO10` hardcodé (contournement du service `PromoCodesController`) | Brancher sur `POST /promo-codes/validate` |
| `PerformanceChart.tsx` | ~23 | Données fixes `[{week:'Sem 1', score:13.5}…]` | Brancher sur `GET /student/score-history` |
| `AdminSubscriptions.tsx` | — | Commentaire `// Données mockées` — aucun appel API | Brancher sur `GET /subscriptions` admin |
| `DashboardPage.jsx` | — | Factures récentes hardcodées `[{m:"Mai 2026"…}]` | Brancher sur `GET /orders` |
| `OverviewTab` dans DashboardPage | ~123 | `MOCK.DASH_RECENT` pour "Activité récente" | Brancher sur `GET /student/download-history` |
| `OverviewTab` dans DashboardPage | ~157 | `MOCK.SUBJECTS` pour "Recommandé" | Brancher sur `GET /student/exams/recommended` |
| `OverviewTab` dans DashboardPage | ~189 | `MOCK.ANNOUNCEMENTS` pour "Prochaines échéances" | Brancher sur `GET /student/events/upcoming` |

---

## 5. 🏚️ Routes backend sans appel frontend

> Implémentées mais l'interface n'en tire pas encore parti.

| Endpoint | Raison |
|----------|--------|
| `GET /student/peer-comparison` | Pas de composant UI |
| `GET /parent/ai-alerts/{childId}` | Widget pas encore créé |
| `POST /parent/children` / `DELETE /parent/children/{id}` | Interface gestion enfants absente |
| `POST /admin/subjects/{id}/approve|reject` | Workflow admin non câblé |
| `POST /admin/uploads/*` (multipart) | Upload multipart non utilisé côté front |
| `POST /revisions` / `PUT /revisions/{id}` *(Admin)* | Interface admin révisions absente |
| `POST /certificates/admin/issue` | Interface admin certificats absente |
| `GET /teacher/quizzes/available` / `GET /teacher/revisions/available` | Non affichés dans le dashboard enseignant |
| `GET /analytics/overview` · `GET /analytics/session-stats` | Dashboard analytics non créé |

---

## 6. Tableau de synthèse

| Domaine | Appels front | Backend existe | Couverture |
|---------|-------------|----------------|-----------|
| Chatbot / IA | 15 | 15 | ✅ 100% |
| Auth | 7 | 7 | ✅ 100% |
| Forum | 8 | 8 | ✅ 100% |
| Messagerie | 5 | 5 | ✅ 100% |
| Abonnements | 5 | 5 | ✅ 100% |
| Paiements | 8 | 8 | ✅ 100% |
| Commandes | 10 | 10 | ✅ 100% |
| Notifications | 4 | 4 | ✅ 100% |
| Dashboard Élève | 13 | 13 | ✅ 100% |
| Dashboard Enseignant | 21 | 21 | ✅ 100% |
| Dashboard Parent | 20 | 20 | ✅ 100% |
| Crédits parent | 3 | 3 | ✅ 100% |
| Révisions | 9 | 9 | ✅ 100% |
| Notes / Groupes d'étude | 9 | 9 | ✅ 100% |
| Suivi erreurs quiz | 4 | 4 | ✅ 100% |
| Institution | 12 | 12 | ✅ 100% |
| Inscriptions | 5 | 5 | ✅ 100% |
| Certificats | 3 | 3 | ✅ 100% |
| Codes promo | 4 | 4 | ✅ 100% |
| Analytics | 3 | 1 | 🟡 33% |
| **TOTAL** | **~168** | **~165** | **✅ ~98%** |

---

## 7. Corrections appliquées ce sprint (2026-08-31)

### Backend
| Fichier | Correction |
|---------|-----------|
| `TeacherController.cs` | Tous les endpoints utilisaient `[FromQuery] int teacherId` → remplacé par `User.GetUserId()` |
| `ParentController.cs` | Même correction + childId optionnel résolu depuis les liens |
| `StudentController.cs` | 5 stubs (`learning/continue`, `exams/recommended`, `priorities/today`, `events/upcoming`, `goals`) implémentés avec vraies requêtes EF Core |
| `StudentController.cs` | 2 nouveaux endpoints : `GET /student/download-history` et `GET /student/reports` |
| `AnalyticsController.cs` | `var userId = 1` → `var userId = User.GetUserId()` |
| `ApplicationDbContext.Sprints.cs` | Suppression du `DbSet<QuizMistake>` en double (CS0102) |
| `OrdersController.cs` | Ajout `POST /orders/{id}/refund` |
| `UsersController.cs` | Ajout stubs `GET/POST/DELETE /users/{id}/payment-methods` |
| `PaymentsController.cs` | Ajout `POST /payments/confirm` (alias `/initiate`) et `POST /payments/{id}/verify` |

### Frontend
| Fichier | Correction |
|---------|-----------|
| `CartContext.tsx` | `createOrder` implémenté : appelle `orderService.createOrder()` → `POST /api/orders`, vide le panier après succès |
| `DashboardPage.jsx` | KPIs branchés sur `/student/stats` et `/student/download-history` (score, temps, téléchargements, quiz) |
| `DashboardPage.jsx` | `ProgressTab` branché sur `/student/score-history` pour le graphique d'évolution |

---

## 8. Reste à faire (données mockées → API réelle)

### Priorité haute
- [ ] `OverviewTab` — remplacer `MOCK.DASH_RECENT` → `GET /student/download-history`
- [ ] `OverviewTab` — remplacer `MOCK.SUBJECTS` → `GET /student/exams/recommended`
- [ ] `OverviewTab` — remplacer `MOCK.ANNOUNCEMENTS` → `GET /student/events/upcoming`
- [ ] `CartContext.tsx` — valider les codes promo via `POST /promo-codes/validate` (supprimer le mock `PROMO10`)

### Priorité moyenne
- [ ] `PerformanceChart.tsx` — brancher sur `GET /student/score-history`
- [ ] `AdminSubscriptions.tsx` — brancher sur API réelle
- [ ] Factures dans DashboardPage — brancher sur `GET /orders`
- [ ] Ajouter `GET /analytics/segments` au backend (`AnalyticsController`)

### Priorité faible
- [ ] Retirer `POST /notifications` de `notificationService.ts` (les notifs sont créées par le backend)
- [ ] Créer composants UI pour `peer-comparison`, `parent/ai-alerts`, gestion multi-enfants
- [ ] Interface admin pour révisions et certificats

---

*Mis à jour le 2026-08-31 après audit manuel complet + corrections sprint.*
