# 📚 Classification Complète - Épreuves, Sujets, Quiz, Révisions & Autres
**Date:** 18 Février 2026  
**Status:** Classification Exhaustive Frontend ↔ Backend

---

## 🎯 Vue Globale de la Taxonomie

```
┌─────────────────────────────────────────────────────────┐
│             RESSOURCES ÉDUCATIVES (Win+)                │
├─────────────────────────────────────────────────────────┤
│                                                          │
│  1. SUBJECTS (Sujets/Matières)                          │
│     └─ Mathématiques, Français, Physique, etc.         │
│                                                          │
│  2. EXAMS (Épreuves)                                    │
│     ├─ Baccalauréat (licences/séries)                  │
│     ├─ Probatoire                                       │
│     └─ BEPC                                             │
│                                                          │
│  3. COURSE CONTENT (Contenu Pédagogique)               │
│     ├─ Leçons/Modules (vidéos + docs)                  │
│     ├─ Quiz interactifs                                │
│     └─ Exercices pratiques                             │
│                                                          │
│  4. ASSESSMENTS (Évaluations)                          │
│     ├─ Quizzes (auto-générés par IA)                   │
│     ├─ Mock exams (simulations)                        │
│     └─ Corrections détaillées                          │
│                                                          │
│  5. REVISIONS & PRACTICE                               │
│     ├─ Fiches de révision                              │
│     ├─ Exercices ciblés                                │
│     └─ Corrections pas-à-pas                           │
│                                                          │
│  6. SESSIONS & EVENTS                                  │
│     ├─ Sessions d'enseignement (Teacher)               │
│     ├─ Événements (examens, deadlines)                 │
│     └─ Annonces système                                │
│                                                          │
│  7. PROGRESS TRACKING                                  │
│     ├─ Enrollments (inscription)                       │
│     ├─ Learning History (activité)                     │
│     └─ Certificates (diplômes)                         │
│                                                          │
└─────────────────────────────────────────────────────────┘
```

---

# 1️⃣ SUBJECTS (Sujets/Matières)

Ces sont les **disciplines académiques** : Mathématiques, Français, Anglais, SVT, Physique, etc.

## Frontend

**Fichiers:**
- `src/pages/CatalogPage.tsx`
- `src/types/catalog.ts`
- `src/services/catalogService.ts`

**Interface TypeScript:**
```typescript
// src/types/catalog.ts
export interface Subject {
  id: string;
  title: string;              // ex: "Mathématiques"
  description: string;
  category: string;           // "Matière"
  imageUrl?: string;
  price: number;
  isFree: boolean;
  isPremium: boolean;
  rating?: number;
  downloads: number;
  views: number;
  tags: string[];            // ["Algèbre", "Géométrie"]
  createdAt: string;
  updatedAt: string;
}
```

**État/Stockage:**
```tsx
const [subjects, setSubjects] = useState<Subject[]>([]);

// Chargement
const subjectsData = await catalogService.getAllSubjects(1, 100);
setSubjects(subjectsData?.data || []);

// Utilisation
subjects.map(subject => (
  <SubjectCard key={subject.id} subject={subject} />
))
```

**Service Frontend:**
```typescript
// src/services/catalogService.ts
getAllSubjects: async (page, pageSize) => {
  return api.get(`/subjects?page=${page}&pageSize=${pageSize}`);
},

getSubjectsByCategory: async (categoryName, page, pageSize) => {
  return api.get(`/subjects/by-category/${categoryName}?...`);
},

searchSubjects: async (params) => {
  // Recherche par nom, tags, difficulté
  return api.get(`/subjects/search?q=${query}&...`);
}
```

## Backend

**Entity (.NET):**
```csharp
// Models/Entities/Subject.cs
public class Subject
{
    public int Id { get; set; }
    public string Title { get; set; }              // "Mathématiques"
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? ThumbnailUrl { get; set; }
    public decimal Price { get; set; }
    public bool IsPublished { get; set; }
    public int EnrollmentCount { get; set; }
    public decimal AverageRating { get; set; }
    public int TotalRatings { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
    
    // Relations
    public ICollection<CourseContent> Contents { get; set; }
    public ICollection<Enrollment> Enrollments { get; set; }
    public ICollection<LearningHistory> LearningHistories { get; set; }
}
```

