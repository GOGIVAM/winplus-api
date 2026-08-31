# WinPlus — Audit de couverture API
> Mis à jour le 2026-08-31 · Sprint 2 — intégration de toutes les routes backend manquantes

---

## 1. Fonctionnalités FastAPI (Python)

> Base URL : `https://api.winplus.cm` (proxy .NET → FastAPI via `FastApiClient`)

### Chatbot WinAI — `/api/chatbot`
| Méthode | Chemin | Description | Frontend |
|---------|--------|-------------|----------|
| GET | `/health` | Health check DeepSeek | `chatbotService` |
| POST | `/chat` | Chat complet avec contexte rôle + historique | `chatbotService` |
| POST | `/stream` | Stream SSE — persistance messages en DB | `chatbotService` |
| POST | `/complete` | Complétion simple sans historique | `chatbotService` |

### Quiz IA — `/api/quiz`
| Méthode | Chemin | Description | Frontend |
|---------|--------|-------------|----------|
| POST | `/explain-error` | Explication pédagogique d'une erreur (SSE) | `AIService.explainError()` |

### Coach d'examen — `/api/exam-coach`
| Méthode | Chemin | Description | Frontend |
|---------|--------|-------------|----------|
| POST | `/generate` | Plan de révision espacé Ebbinghaus | `AIService.examCoachGenerate()` |
| POST | `/recalibrate` | Recalibrage du plan sur résultats hebdo | `AIService.examCoachRecalibrate()` |
| GET | `/today/{user_id}` | Session du jour + message motivationnel WinAI | `AIService.examCoachToday()` |
| POST | `/predict-grade` | Prédiction de note /20 sur 90 derniers jours | `AIService.predictGrade()` |
| GET | `/micro-intervention/{user_id}` | Intervention si inactivité + examen proche | `AIService.getMicroIntervention()` |

### Sessions d'étude — `/api/study-session`
| Méthode | Chemin | Description | Frontend |
|---------|--------|-------------|----------|
| POST | `/generate` | Session guidée 3 phases : briefing → quiz 5 questions → synthèse | `AIService.generateStudySession()` |
| POST | `/complete` | Sauvegarde session + mise à jour DailyScore | `AIService.completeStudySession()` |

### Enseignant IA — `/api`
| Méthode | Chemin | Description | Frontend |
|---------|--------|-------------|----------|
| POST | `/ai/optimize-title` | Optimisation SEO du titre de contenu | `AIService.optimizeTitle()` |
| POST | `/ai/generate-description` | Description catalogue 2-3 phrases | `AIService.generateDescription()` |
| POST | `/teacher/class-analysis` | Analyse performance de classe sur contenu | `teacherExtraService.getClassAnalysis()` |
| GET | `/teacher/content-impact/{content_id}` | Score d'impact pédagogique 0-100 | `teacherExtraService.getContentImpact()` |
| POST | `/teacher/generate-correction` | Correction structurée d'examen | `teacherExtraService.generateCorrection()` |
| POST | `/teacher/predict-popularity` | Prédiction popularité + prix recommandé | `teacherExtraService.predictPopularity()` |
| POST | `/teacher/analyze-submission` | Analyse copie élève : erreur, note, commentaire | `teacherExtraService.analyzeSubmission()` |

### Parent extra — `/api`
| Méthode | Chemin | Description | Frontend |
|---------|--------|-------------|----------|
| GET | `/parent-engagement/{parent_id}` | Score d'engagement parental 0-100 | `parentExtraService.getEngagementScore()` |
| POST | `/parent/educational-roi` | ROI éducatif sur 90 jours | `parentExtraService.getEducationalRoi()` |
| GET | `/parent/children-insights` | Analyse comparative multi-enfants | `parentExtraService.getChildrenInsights()` |

