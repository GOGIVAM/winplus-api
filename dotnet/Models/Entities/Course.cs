using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

public class Course
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ShortDescription { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? PreviewVideoUrl { get; set; }
    public string Language { get; set; } = "fr";
    public string Level { get; set; } = "debutant";
    public string? Category { get; set; }
    public List<string> Tags { get; set; } = new();
    public decimal Price { get; set; }
    public bool IsFree { get; set; }
    public bool IsIncludedInSub { get; set; }
    public string Status { get; set; } = "draft";
    public int InstructorId { get; set; }
    public User Instructor { get; set; } = null!;
    public int? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? RejectionReason { get; set; }
    public int TotalDurationMin { get; set; }
    public int LessonsCount { get; set; }
    public int EnrolledCount { get; set; }
    public decimal? AvgRating { get; set; }
    public int ReviewsCount { get; set; }
    public List<string> Requirements { get; set; } = new();
    public List<string> Objectives { get; set; } = new();
    public bool CertificateEnabled { get; set; } = true;

    /// <summary>Canal de discussion par formation avec Q&amp;A automatique WinAI (Module 7, 3C).</summary>
    public bool CanalMessagerie { get; set; } = false;

    /// <summary>
    /// Nombre de jours d'inactivité avant qu'un élève soit signalé comme
    /// décrocheur potentiel (US-FOR-07, Module 9). Configurable par le
    /// professeur, 7 par défaut.
    /// </summary>
    public int InactivityThresholdDays { get; set; } = 7;

    /// <summary>
    /// Date de publication, renseignee par AdminCourseController.Approve.
    ///
    /// La propriete etait utilisee dans le controleur (lignes 71 et 152) mais
    /// absente de l'entite : erreur de compilation CS1061. Nullable, car une
    /// formation en brouillon ou rejetee n'a pas de date de publication.
    ///
    /// La colonne SQL correspondante est ajoutee par
    /// sql/003_add_courses_publishedat.sql.
    /// </summary>
    public DateTime? PublishedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CourseSection> Sections { get; set; } = new List<CourseSection>();
    public ICollection<CourseEnrollment> CourseEnrollments { get; set; } = new List<CourseEnrollment>();
    public ICollection<CourseReview> CourseReviews { get; set; } = new List<CourseReview>();
}

public class CourseSection
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Position { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Règle de déblocage (drip content, prompt_prof.md Module 5B) :
    /// "immediate" | "delay_days" | "min_score". "immediate" = toujours
    /// disponible dès l'inscription, indépendamment des autres sections.
    /// </summary>
    public string UnlockRule { get; set; } = "immediate";

    /// <summary>Nombre de jours après l'inscription avant déblocage (règle "delay_days").</summary>
    public int? DelayDays { get; set; }

    /// <summary>
    /// Score minimum (0-100) requis sur le quiz de la section précédente pour
    /// débloquer celle-ci (règle "min_score"). Sans quiz sur la section
    /// précédente, la règle ne peut pas s'appliquer et la section reste
    /// débloquée par défaut — voir CoursePlayerController.ComputeSectionAccess.
    /// </summary>
    public int? MinScore { get; set; }

    public ICollection<CourseLesson> Lessons { get; set; } = new List<CourseLesson>();
}

public class CourseLesson
{
    public int Id { get; set; }
    public int SectionId { get; set; }
    public CourseSection Section { get; set; } = null!;
    public int CourseId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string LessonType { get; set; } = "video";
    public string? VideoUrl { get; set; }
    public int? VideoDurationSec { get; set; }
    public string? ArticleContent { get; set; }
    public string? FileUrl { get; set; }
    public string? FileName { get; set; }
    public int? QuizId { get; set; }
    public int Position { get; set; }
    public bool IsPreview { get; set; }
    public bool IsPublished { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Épreuve/correction du catalogue ajoutée comme ressource dans cette
    /// leçon (Module 2, US-CAT-03 "Ajouter à une formation"). Sert aussi à
    /// calculer "X enseignants ont utilisé ce contenu" (COUNT DISTINCT
    /// Course.InstructorId sur les leçons référençant ce Subject).
    /// </summary>
    public int? SourceSubjectId { get; set; }

    /// <summary>
    /// Checkpoints vidéo (prompt_prof.md Module 5B) : JSON d'une liste de
    /// { timestampMs, question, options[], bonneReponseIndex }. Sérialisé/
    /// désérialisé côté contrôleur (pas de colonne JSON typée EF ici, cohérent
    /// avec Tags/Requirements sur Course qui restent des List&lt;string&gt; côté
    /// C# mappées en JSON par ailleurs) — voir CheckpointDto.
    /// </summary>
    public string? CheckpointsJson { get; set; }
}

public class CourseEnrollment
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;
    public string AccessType { get; set; } = "purchase";
    public int? OrderId { get; set; }
    public decimal ProgressPercent { get; set; }
    public DateTime? LastAccessedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? CertificateUrl { get; set; }
    public bool IsActive { get; set; } = true;
}

public class LessonProgress
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int LessonId { get; set; }
    public int CourseId { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int WatchTimeSec { get; set; }
    public int LastPositionSec { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Trace qu'une notification "section débloquée" a déjà été envoyée pour un
/// couple (élève, section) — évite les doublons puisque SectionUnlockNotificationService
/// tourne périodiquement et réévalue l'accès à chaque passage.
/// </summary>
public class SectionUnlockNotification
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int SectionId { get; set; }
    public DateTime NotifiedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Trace l'envoi d'une relance WinAI à un élève inactif (US-FOR-07). Sert à
/// mesurer le taux de réactivation (l'élève a-t-il repris une leçon après la
/// relance ?) et à éviter de relancer deux fois le même jour.
/// </summary>
public class CourseInactivityRelaunch
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public int UserId { get; set; }
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Alerte de décrochage persistée (Module 8, 8A) : détectée quotidiennement par
/// CourseInactivityAlertService (inactivité + baisse de score de quiz, scorée par
/// FastAPI /api/winai/detection-decrochage), consultable en un seul endroit pour
/// toutes les formations du professeur (contrairement à ComputeInactiveStudentsAsync
/// qui recalcule à la volée, formation par formation, sans mémoriser un traitement).
/// </summary>
public class AlerteDecrochage
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    [ForeignKey(nameof(CourseId))]
    public Course Course { get; set; } = null!;
    public int StudentUserId { get; set; }
    [ForeignKey(nameof(StudentUserId))]
    public User Student { get; set; } = null!;
    /// <summary>"faible" ou "eleve".</summary>
    public string Niveau { get; set; } = "faible";
    /// <summary>Liste de signaux (ex: "inactif depuis 14 jours", "scores de quiz en baisse"), sérialisée en JSON.</summary>
    public string SignauxJson { get; set; } = "[]";
    public DateTime DateDetection { get; set; } = DateTime.UtcNow;
    public bool Traitee { get; set; }
    public DateTime? TraiteeAt { get; set; }
}

public class CourseReview
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public short Rating { get; set; }
    public string? Comment { get; set; }
    public bool IsVerified { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