**Repository:**
```csharp
public interface ISubjectRepository
{
    Task<Subject?> GetByIdAsync(int id);
    Task<IEnumerable<Subject>> GetAllAsync();
    Task<IEnumerable<Subject>> GetPublishedAsync();
    Task<IEnumerable<Subject>> GetByCategoryAsync(string category);
    Task<IEnumerable<Subject>> SearchAsync(string searchTerm);
    Task<IEnumerable<Subject>> GetPopularAsync(int limit = 10);
}
```

**Service:**
```csharp
public interface ISubjectService
{
    Task<Subject?> GetSubjectByIdAsync(int id);
    Task<IEnumerable<Subject>> GetAllSubjectsAsync();
    Task<IEnumerable<Subject>> GetPublishedSubjectsAsync();
    Task<IEnumerable<Subject>> GetSubjectsByCategoryAsync(string category);
    Task<IEnumerable<Subject>> SearchSubjectsAsync(string searchTerm);
    Task<IEnumerable<Subject>> GetPopularSubjectsAsync(int limit = 10);
}
```

**Controller API:**
```csharp
// Controllers/SubjectsController.cs
[HttpGet]                           // GET  /api/subjects?page=1&pageSize=20
GetAll(int page, int pageSize)

[HttpGet("{id}")]                   // GET  /api/subjects/{id}
GetById(int id)

[HttpGet("published")]              // GET  /api/subjects/published
GetPublished()

[HttpGet("by-category/{category}")] // GET  /api/subjects/by-category/maths
GetByCategory(string category)

[HttpGet("search")]                 // GET  /api/subjects/search?q=algo
Search(string q)
```

**Structure BD:**
```
Subjects
├─ Id (int, PK)
├─ Title (nvarchar)
├─ Description (nvarchar)
├─ Category (nvarchar)
├─ ThumbnailUrl (nvarchar)
├─ Price (decimal)             
├─ IsPublished (bit)
├─ AverageRating (decimal)
└─ CreatedAt (datetime)
```

---

# 2️⃣ EXAMS (Épreuves/Examens Officiels)

Les **épreuves formelles** d'examens (Bac 2024, Probatoire 2023, BEPC, etc.)

## Frontend

**Fichiers:**
- `src/pages/CatalogPage.tsx`
- `src/pages/SubjectDetailsPage.tsx`
- `src/pages/UpcomingExams.tsx`

**Interface TypeScript:**
```typescript
// Considéré comme Subject avec champs supplémentaires
export interface Subject {
  // ... champs de base
  
  // Spécifiques aux épreuves
  exam: string;              // "Baccalauréat", "Probatoire", "BEPC"
  subject: string;           // "Mathématiques", "Français"
  year: number;              // 2024, 2023, 2022
  session?: string;          // "Normale", "Remplacement"
  level?: string;            // "Terminale", "Première", "Seconde"
  difficulty: DifficultyLevel; // 1-5
  duration: number;          // en minutes
  coefficient?: number;      // coefficient de l'épreuve
  hasCorrection: boolean;    // Correction incluse?
  correctionId?: string;     // ID de la correction associée
}

// Dans CatalogPage, les filtres
const examTypes = [
  { value: 'baccalaureat', label: 'Baccalauréat' },
  { value: 'probatoire', label: 'Probatoire' },
  { value: 'bepc', label: 'BEPC' }
];
```

**État/Stockage:**
```tsx
const [selectedExamType, setSelectedExamType] = useState('tous');
const [selectedYear, setSelectedYear] = useState('tous');

// Filtrage
const filteredTests = useMemo(() => {
  return allTests.filter(test => {
    const examMatch = selectedExamType === 'tous' || test.examType === selectedExamType;
    const yearMatch = selectedYear === 'tous' || test.year === selectedYear;
    return searchMatch && examMatch && yearMatch && /* autres filtres */;
  });
}, [allTests, selectedExamType, selectedYear]);
```