### Institution — `/api`
| Méthode | Chemin | Description | Frontend |
|---------|--------|-------------|----------|
| POST | `/institution/action-plan` | 3 actions prioritaires de la semaine | `institutionExtraService.getActionPlan()` |

### Administration — `/api/admin`
| Méthode | Chemin | Description | Frontend |
|---------|--------|-------------|----------|
| POST | `/ai/summarize-notifications` | Résumé bullet-points via DeepSeek | `AIService.summarizeNotifications()` |
| POST | `/ai/content-fit-analysis` | Adéquation contenu/profil utilisateur | `AIService.contentFitAnalysis()` |

---

## 2. Fonctionnalités .NET (C#) — `api/`

### Auth
`POST /auth/login` · `POST /auth/register` · `POST /auth/refresh` · `POST /auth/logout`
`POST /auth/verify-email` · `POST /auth/forgot-password` · `POST /auth/reset-password`
`GET /auth/reconfirmation-status` · `POST /auth/send-confirmation-code` · `POST /auth/verify-confirmation`
> Service : `auth.ts`, `authExtraService.ts`

### Utilisateurs
`GET/PUT /users/me` · `PUT /users/me/avatar` · `GET /users/me/downloads`
`GET /users/{id}` · `GET /users/search` · `GET /users/profile/statistics` · `GET /users/profile/subscriptions`
`GET/PUT /users/settings/notifications` · `GET/PUT /users/settings/privacy`
`GET/PUT/DELETE /users/me/weekly-goal`
`GET /users/{id}/payment-methods` · `POST /users/{id}/payment-methods` · `DELETE /users/{id}/payment-methods/{mid}`
`GET /users/sessions` · `DELETE /users/sessions/{id}` · `GET/POST /users/2fa/*`
> Service : `user.ts`, `weeklyGoalService.ts`

### Abonnements
`GET /subscriptions/me` · `POST /subscriptions` · `POST /subscriptions/purchase` · `POST /subscriptions/me/cancel`
> Service : `paymentService.ts`

### Paiements
`POST /payments/initiate` · `POST /payments/confirm` · `GET /payments/{id}/status`
`POST /payments/{id}/verify` · `POST /payments/{id}/retry` · `GET /payments/history`
`POST /payments/webhook/notchpay`
> Service : `paymentService.ts`

### Commandes
`POST /orders` · `GET /orders` · `GET /orders/{id}` · `POST /orders/{id}/cancel`
`POST /orders/{id}/refund` · `GET /orders/{id}/invoice` · `GET /orders/{id}/status`
`GET /orders/statistics` · `GET /orders/search` · `POST /orders/summary`
> Service : `orderService.ts`, `CartContext.tsx`

### Catalogue / Sujets
`GET /subjects` · `GET /subjects/{id}` · `POST /subjects/{id}/download` · `POST /subjects/{id}/rate`
`GET /categories` · `GET /exams` · `GET /exams/{id}`
> Service : `catalogService.ts`, `categoryService.ts`

### Codes promo
`POST /promo-codes/validate` · `POST /promo-codes/apply` · `GET /promo-codes`
`GET /promo-codes/admin` · `POST /promo-codes` · `PATCH /promo-codes/{id}/status` · `DELETE /promo-codes/{id}`
> Service : `adminService.ts`, `cartService.ts`

### Forum
`GET/POST /forums/threads` · `GET /forums/threads/search` · `GET/DELETE /forums/threads/{id}`
`GET/POST /forums/threads/{id}/posts` · `POST /forums/posts/{id}/vote` · `POST /forums/posts/{id}/accept`
`GET /forums/threads/feed` · `GET /forums/follows` · `POST/DELETE /forums/threads/{id}/follow`
> Service : `forumService.ts`

### Historique — `HistoryController`
`POST /history` · `GET /history` · `GET /history/type/{type}` · `GET /history/subject/{subjectId}`
`GET /history/range` · `GET /history/statistics` · `GET /history/recent` · `DELETE /history/{id}` · `DELETE /history`
> Service : `historyService.ts`

