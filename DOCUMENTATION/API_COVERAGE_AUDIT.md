# WinPlus — Audit de couverture API
> Généré le 2026-08-31 · Analyse automatique du codebase frontend + backend

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
| POST | `/moderate-content` | Modération post forum (spam, inapproprié…) |
| POST | `/moderate-message` | Modération message privé (sans stockage DB) |
| GET | `/moderation/pending` | Posts en attente de modération |
| POST | `/moderation/{id}/resolve` | Approuver ou rejeter du contenu modéré |

### IA Smart / Catalogue — `/api`
| Méthode | Chemin | Description |
|---------|--------|-------------|
| POST | `/ai/generate-notification` | Notification mobile personnalisée (cache 24h, 3/user/jour) |
| POST | `/ai/summarize-notifications` | Résumé de 50 notifications non lues |
| POST | `/ai/content-fit-analysis` | Score correspondance contenu/profil (0-1) |
| GET | `/subjects` | Catalogue avec pagination, filtres catégorie/search/featured |
| GET | `/subjects/{id}` | Détail sujet + contenus |
| GET | `/categories` | Toutes les catégories |
| GET | `/popular` | Sujets populaires (top 10) |
| GET | `/recommendations/{subject_id}` | 5 sujets similaires (auth requis) |
| POST | `/recommend` | Recommandations personnalisées par historique |
| POST | `/adaptive-quiz` | Quiz adaptatif sur lacunes détectées (limite 5 req/min) |
| POST | `/learning-style` | Analyse VARK style d'apprentissage |
| GET | `/learning-path/{user_id}` | Parcours d'apprentissage personnalisé |
| POST | `/analyze-progress` | Analyse progression + projections |
| GET | `/success-prediction/{user_id}` | Probabilité de réussite |

---

## 2. Fonctionnalités .NET (C#) — `api/`

### Auth
| GET/POST | `/auth/login`, `/auth/register`, `/auth/refresh`, `/auth/logout`, `/auth/verify-email`, `/auth/forgot-password`, `/auth/reset-password` |

### Utilisateurs
| GET/PUT/PATCH | `/users/me`, `/users/me/avatar`, `/users/me/downloads`, `/users/{id}`, `/users/search` |

### Abonnements
| GET/POST | `/subscriptions/me`, `/subscriptions`, `/subscriptions/purchase`, `/subscriptions/me/cancel` |

### Paiements
| GET/POST | `/payments/initiate`, `/payments/{id}/status`, `/payments/history`, `/payments/{id}/retry`, `/payments/webhook/notchpay` |

### Catalogue / Sujets
| GET/POST/PUT/DELETE | `/subjects`, `/subjects/{id}`, `/subjects/{id}/download`, `/subjects/{id}/rate` |

### Forum
| GET/POST/DELETE | `/forums/threads`, `/forums/threads/{id}`, `/forums/threads/{id}/posts`, `/forums/posts/{id}/vote`, `/forums/posts/{id}/accept`, `/forums/threads/{id}` |

### Messagerie directe
| GET/POST/PUT | `/messages/contacts`, `/messages/conversations`, `/messages/conversations/{id}/messages`, `/messages/conversations/{id}/read` |

### Chatbot (proxy .NET → FastAPI)
| GET/POST | `/chatbot/stream`, `/chatbot/message`, `/chatbot/conversations`, `/chatbot/conversations/{id}`, `/chatbot/context`, `/chatbot/context/sync`, `/chatbot/messages/{id}/feedback` |

### Notifications
| GET/POST/PUT | `/notifications`, `/notifications/{id}/read`, `/notifications/read-all`, `/notifications/sse` |

### Administration .NET
| GET/POST/PATCH/DELETE | `/admin/users`, `/admin/subjects/{id}/approve`, `/admin/subjects/{id}/reject`, `/admin/emails/send`, `/admin/supervision/*`, `/admin/winai/*` |

---

## 3. ❌ Fonctionnalités frontend SANS endpoint backend