**Opérations:**
```tsx
// Chargement
const testsData = await catalogService.getAllSubjects(1, 200);
setAllTests(testsData?.data || []);

// Filtrage avancé
const filtered = allTests.filter(test => 
  test.exam === 'Baccalauréat' && 
  test.year === 2024 &&
  test.subject === 'Mathématiques'
);

// Consultation détails
const details = await catalogService.getSubjectDetails(examId);

// Affichage dans UpcomingExams
const upcomingExams = [
  {
    id: '1',
    name: 'Baccalauréat 2025',
    date: '2025-06-15',
    daysLeft: 120,
    prepared: 68,
    subjects: ['Mathématiques', 'Physique', 'SVT']
  }
];
```

## Backend

**Entity (.NET):**
```csharp
// Les épreuves sont modélisées comme Subject
// avec les champs supplémentaires (manquants actuellement):
public class Subject
{
    // ... champs de base
    
    // À AJOUTER pour épreuves complètes:
    // public string? Exam { get; set; }        // "Baccalauréat"
    // public int? Year { get; set; }           // 2024
    // public string? Session { get; set; }     // "Normale"
    // public string? Level { get; set; }       // "Terminale"
    // public int? Difficulty { get; set; }     // 1-5
    // public int? Duration { get; set; }       // minutes
    // public int? Coefficient { get; set; }    // coefficient
    // public bool? HasCorrection { get; set; }
}
```

**API Endpoints:**
```
GET  /api/subjects                      - Tous les sujets
GET  /api/subjects/{id}                 - Détails d'une épreuve
GET  /api/subjects/search?exam=bac      - Épreuves de bac
GET  /api/subjects/search?year=2024     - Épreuves de 2024
GET  /api/subjects/by-category/maths    - Épreuves de maths
```

⚠️ **LIMITATION:** Le model Subject backend ne stocke pas les champs `exam`, `year`, `session`, etc. Ces données doivent être enrichies ou une table dédiée `Exam` créée.

---

# 3️⃣ COURSE CONTENT (Contenu Pédagogique)

Les **leçons, modules et matériel** à l'intérieur d'un sujet/cours.

## Frontend

**Fichiers:**
- `src/pages/professeur.tsx` (Teacher dashboard)
- `src/pages/Discover.tsx`
- `src/pages/Dashboard.tsx`

**Utilisation:**
```tsx
const [content, setContent] = useState([
  {
    id: '1',
    title: 'Introduction aux Intégrales',
    description: 'Chapitre 1: Calcul intégral',
    type: 'lesson',        // or 'video', 'document', 'exercise'
    duration: 45,          // minutes
    level: 'Terminale C',
    rating: 4.8,
    completed: false,
    videoUrl: 'https://...',
    documentUrl: 'https://...',
    order: 1
  }
]);

// Affichage dans cours
content.map(c => (
  <ContentCard 
    key={c.id} 
    content={c}
    onSelect={() => viewContent(c.id)}
  />
))
```

## Backend

**Entity (.NET):**
```csharp
// Models/Entities/CourseContent.cs
public class CourseContent
{
    public int Id { get; set; }
    public int SubjectId { get; set; }          // FK vers Subject
    public string Title { get; set; }           // "Intégrales - Partie 1"
    public string? Description { get; set; }
    public string? VideoUrl { get; set; }       // URL vidéo YouTube/Vimeo
    public string? DocumentUrl { get; set; }    // PDF, slides
    public int OrderIndex { get; set; }         // Ordre du module
    public int DurationMinutes { get; set; }    // 45, 60, etc.
    public bool IsLocked { get; set; }          // Premium? Verrouillé?
    public DateTime CreatedAt { get; set; }
    
    // Navigation
    public Subject Subject { get; set; }
    public ICollection<LearningHistory> LearningHistories { get; set; }
}
```