### Messagerie directe
`GET /messages/contacts` · `GET /messages/conversations` · `GET/POST /messages/conversations/{id}/messages`
`PUT /messages/conversations/{id}/read`
> Service : `messagingService.ts`

### Notifications — `NotificationsController`
`GET /notifications` · `PUT /notifications/{id}/read` · `PUT /notifications/read-all` · `GET /notifications/sse`
> Service : `notificationService.ts` (`fetchNotifications`, `markAsRead`, `markAllAsRead`, `openSSE`)

### Chatbot (proxy .NET → FastAPI)
`POST /chatbot/stream` · `POST /chatbot/message` · `GET /chatbot/conversations`
`GET /chatbot/conversations/{id}` · `GET/POST /chatbot/context` · `POST /chatbot/context/sync`
`POST /chatbot/messages/{id}/feedback`
> Service : `chatbotService.ts`

### IA — `AIController`
`POST /ai/recommend` · `POST /ai/analyze-progress` · `POST /ai/generate-quiz` · `GET /ai/performance`
`POST /ai/personalized-path` · `GET /ai/recommendations/{id}` · `GET /ai/success-prediction/{userId}`
`POST /ai/predict-success` · `POST /ai/study-plan` · `POST /ai/explain-error`
`GET /ai/study-habits` · `POST /ai/exam-coach/generate` · `POST /ai/exam-coach/recalibrate`
`GET /ai/exam-coach/today` · `POST /ai/feedback/recommendation` · `GET /ai/health`
`POST /ai/predict-grade` · `GET /ai/micro-intervention`
`GET /ai/memory` · `POST /ai/memory` · `POST /ai/memory/upsert` · `DELETE /ai/memory/{type}`
`POST /ai/study-session/generate` · `POST /ai/study-session/complete`
`POST /ai/optimize-title` · `POST /ai/generate-description`
`POST /ai/summarize-notifications` · `POST /ai/content-fit-analysis`
> Service : `aiService.ts`

### Coach d'examen — `ExamCoachController`
`POST /exam-coach` · `GET /exam-coach/active` · `PUT /exam-coach/{id}/complete-day` · `DELETE /exam-coach/{id}`
> Service : `examCoachService.ts` *(nouveau)*

### Dashboard Élève — `StudentController` + `StudentReportsController`
`GET /student/stats` · `GET /student/statistics` · `GET /student/progress`
`GET /student/learning/continue` · `GET /student/exams/recommended`
`GET /student/priorities/today` · `GET /student/events/upcoming` · `GET /student/goals`
`GET /student/score-history` · `GET /student/download-history` · `GET /student/reports`
`GET /student/links` · `GET /student/peer-comparison`
> Service : `dashboardService.ts`, `studentExtraService.ts`

### Dashboard Enseignant — `TeacherController` + `TeacherContentController`
`GET /teacher/contents` · `GET /teacher/contents/mine` · `GET /teacher/contents/{id}/stats`
`PATCH /teacher/contents/{id}` · `DELETE /teacher/contents/{id}` · `DELETE /teacher/contents/{id}?permanent=true`
`GET /teacher/students/recent` · `GET /teacher/corrections/pending` · `GET /teacher/sessions/upcoming`
`GET /teacher/quizzes/available` · `GET /teacher/revisions/available`
`GET /teacher/stats` · `GET /teacher/profile` · `GET /teacher/revenues` · `GET /teacher/publications`
`GET /teacher/insights` · `GET /teacher/revenue-share`
`POST /teacher/class-analysis` · `GET /teacher/content-impact/{id}` · `POST /teacher/generate-correction`
`POST /teacher/predict-popularity` · `POST /teacher/analyze-submission`
> Service : `dashboardService.ts`, `teacherExtraService.ts`