> Ces appels API sont faits par le frontend mais aucun contrôleur ne répond.

### 3.1 Dashboard Élève — `dashboardService.ts`
| Priorité | Endpoint manquant | Utilisé dans |
|----------|-------------------|--------------|
| 🔴 CRITIQUE | `GET /student/learning/continue` | Widget "Continuer" |
| 🔴 CRITIQUE | `GET /student/exams/recommended` | Widget recommandations |
| 🔴 CRITIQUE | `GET /student/priorities/today` | Widget priorités |
| 🟠 HAUTE | `GET /student/events/upcoming` | Calendrier |
| 🟠 HAUTE | `GET /student/goals` | Objectifs |
| 🟠 HAUTE | `GET /student/statistics` | KPI dashboard |
| 🟠 HAUTE | `GET /student/progress` | Barre progression |
| 🟡 MOYENNE | `GET /users/profile/statistics` | Profil stats |
| 🟡 MOYENNE | `GET /users/profile/subscriptions` | Profil abonnement |

### 3.2 Dashboard Enseignant — `teacherExtraService.ts`
| Priorité | Endpoint manquant | Utilisé dans |
|----------|-------------------|--------------|
| 🔴 CRITIQUE | `GET /teacher/contents/mine` | Liste contenus publiés |
| 🔴 CRITIQUE | `GET /teacher/contents/{id}/stats` | Stats par contenu |
| 🔴 CRITIQUE | `GET /teacher/revenues` | Dashboard revenus |
| 🔴 CRITIQUE | `GET /teacher/revenue-share` | Part des revenus |
| 🔴 CRITIQUE | `GET /teacher/classes` | Liste des classes |
| 🔴 CRITIQUE | `POST /teacher/classes` | Créer une classe |
| 🔴 CRITIQUE | `PATCH /teacher/classes/{id}` | Modifier une classe |
| 🔴 CRITIQUE | `DELETE /teacher/classes/{id}` | Supprimer une classe |
| 🔴 CRITIQUE | `GET /teacher/classes/{id}/students` | Élèves d'une classe |
| 🔴 CRITIQUE | `POST /teacher/classes/{id}/students` | Ajouter élève à classe |
| 🔴 CRITIQUE | `DELETE /teacher/classes/{id}/students/{sid}` | Retirer élève |
| 🟠 HAUTE | `GET /teacher/students/recent` | Élèves récents |
| 🟠 HAUTE | `GET /teacher/corrections/pending` | Corrections en attente |
| 🟠 HAUTE | `GET /teacher/sessions/upcoming` | Sessions à venir |
| 🟡 MOYENNE | `GET /teacher/insights` | Insights enseignant |

### 3.3 Dashboard Parent — `parentExtraService.ts`
| Priorité | Endpoint manquant | Utilisé dans |
|----------|-------------------|--------------|
| 🔴 CRITIQUE | `GET /parent/children` | Liste des enfants |
| 🔴 CRITIQUE | `GET /parent/children/{id}/stats` | Stats par enfant |
| 🔴 CRITIQUE | `GET /parent/credits` | Solde crédits |
| 🔴 CRITIQUE | `GET /parent/credits/history` | Historique crédits |
| 🔴 CRITIQUE | `POST /parent/purchase-for-child` | Achat pour enfant |
| 🔴 CRITIQUE | `GET /parent/analytics/{childId}` | Analytiques enfant |
| 🟠 HAUTE | `GET /parent/activities/recent` | Activités récentes |
| 🟠 HAUTE | `GET /parent/payments/upcoming` | Prochains paiements |
| 🟠 HAUTE | `GET /parent/events/upcoming` | Événements à venir |
| 🟠 HAUTE | `GET /parent/messages` | Messages parent |
| 🟡 MOYENNE | `GET /parent/children/{id}/goals` | Objectifs enfant |