**Service:** Généralement via SubjectService
```csharp
public interface ISubjectService
{
    // Récupère tous les contenus d'un sujet
    Task<IEnumerable<CourseContent>> GetSubjectContentsAsync(int subjectId);
}
```

**API Endpoints:**
```
GET  /api/subjects/{subjectId}/contents     - Modules d'un sujet
GET  /api/courses/{contentId}               - Détails d'un module
POST /api/subjects/{id}/mark-complete       - Marquer comme complété
```

**Structure BD:**
```
CourseContents
├─ Id (int, PK)
├─ SubjectId (int, FK → Subjects)
├─ Title (nvarchar)
├─ VideoUrl (nvarchar)
├─ DocumentUrl (nvarchar)
├─ OrderIndex (int)
├─ DurationMinutes (int)
├─ IsLocked (bit)
└─ CreatedAt (datetime)
```

---

# 4️⃣ QUIZ (Questionnaires/Tests)

Les **questionnaires interactifs** générés par IA ou créés manuellement.

## Frontend

**Fichiers:**
- `src/pages/Student.tsx`
- `src/pages/Dashboard.tsx`
- `src/services/aiService.ts`

**Interface/État:**
```typescript
interface Quiz {
  id: string;
  title: string;
  subject: string;
  difficulty: 'easy' | 'medium' | 'hard';
  questions: QuizQuestion[];
  timeLimit: number;        // minutes
  passingScore: number;     // 60
  createdAt: string;
}

interface QuizQuestion {
  id: string;
  question: string;
  options: string[];
  correctAnswer: string;
  explanation?: string;
}

// État
const [quizzes, setQuizzes] = useState<Quiz[]>([
  {
    id: '1',
    title: 'Quiz Trigonométrie - Niveau 2',
    subject: 'Mathématiques',
    difficulty: 'medium',
    timeLimit: 30,
    passingScore: 60,
    questions: [...]
  }
]);

// Affichage recommandations IA
const aiRecommendations = [
  {
    id: 1,
    title: "Quiz ciblé: Trigonométrie",
    reason: "Améliorer votre vitesse de calcul",
    confidence: 85,
    estimatedScore: "+20% de rapidité",
    duration: "15 min",
    type: "quiz"
  }
];
```

**Service Frontend (appel à IA/API):**
```typescript
// src/services/aiService.ts
static async generateAdaptiveQuiz(
  courseId: string,
  previousScore: number
): Promise<any[]> {
  return api.post(`/api/ai/generate-quiz`, {
    subject: courseId,
    difficulty: previousScore < 60 ? 'easy' : 'medium',
    count: 10
  });
}

// Dans Student.tsx
const handleStartQuiz = async (subject: string) => {
  const quiz = await AIService.generateAdaptiveQuiz(subject, userScore);
  navigateToQuiz(quiz);
};
```

## Backend

**Service (FastApi - IA):**
```python
# backend/fastapi_api/services/quiz_generator.py
def generate_quiz(subject, difficulty='medium', count=10):
    """
    Génère des questions de quiz basées sur le sujet
    Input:
    - subject: str (ex: "Mathématiques")
    - difficulty: str ('easy', 'medium', 'hard')
    - count: int (nombre de questions, max 20)
    
    Output:
    {
        "totalQuestions": 10,
        "difficulty": "medium",
        "questions": [
            {
                "id": "q1",
                "question": "Qu'est-ce que une intégrale?",
                "options": ["Réponse A", "B", "C", "D"],
                "correctAnswer": "A",
                "explanation": "..."
            }
        ]
    }
    """
```

**API Endpoint (FastApi):**
```
POST /api/ai/generate-quiz
├─ Input: { subject, difficulty, count }
├─ Output: { questions, totalQuestions, difficulty }
└─ Généré par: backend/fastapi_api
```