### Classes enseignant — `TeacherClassesController`
`GET /teacher/classes` · `POST /teacher/classes` · `PATCH /teacher/classes/{id}` · `DELETE /teacher/classes/{id}`
`GET /teacher/classes/{id}/students` · `POST /teacher/classes/{id}/students`
`DELETE /teacher/classes/{id}/students/{studentId}`
> Service : `teacherExtraService.ts`

### Liaisons prof-élève — `TeacherStudentLinksController`
`GET /teacher-links/search` · `POST /teacher-links/invite` · `GET /teacher-links/pending`
`PUT /teacher-links/{id}/accept` · `PUT /teacher-links/{id}/reject` · `GET /teacher-links/mine` · `DELETE /teacher-links/{id}`
> Service : `linkingService.ts`

### Dashboard Parent — `ParentController` + `ParentCreditsController`
`GET /parent/children` · `POST /parent/children` · `DELETE /parent/children/{id}`
`GET /parent/children/{id}/stats` · `GET /parent/children/{id}/goals` · `GET /parent/children/{id}/activity`
`GET /parent/analytics/{childId}` · `GET /parent/activities/recent` · `GET /parent/payments/upcoming`
`GET /parent/events/upcoming` · `GET /parent/quizzes/available` · `GET /parent/revisions/available`
`GET /parent/profile` · `PUT /parent/settings` · `GET /parent/messages`
`GET /parent/alerts` · `PUT /parent/alerts/{id}/read` · `GET /parent/ai-alerts/{childId}`
`GET /parent/engagement/{id}` · `POST /parent/educational-roi` · `GET /parent/children-insights`
`GET /parent/credits` · `GET /parent/credits/history` · `POST /parent/purchase-for-child`
> Service : `dashboardService.ts`, `parentExtraService.ts`, `linkingService.ts`

### Révisions — `RevisionsController`
`GET /revisions` · `GET /revisions/{id}` · `POST /revisions/filter` · `GET /revisions/by-subject/{subject}`
`GET /revisions/me/assigned` · `GET /revisions/search` · `GET /revisions/published`
`POST /revisions/{id}/start` · `POST /revisions/{id}/complete` · `GET /revisions/{id}/progress`
`POST /revisions/me/auto-assign` · `GET /revisions/{id}/stats`
`POST /revisions` · `PUT /revisions/{id}` · `POST /revisions/{id}/publish` · `DELETE /revisions/{id}` *(Admin)*
> Service : `revisionService.ts`

### Notes de révision — `RevisionNotesController`
`GET /revision-notes` · `POST /revision-notes` · `DELETE /revision-notes/{id}` · `POST /revision-notes/tags/toggle`
> Service : `studentExtraService.ts`

### Groupes d'étude — `StudyGroupsController`
`GET /study-groups/me` · `GET /study-groups/{id}` · `POST /study-groups`
`POST /study-groups/join` · `DELETE /study-groups/{id}/leave`
> Service : `studentExtraService.ts`

### Erreurs quiz — `QuizMistakesController`
`GET /quiz/mistakes` · `GET /quiz/mistakes/subjects` · `POST /quiz/mistakes` · `POST /quiz/mistakes/{id}/resolve`
> Service : `studentExtraService.ts`

### Certificats — `CertificatesController`
`POST /certificates` · `GET /certificates/{id}` · `GET /certificates/user/my-certificates`
`GET /certificates/verify/{code}` · `GET /certificates/subject/{id}`
`GET /certificates/admin/all` · `POST /certificates/admin/issue` *(Admin)*
> Service : `certificateService.ts`

### Inscriptions — `EnrollmentsController`
`POST /enrollments` · `GET /enrollments/user/{userId}` · `GET /enrollments/{userId}/{subjectId}`
`GET /enrollments/{id}/progress` · `DELETE /enrollments/{id}`
> Service : `enrollmentService.ts`