### 3.4 Système de révisions — `revisionService.ts`
| Priorité | Endpoint manquant | Utilisé dans |
|----------|-------------------|--------------|
| 🔴 CRITIQUE | `GET /revisions/me/assigned` | Révisions assignées |
| 🔴 CRITIQUE | `POST /revisions/{id}/start` | Démarrer révision |
| 🔴 CRITIQUE | `POST /revisions/{id}/complete` | Terminer révision |
| 🟠 HAUTE | `GET /revisions/{id}/progress` | Progression révision |
| 🟠 HAUTE | `POST /revisions/me/auto-assign` | Auto-assignation |
| 🟠 HAUTE | `GET /revisions/search` | Recherche révisions |

### 3.5 Extras Élève — `studentExtraService.ts`
| Priorité | Endpoint manquant | Utilisé dans |
|----------|-------------------|--------------|
| 🔴 CRITIQUE | `GET /revision-notes` | Notes de révision |
| 🔴 CRITIQUE | `POST /revision-notes` | Créer une note |
| 🔴 CRITIQUE | `DELETE /revision-notes/{id}` | Supprimer une note |
| 🔴 CRITIQUE | `POST /revision-notes/tags/toggle` | Taguer une note |
| 🔴 CRITIQUE | `GET /study-groups/me` | Mes groupes d'étude |
| 🔴 CRITIQUE | `GET /study-groups/{id}` | Détail groupe |
| 🔴 CRITIQUE | `POST /study-groups` | Créer un groupe |
| 🔴 CRITIQUE | `POST /study-groups/join` | Rejoindre un groupe |
| 🔴 CRITIQUE | `DELETE /study-groups/{id}/leave` | Quitter un groupe |
| 🟠 HAUTE | `GET /quiz/mistakes` | Erreurs de quiz |
| 🟠 HAUTE | `POST /quiz/mistakes` | Signaler une erreur |
| 🟠 HAUTE | `GET /quiz/mistakes/subjects` | Erreurs par matière |
| 🟠 HAUTE | `POST /quiz/mistakes/{id}/resolve` | Marquer résolue |
| 🟡 MOYENNE | `GET /student/download-history` | Historique téléchargements |
| 🟡 MOYENNE | `GET /student/reports` | Rapports élève |

### 3.6 Institution — `institutionExtraService.ts`
| Priorité | Endpoint manquant | Utilisé dans |
|----------|-------------------|--------------|
| 🔴 CRITIQUE | `GET /institution/me` | Profil institution |
| 🔴 CRITIQUE | `GET /institution/{id}/students` | Liste élèves |
| 🔴 CRITIQUE | `POST /institution/{id}/students/import` | Import CSV élèves |
| 🔴 CRITIQUE | `GET /institution/{id}/kpis` | KPI institution |
| 🟠 HAUTE | `GET /institution/{id}/subject-stats` | Stats par matière |
| 🟠 HAUTE | `POST /institution/{id}/reports` | Générer rapport |

### 3.7 Inscriptions — `enrollmentService.ts`
| Priorité | Endpoint manquant | Utilisé dans |
|----------|-------------------|--------------|
| 🟠 HAUTE | `GET /enrollments/user/{userId}` | Inscriptions utilisateur |
| 🟠 HAUTE | `GET /enrollments/{userId}/{subjectId}` | Statut inscription |

### 3.8 Notifications — `notificationService.ts`
| Priorité | Endpoint manquant | Utilisé dans |
|----------|-------------------|--------------|
| 🟠 HAUTE | `PUT /notifications/{id}/read` | Marquer une notif lue |
| 🟠 HAUTE | `PUT /notifications/read-all` | Tout marquer lu |
| 🟡 MOYENNE | `PUT /users/settings/notifications` | Préférences notifs |