**API Endpoint (.NET):**
```csharp
// Si stocké en .NET (optionnel)
[HttpGet("quizzes/available")]           // GET  /api/quizzes/available?limit=10
[HttpPost("{id}/submit")]                // POST /api/quizzes/{id}/submit (soumission)
[HttpGet("{id}/results")]                // GET  /api/quizzes/{id}/results (résultats)
```

**Stockage:**
- ✅ Généré à la demande (pas stocké en BD généralement)
- ❌ Pas d'entité `Quiz` en Base depuis le backend .NET actuellement

---

# 5️⃣ REVISIONS & PRACTICE (Révisions et Exercices)

Les **fiches de révision, exercices ciblés** et **corrections détaillées**.

## Frontend

**Fichiers:**
- `src/pages/Student.tsx`
- `src/pages/Dashboard.tsx`
- `src/pages/professeur.tsx` (teacher view)

**État/Recommandations:**
```tsx
const aiRecommendations = [
  {
    id: 2,
    title: "Chapitre: Dérivées partielles",
    reason: "Prérequis pour votre prochain cours",
    confidence: 88,
    estimatedScore: "Score attendu: B+",
    duration: "30 min",
    type: "chapter"           // ← Révision/théorie
  },
  {
    id: 3,
    title: "Quiz ciblé: Trigonométrie",
    reason: "Améliorer votre vitesse de calcul",
    confidence: 85,
    estimatedScore: "+20% de rapidité",
    duration: "15 min",
    type: "quiz"              // ← Pratique
  }
];

// Bouton dans UI
<button onClick={() => navigate(`/revisions/${topicId}`)}>
  Réviser: Dérivées
</button>
```

## Backend

**Service Placeholder:**
```csharp
// Services/TeacherService.cs
public async Task<IEnumerable<dynamic>> GetTeacherRevisionsAsync(int teacherId, int limit = 10)
{
    try
    {
        // Actuellement: Placeholder
        // Nécessite une table "Revisions" en BD
        return Enumerable.Empty<dynamic>();
    }
}

// Services/ParentService.cs
public async Task<IEnumerable<dynamic>> GetChildRevisionsAsync(int parentId, int childId, int limit = 10)
{
    try
    {
        // Nécessite une table "Revisions"
        return Enumerable.Empty<dynamic>();
    }
}
```

**API Endpoint:**
```
GET /api/teacher/revisions/available?limit=10        - Révisions pour prof
GET /api/parent/revisions/available?limit=10         - Révisions pour enfant
```

⚠️ **LIMITATION:** Les révisions ne sont pas encore implémentées en BD. À créer:
- Table `Revisions` 
- Service `RevisionService`
- Controller endpoint

---

# 6️⃣ SESSIONS & EVENTS (Sessions d'Enseignement & Événements)

Les **classes en ligne, sessions de coaching** et **événements importants** (examens, deadlines).

## Frontend

**Fichiers:**
- `src/pages/UpcomingExams.tsx`
- `src/pages/Dashboard.tsx`
- `src/pages/professeur.tsx`

**État/Affichage:**
```tsx
// Examens à venir
const [upcomingExams, setUpcomingExams] = useState([
  {
    id: '1',
    name: 'Baccalauréat 2025',
    date: '2025-06-15',
    daysLeft: 120,
    prepared: 68,
    subjects: ['Mathématiques', 'Physique', 'Chimie']
  }
]);

// Sessions Teacher
const [teacherSessions, setTeacherSessions] = useState([
  {
    id: '1',
    title: 'Session Mathématiques - Groupe A',
    description: 'Révision intégrales',
    startDate: '2025-02-20T14:00:00',
    endDate: '2025-02-20T15:30:00',
    maxParticipants: 25,
    status: 'Scheduled',
    createdBy: 123
  }
]);
```

## Backend