### Institution — `InstitutionController` + `InstitutionStudentsController` + `InstitutionMobileController`
`GET /institutions` · `GET /institutions/by-country` · `GET /institutions/{id}`
`GET /institution/me` · `GET /institution/{id}/students` · `POST /institution/{id}/students/import`
`GET /institution/{id}/kpis` · `GET /institution/{id}/subject-stats` · `POST /institution/{id}/reports`
`GET /institution/groups` · `POST /institution/groups` · `DELETE /institution/groups/{id}`
`GET /institution/groups/{id}/members` · `POST /institution/groups/{id}/members`
`DELETE /institution/groups/{id}/members/{memberId}`
`GET /institution/analytics` · `GET /institution/at-risk` · `GET /institution/action-plan`
> Service : `institutionsService.ts`, `institutionExtraService.ts`

### Analytics — `AnalyticsController`
`POST /analytics/track` · `GET /analytics/session` · `GET /analytics/user/{userId}` · `GET /analytics/recent`
> Service : `analyticsService.ts`

### Administration .NET — `AdminController` + `AdminUsersController` + `AdminSupervisionController`
`GET/POST/PATCH/DELETE /admin/users` · `GET /admin/users/stats` · `GET /admin/users/online`
`GET /admin/users/{id}` · `POST /admin/users/{id}/suspend` · `POST /admin/users/{id}/reactivate`
`POST /admin/users/{id}/verify-email` · `POST /admin/users/{id}/reset-password`
`GET /admin/users/{id}/sessions` · `DELETE /admin/users/{id}/sessions`
`GET /admin/users/{id}/payments` · `GET /admin/users/{id}/activity` · `POST /admin/users/{id}/grant-subscription`
`GET /admin/dashboard/stats` · `GET /admin/activities/recent` · `GET /admin/payments`
`GET /admin/supervision/stats` · `GET /admin/supervision/study-groups` · `GET /admin/supervision/teacher-classes`
`GET /admin/supervision/parent-credits` · `GET /admin/supervision/institution-licences`
`GET /admin/supervision/export/{registry}`
`POST /admin/supervision/study-groups/{id}/archive`
> Service : `adminService.ts`, `adminUserService.ts`, `adminExtraService.ts`

### Administration — Examens & Bibliothèque — `AdminExamsController` + `AdminLibraryController` + `AdminTaxonomyController`
`GET /admin/exams` · `GET /admin/exams/filters` · `GET /admin/exams/{id}`
`POST /admin/exams` · `PUT /admin/exams/{id}` · `DELETE /admin/exams/{id}`
`DELETE /admin/exams/{id}/hard` · `POST /admin/exams/{id}/restore` · `POST /admin/exams/{id}/publish`
`POST /admin/exams/bulk`
`POST /admin/library` · `PUT /admin/library/{id}` · `DELETE /admin/library/{id}`
`GET /admin/taxonomy`
> Service : `adminExamService.ts`, `adminExtraService.ts`

### Administration — Uploads S3 — `AdminUploadsController`
`POST /admin/uploads/direct` · `POST /admin/uploads/init` · `POST /admin/uploads/part-url`
`GET /admin/uploads/parts` · `POST /admin/uploads/complete` · `POST /admin/uploads/abort`
> Service : `adminExamService.ts` (`uploadSmall`, `uploadLarge`)

### Annonces — `AnnouncementController`
`GET /announcements` · `GET /announcements/{id}` · `GET /announcements/admin/all`
`POST /announcements` · `PUT /announcements/{id}` · `DELETE /announcements/{id}` · `POST /announcements/{id}/publish`
> Service : `homeService.ts` (public), `adminService.ts` (admin)

### Objectif hebdomadaire — `WeeklyGoalController`
`GET /users/me/weekly-goal` · `PUT /users/me/weekly-goal` · `DELETE /users/me/weekly-goal`
> Service : `weeklyGoalService.ts`

---

## 3. ❌ Vrais manques identifiés