### 3.9 Commandes & Paiements — `orderService.ts` / `paymentService.ts`
| Priorité | Endpoint manquant | Utilisé dans |
|----------|-------------------|--------------|
| 🔴 CRITIQUE | `POST /orders/{id}/cancel` | Annuler commande |
| 🟠 HAUTE | `GET /orders/{id}/invoice` | Télécharger facture |
| 🟠 HAUTE | `POST /orders/summary` | Résumé commande |
| 🟠 HAUTE | `GET /orders/{id}/status` | Statut commande |
| 🟡 MOYENNE | `GET /orders/statistics` | Stats commandes |
| 🟡 MOYENNE | `GET /orders/search` | Recherche commandes |
| 🟡 MOYENNE | `POST /payments/{id}/verify` | Vérification paiement |
| 🟡 MOYENNE | `POST /payments/{id}/refund` | Remboursement |
| 🟡 MOYENNE | `GET /users/{id}/payment-methods` | Méthodes enregistrées |
| 🟡 MOYENNE | `POST /users/{id}/payment-methods` | Enregistrer méthode |
| 🟡 MOYENNE | `DELETE /users/{id}/payment-methods/{mid}` | Supprimer méthode |

### 3.10 Certificats
| Priorité | Endpoint manquant | Utilisé dans |
|----------|-------------------|--------------|
| 🟡 MOYENNE | `GET /certificates/user/my-certificates` | Mes certificats |
| 🟡 MOYENNE | `POST /certificates/admin/issue` | Émettre certificat |

### 3.11 Analytics & IA — `analyticsService.ts` / `aiService.ts`
| Priorité | Endpoint manquant | Utilisé dans |
|----------|-------------------|--------------|
| 🟡 MOYENNE | `GET /analytics/segments` | Segmentation utilisateurs |
| 🟡 MOYENNE | `GET /ai/study-tips` | Conseils d'étude |

### 3.12 Administration — `adminService.ts`
| Priorité | Endpoint manquant | Utilisé dans |
|----------|-------------------|--------------|
| 🟡 MOYENNE | `GET /promo-codes/admin` | Liste codes promo |
| 🟡 MOYENNE | `PATCH /promo-codes/{id}/status` | Activer/désactiver code |
| 🟡 MOYENNE | `PUT /announcements/{id}` | Modifier annonce |
| 🟡 MOYENNE | `POST /announcements/{id}/publish` | Publier annonce |

---

## 4. ⚠️ Données mockées / hardcodées côté frontend

> Ces composants affichent des données fictives au lieu d'appeler l'API.

| Fichier | Problème | Impact |
|---------|----------|--------|
| `CartContext.tsx:580` | Validation code promo hardcodée : seul `PROMO10` fonctionne | Codes promo réels inutilisables |
| `CartContext.tsx:709` | `throw new Error('createOrder not implemented yet')` | Checkout impossible |
| `PerformanceChart.tsx:23` | Données fixes `[{week:'Sem 1', score:13.5}…]` | Graphe jamais réel |
| `DashboardPage.jsx` | Quotas IA hardcodés `current={47} max={200}` | Affichage incorrect (corrigé en partie) |
| `AdminSubscriptions.tsx` | Commentaire `// Données mockées` | Gestion admin non fonctionnelle |
| `Profile.tsx:267` | `// TODO backend : endpoint absent à ce jour` | Profil stats vide |
| `DashboardPage.jsx` | KPI row (`14h 28m`, `12 jours`…) tous hardcodés | Dashboard élève fictif |
| `DashboardPage.jsx` | Factures récentes hardcodées `[{m:"Mai 2026"…}]` | Pas de vraie facturation |

---

## 5. 🏚️ Endpoints backend non appelés depuis le frontend

> Implémentés côté .NET mais jamais utilisés dans l'interface.