**Entities (.NET):**
```csharp
// Models/Entities/Event.cs - Événement officiel
public class Event
{
    public int Id { get; set; }
    public string Title { get; set; }           // "Baccalauréat 2025"
    public string? Description { get; set; }
    public DateTime StartDate { get; set; }     // 2025-06-01
    public DateTime EndDate { get; set; }       // 2025-06-15
    public string? Location { get; set; }       // Physique ou online
    public string EventType { get; set; }       // "Exam", "Deadline", "Session"
    public DateTime CreatedAt { get; set; }
}

// Models/Entities/Session.cs - Session d'enseignement
public class Session
{
    public int Id { get; set; }
    public string Title { get; set; }           // "Session Maths - Groupe A"
    public string? Description { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int MaxParticipants { get; set; }
    public string Status { get; set; }          // "Scheduled", "Ongoing", "Completed"
    public int CreatedBy { get; set; }          // Teacher ID
    public DateTime CreatedAt { get; set; }
}
```

**Services:**
```csharp
// Services/IEventRepository.cs
public interface IEventRepository
{
    Task<IEnumerable<Event>> GetUpcomingAsync(int limit = 10);
    Task<IEnumerable<Event>> GetByTypeAsync(string eventType);
    Task<IEnumerable<Event>> GetByDateRangeAsync(DateTime start, DateTime end);
}

// Services/ISessionRepository.cs
public interface ISessionRepository
{
    Task<IEnumerable<Session>> GetUpcomingAsync();
    Task<IEnumerable<Session>> GetByTeacherAsync(int teacherId);
    Task<IEnumerable<Session>> GetByStatusAsync(string status);
}
```

**Controllers (Créés Session 8):**
```csharp
// Controllers/EventController.cs
[HttpGet("upcoming")]                   // GET  /api/events/upcoming?limit=10
GetUpcoming(int limit = 10)

[HttpGet("by-type/{type}")]             // GET  /api/events/by-type/exam
GetByType(string type)

// Controllers/SessionController.cs (A créer)
[HttpGet("upcoming")]                   // GET  /api/sessions/upcoming
GetUpcoming()

[HttpGet("by-teacher/{teacherId}")]     // GET  /api/sessions/by-teacher/{id}
GetByTeacher(int teacherId)
```

**API Endpoints (Session 8):**
```
GET  /api/events/upcoming?limit=10              - Événements à venir
GET  /api/events/by-type/exam                   - Examens planifiés
GET  /api/teacher/sessions/upcoming?limit=10    - Sessions du prof
GET  /api/parent/events/upcoming?limit=10       - Événements parent
```

---

# 7️⃣ PROGRESS TRACKING (Suivi et Progression)

Le système de **suivi de progression utilisateur** :

## Frontend

**Fichiers:**
- `src/pages/UserDashboard.tsx`
- `src/pages/Student.tsx`
- `src/pages/Parent.tsx`

**État/Métriques:**
```tsx
const [userStats, setUserStats] = useState({
  enrollmentCount: 12,
  completionRate: 68,
  averageScore: 78.5,
  hoursSpent: 156,
  streakDays: 14,
  certificatesEarned: 3
});

const [upcomingPayments, setUpcomingPayments] = useState([
  {
    date: '2025-03-15',
    amount: '5000 FCFA',
    subject: 'Renew: Mathematics Premium',
    status: 'pending'
  }
]);
```

## Backend

**Entities (.NET):**
```csharp
// Models/Entities/Enrollment.cs - Inscription
public class Enrollment
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int SubjectId { get; set; }
    public DateTime EnrolledAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public decimal ProgressPercentage { get; set; } // 0-100
    public bool IsCompleted { get; set; }
    public string? CertificateUrl { get; set; }
}

// Models/Entities/LearningHistory.cs - Historique activité
public class LearningHistory
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int SubjectId { get; set; }
    public int? ContentId { get; set; }
    public string EventType { get; set; }        // "Viewed", "Completed", "Quizzed"
    public int? TimeSpentSeconds { get; set; }
    public decimal? QuizScore { get; set; }      // Score du test
    public bool IsCompleted { get; set; }
    public DateTime CreatedAt { get; set; }
}

// Models/Entities/Certificate.cs - Diplôme/Certificat
public class Certificate
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int EnrollmentId { get; set; }
    public string Title { get; set; }            // "Certificate of Completion"
    public string CertificateUrl { get; set; }
    public decimal FinalScore { get; set; }
    public DateTime IssuedAt { get; set; }
}

// Models/Entities/Subscription.cs - Abonnement (Session 8)
public class Subscription
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int PricingPlanId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Status { get; set; }           // "Active", "Expired", "Paused"
    public DateTime? RenewalDate { get; set; }
}
```