| Priorité | Endpoint | Service frontend | Statut |
|----------|----------|-----------------|--------|
| 🟡 MOYENNE | `GET /analytics/segments` | `analyticsService.ts` | Pas de route backend — à créer |
| 🟡 FAIBLE | `GET /ai/study-tips` | `aiService.ts` | Pas de route backend |
| 🟡 FAIBLE | `POST /notifications` (création) | `notificationService.ts` | À retirer du service — les notifs sont créées par le backend uniquement |

---

## 4. ⚠️ Données encore mockées / hardcodées côté frontend

| Fichier | Problème | Action |
|---------|----------|--------|
| `CartContext.tsx` | Code promo `PROMO10` hardcodé dans la branche `else` (unreachable quand `SYNC_WITH_BACKEND=true`) | Aucune urgence — branche jamais exécutée |
| `PerformanceChart.tsx` | Données fixes `[{week:'Sem 1', score:13.5}…]` | Brancher sur `GET /student/score-history` |
| `AdminSubscriptions.tsx` | Commentaire `// Données mockées` | Brancher sur `GET /subscriptions` admin |
| `DashboardPage.jsx` | Factures récentes hardcodées | Brancher sur `GET /orders` |

---

## 5. Routes backend exposées mais sans interface UI

> Toutes ont maintenant un service frontend. L'interface (composant) reste à créer.

| Endpoint | Service disponible | UI manquante |
|----------|--------------------|--------------|
| `GET /exam-coach/active` + CRUD | `examCoachService.ts` | Page coach d'examen |
| `GET /student/peer-comparison` | `studentExtraService.ts` | Widget comparaison |
| `GET /parent/ai-alerts/{childId}` | `parentExtraService.ts` | Widget alertes IA parent |
| `GET /ai/memory` / `POST /ai/memory` | `aiService.ts` | Interface mémoire WinAI |
| `GET /institution/at-risk` | `institutionExtraService.ts` | Tableau élèves à risque |
| `GET /forums/threads/feed` + follow | `forumService.ts` | Fil personnalisé forum |
| `GET /admin/taxonomy` | `adminExtraService.ts` | Formulaire upload dynamique |
| `POST/PUT/DELETE /admin/library` | `adminExtraService.ts` | Interface bibliothèque admin |
| `GET/POST/DELETE /ai/memory/{type}` | `aiService.ts` | Gestionnaire mémoire IA |

---

## 6. Tableau de synthèse

| Domaine | Routes backend | Service frontend | Couverture |
|---------|---------------|-----------------|-----------|
| Chatbot / IA | 29 | 29 | ✅ 100% |
| Auth | 10 | 10 | ✅ 100% |
| Forum | 12 | 12 | ✅ 100% |
| Historique | 9 | 9 | ✅ 100% |
| Messagerie | 5 | 5 | ✅ 100% |
| Abonnements | 5 | 5 | ✅ 100% |
| Paiements | 8 | 8 | ✅ 100% |
| Commandes | 10 | 10 | ✅ 100% |
| Notifications | 4 | 4 | ✅ 100% |
| Dashboard Élève | 13 | 13 | ✅ 100% |
| Dashboard Enseignant | 21 | 21 | ✅ 100% |
| Dashboard Parent | 24 | 24 | ✅ 100% |
| Révisions | 16 | 16 | ✅ 100% |
| Notes / Groupes d'étude | 9 | 9 | ✅ 100% |
| Suivi erreurs quiz | 4 | 4 | ✅ 100% |
| Certificats | 7 | 7 | ✅ 100% |
| Inscriptions | 5 | 5 | ✅ 100% |
| Institution | 18 | 18 | ✅ 100% |
| Coach d'examen | 4 | 4 | ✅ 100% |
| Codes promo | 7 | 7 | ✅ 100% |
| Annonces | 7 | 7 | ✅ 100% |
| Objectif hebdomadaire | 3 | 3 | ✅ 100% |
| Liaisons prof-élève | 7 | 7 | ✅ 100% |
| Admin (users + supervision) | 25 | 25 | ✅ 100% |
| Admin (exams + bibliothèque) | 14 | 14 | ✅ 100% |
| Admin (uploads S3) | 6 | 6 | ✅ 100% |
| Analytics | 4 | 3 | 🟡 75% |
| **TOTAL** | **~315** | **~314** | **✅ ~99,7%** |