| Endpoint .NET | Raison probable |
|--------------|-----------------|
| `POST /admin/subjects/{id}/approve` | Workflow admin non câblé |
| `POST /admin/subjects/{id}/reject` | Idem |
| `POST /admin/subjects/{id}/pdf` | Upload PDF non intégré au front |
| `POST /admin/emails/send` | Module email admin non affiché |
| `GET /admin/supervision/study-groups` | Panel supervision non fait |
| `POST /admin/supervision/study-groups/{id}/archive` | Idem |
| `GET /admin/supervision/teacher-classes` | Idem |
| `GET /admin/supervision/parent-credits` | Idem |
| `GET /admin/supervision/institution-licences` | Idem |
| `POST /admin/uploads/*` (init, parts, complete, abort) | Upload multipart non utilisé |
| `POST /api/ai/personalized-path` | Doublon avec FastAPI |
| `GET /api/ai/study-habits` | Non affiché dans l'UI |
| `POST /api/ai/exam-coach/*` | Proxy non câblé |

---

## 6. Tableau de synthèse

| Domaine | Appels front | Implémentés | Manquants | Couverture |
|---------|-------------|-------------|-----------|-----------|
| Chatbot / IA | 15 | 15 | 0 | ✅ 100% |
| Auth | 7 | 7 | 0 | ✅ 100% |
| Forum | 8 | 8 | 0 | ✅ 100% |
| Messagerie | 5 | 5 | 0 | ✅ 100% |
| Abonnements | 5 | 5 | 0 | ✅ 100% |
| Paiements | 8 | 4 | 4 | 🟡 50% |
| Notifications | 5 | 3 | 2 | 🟡 60% |
| Dashboard Élève | 9 | 1 | 8 | 🔴 11% |
| Dashboard Enseignant | 15 | 2 | 13 | 🔴 13% |
| Dashboard Parent | 13 | 2 | 11 | 🔴 15% |
| Révisions | 6 | 0 | 6 | 🔴 0% |
| Notes / Groupes d'étude | 9 | 0 | 9 | 🔴 0% |
| Suivi erreurs quiz | 4 | 0 | 4 | 🔴 0% |
| Institution | 6 | 1 | 5 | 🔴 17% |
| Inscriptions | 2 | 0 | 2 | 🔴 0% |
| Certificats | 2 | 0 | 2 | 🔴 0% |
| **TOTAL** | **~124** | **~53** | **~71** | **🔴 ~43%** |

---

## 7. Roadmap recommandée

### Sprint 1 — Fondations (bloquant)
- [ ] `POST /orders` + `GET /orders/{id}/status` → débloquer le checkout
- [ ] `GET /notifications/{id}/read` + `PUT /notifications/read-all`
- [ ] `GET /student/statistics` + `GET /student/progress`

### Sprint 2 — Dashboard Élève
- [ ] `GET /student/learning/continue`
- [ ] `GET /student/exams/recommended`
- [ ] `GET /student/priorities/today`
- [ ] `GET /student/goals` (CRUD)
- [ ] Remplacer les KPIs hardcodés par de vrais appels

### Sprint 3 — Dashboard Enseignant
- [ ] CRUD `/teacher/classes` + `/teacher/classes/{id}/students`
- [ ] `GET /teacher/contents/mine` + `/teacher/contents/{id}/stats`
- [ ] `GET /teacher/revenues` + `/teacher/revenue-share`

### Sprint 4 — Dashboard Parent
- [ ] `GET /parent/children` + `/parent/children/{id}/stats`
- [ ] Système crédits : `GET /parent/credits` + `POST /parent/purchase-for-child`
- [ ] `GET /parent/analytics/{childId}`

### Sprint 5 — Fonctionnalités manquantes
- [ ] Système de révisions complet (`/revisions/*`)
- [ ] Notes de révision + Groupes d'étude (`/revision-notes/*`, `/study-groups/*`)
- [ ] Suivi erreurs quiz (`/quiz/mistakes/*`)
- [ ] Institution KPIs + import CSV

### Sprint 6 — Complétion
- [ ] Certificats
- [ ] Codes promo réels (supprimer le mock `PROMO10`)
- [ ] Factures PDF téléchargeables
- [ ] Données de performance réelles dans les graphiques

---

*Fichier généré automatiquement — mettre à jour après chaque sprint.*