**Service Endpoints:**
```csharp
// Services pour student/parent/teacher

// Enrollment
public interface IEnrollmentService
{
    Task<Enrollment> EnrollUserAsync(int userId, int subjectId);
    Task<IEnumerable<Enrollment>> GetUserEnrollmentsAsync(int userId);
    Task<Enrollment?> GetEnrollmentAsync(int userId, int subjectId);
    Task<decimal> GetProgressAsync(int enrollmentId, int userId);
    Task<bool> MarkCompleteAsync(int enrollmentId, int userId);
}

// Learning History (auto-tracked)
public interface ILearningHistoryService
{
    Task<IEnumerable<LearningHistory>> GetUserActivityAsync(int userId);
    Task<decimal> GetAverageScoreAsync(int userId);
    Task RecordEventAsync(int userId, string eventType, int? score);
}

// Certificates
public interface ICertificateService
{
    Task<Certificate> IssueCertificateAsync(int enrollmentId);
    Task<IEnumerable<Certificate>> GetUserCertificatesAsync(int userId);
}
```

**API Endpoints:**
```
POST   /api/enrollments                         - S'inscrire à un sujet
GET    /api/enrollments/user/{userId}           - Mes inscriptions
GET    /api/enrollments/{enrollmentId}/progress - Progression
POST   /api/enrollments/{id}/mark-complete      - Marquer complété
GET    /api/certificates/user/{userId}          - Mes certificats
GET    /api/subscriptions/user/{userId}         - Mes abonnements
```

---

# 8️⃣ ANNOUNCEMENTS (Annonces Système)

Les **notifications et annonces** globales de la plateforme.

## Frontend

**Utilisation:**
```tsx
const [announcements, setAnnouncements] = useState([
  {
    id: 1,
    text: "Parents : Suivez les progrès en temps réel",
    cta: "Créer compte",
    color: "linear-gradient(...)"
  }
]);

// Auto-rotation chaque 5s dans CatalogPage
React.useEffect(() => {
  const interval = setInterval(() => {
    setCurrentAnnouncementIndex((prev) => 
      prev === announcements.length - 1 ? 0 : prev + 1
    );
  }, 5000);
}, []);
```

## Backend

**Entity (.NET) - Session 8:**
```csharp
// Models/Entities/Announcement.cs
public class Announcement
{
    public int Id { get; set; }
    public string Title { get; set; }
    public string Content { get; set; }
    public string Priority { get; set; }        // "High", "Normal", "Low"
    public bool IsPublished { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
```

**API Endpoint - Session 8:**
```
GET  /api/announcements                  - Annonces publiées
GET  /api/announcements/all              - Toutes (admin)
POST /api/announcements                  - Créer (admin)
```

**Service:**
```csharp
public interface IAnnouncementService
{
    Task<IEnumerable<Announcement>> GetPublishedAnnouncementsAsync();
    Task<IEnumerable<Announcement>> GetRecentAsync(int limit = 10);
    Task<Announcement> CreateAnnouncementAsync(Announcement announcement);
}
```

---

# 📊 Tableau Récapitulatif