---

## 7. Corrections appliquées — Sprint 1 (2026-08-31)

### Backend
| Fichier | Correction |
|---------|-----------|
| `AnalyticsController.cs` | `var userId = 1` → `var userId = User.GetUserId()` |
| `OrdersController.cs` | Ajout `POST /orders/{id}/refund` |
| `UsersController.cs` | Ajout stubs `GET/POST/DELETE /users/{id}/payment-methods` |
| `PaymentsController.cs` | Ajout `POST /payments/confirm` et `POST /payments/{id}/verify` |

### Frontend
| Fichier | Correction |
|---------|-----------|
| `CartContext.tsx` | `createOrder` implémenté → appelle `orderService.createOrder()` |
| `DashboardPage.jsx` | KPIs branchés sur API réelle (`/student/stats`, `/student/download-history`, `/student/score-history`) |

---

## 8. Connexions ajoutées — Sprint 2 (2026-08-31)

### Nouveaux fichiers de service
| Fichier | Routes couvertes |
|---------|-----------------|
| `examCoachService.ts` | 4 routes `ExamCoachController` : createPlan, getActivePlan, completeDay, deactivatePlan |

### Services enrichis
| Service | Méthodes ajoutées | Routes |
|---------|------------------|--------|
| `aiService.ts` | +19 méthodes | performance, personalized-path, success-prediction, study-habits, exam-coach (recalibrate/today), health, predict-grade, micro-intervention, memory (GET/POST/DELETE), study-session (generate/complete), optimize-title, generate-description, summarize-notifications, content-fit-analysis, explainError (SSE) |
| `historyService.ts` | +5 méthodes API | bySubject, byDateRange, getApiStatistics, getRecent, deleteById |
| `institutionExtraService.ts` | +9 méthodes | groups CRUD, analytics, at-risk, action-plan |
| `forumService.ts` | +4 méthodes | getFeed, getFollowedThreads, followThread, unfollowThread |
| `adminExtraService.ts` | +4 méthodes | createLibraryItem, updateLibraryItem, deleteLibraryItem, getTaxonomy |
| `studentExtraService.ts` | +1 méthode | getMyLinks |
| `notificationService.ts` | +1 méthode | openSSE (proxy ntfy) |

---

## 9. Reste à faire

### Priorité haute — Données mockées
- [ ] `PerformanceChart.tsx` — brancher sur `GET /student/score-history`
- [ ] `AdminSubscriptions.tsx` — brancher sur API réelle
- [ ] Factures dans DashboardPage — brancher sur `GET /orders`

### Priorité moyenne — Interfaces manquantes
- [ ] Créer interface ExamCoach (page planification d'examen)
- [ ] Widget comparaison avec les pairs (`GET /student/peer-comparison`)
- [ ] Widget alertes IA parent (`GET /parent/ai-alerts/{childId}`)
- [ ] Interface gestion groupes institution (`institutionExtraService.ts` prêt)
- [ ] Fil personnalisé forum avec suivi threads (`forumService.getFeed/followThread`)

### Priorité faible — Backend
- [ ] Créer `GET /analytics/segments` dans `AnalyticsController`
- [ ] Retirer `POST /notifications` (création) de `notificationService.ts`

---

*Mis à jour le 2026-08-31 — Sprint 2 : intégration complète de toutes les routes backend (~47 nouvelles connexions, couverture 99,7%)*