| **Catégorie** | **Entité BD** | **Frontend** | **Service Backend** | **API Endpoint** | **Status** |
|---------------|--------------|------------|------------------|-----------------|-----------|
| **Subjects** | Subject | catalogService | SubjectService | GET /api/subjects | ✅ OK |
| **Exams** | Subject (limited) | CatalogPage filters | SubjectService | GET /api/subjects | ⚠️ Incomplete |
| **Content** | CourseContent | Discover, Dashboard | SubjectService | GET /api/subjects/{id}/contents | ✅ OK |
| **Quizzes** | None (IA) | Student, Dashboard | AIService (FastApi) | POST /api/ai/generate-quiz | ✅ FastApi |
| **Revisions** | None (TODO) | Student | None | GET /api/.../revisions | ❌ TODO |
| **Sessions** | Session | professeur.tsx | SessionService | GET /api/teacher/sessions | ✅ Session 8 |
| **Events** | Event | UpcomingExams | EventService | GET /api/events/upcoming | ✅ Session 8 |
| **Progress** | Enrollment, LearningHistory | Dashboards | EnrollmentService | GET /api/enrollments/user | ✅ OK |
| **Certificates** | Certificate | Achievements | CertificateService | GET /api/certificates | ✅ OK |
| **Analytics** | AnalyticsEvent | Admin, professeur | AnalyticsService | GET /api/analytics | ✅ OK |
| **Announcements** | Announcement | Banner rotation | AnnouncementService | GET /api/announcements | ✅ Session 8 |
| **Pricing Plans** | PricingPlan | Pricing page | PricingService | GET /api/pricing/plans | ✅ Session 8 |
| **Subscriptions** | Subscription | Billing, Profile | SubscriptionService | GET /api/subscriptions | ✅ Session 8 |

---

# 🔄 Flux Utilisateur Complet

```
┌─────────────────────┐
│   User Registers    │ → Creates User
└──────────┬──────────┘
           │
           ↓
┌─────────────────────┐
│  Browse Subjects    │ → GET /api/subjects
│  Filter by Exam/Year│    (Math, Phys, French)
└──────────┬──────────┘
           │
           ↓
┌─────────────────────┐
│  Select Exam Paper  │ → GET /api/subjects/{id}
│  (Bac Maths 2024)   │    (Details, Correction)
└──────────┬──────────┘
           │
           ├─ Option A: Start Lesson
           │  ↓
           │  GET /api/subjects/{id}/contents
           │  → CourseContent (Video + Doc)
           │  ↓
           │  POST /api/enrollments (Register)
           │  ↓
           │  Enroll entity created
           │
           ├─ Option B: Take Quiz
           │  ↓
           │  POST /api/ai/generate-quiz (FastApi)
           │  ↓
           │  Get 10 random questions
           │  ↓
           │  Submit answers
           │  ↓
           │  LearningHistory recorded + Score
           │
           └─ Option C: Review Correction
              ↓
              GET /api/subjects/{id}
              → hasCorrection: true
              → PDF/Video de correction

           After Activity:
           ↓
┌─────────────────────┐
│   Track Progress    │ → LearningHistory updated
│   View Dashboard    │    Enrollment.Progress updated
│   Get Stats         │    Certificate earned (if 100%)
└─────────────────────┘
```

---

# 🎯 Résumé Classification

**8 Catégories Principales:**

1. **SUBJECTS** = Disciplines académiques (Maths, Français, etc.)
2. **EXAMS** = Épreuves officielles (Bac, Probatoire, BEPC) 
3. **COURSE CONTENT** = Modules/Leçons du cours
4. **QUIZZES** = Tests interactifs (générés IA)
5. **REVISIONS** = Fiches et exercices ciblés (TODO en BD)
6. **SESSIONS & EVENTS** = Classes en ligne + événements
7. **PROGRESS** = Inscriptions, historique, certificats
8. **ANNOUNCEMENTS** = Notifications système

**Architecture Pattern:**
```
Frontend (React)
  ↓ (fetch)
API (.NET Controller)
  ↓ (delegate)
Service (Business Logic)
  ↓ (query)
Repository (Data Access)
  ↓ (SQL)
Database (PostgreSQL/SQL Server)
```

**État en Frontend:** useState hooks + memoized filtering
**État en Backend:** Entity Framework Core + async/await
**Communication:** RESTful JSON APIs (axios/fetch)

---

✅ **Document Unique Complet** - Classification + Frontend + Backend pour chaque catégorie
