namespace Backend.Models.Entities;
using System.Text.Json.Serialization;

/// <summary>
/// Subject entity - represents a course or educational subject
/// </summary>
public class Subject
{
    public int Id { get; set; }
    
    public required string Title { get; set; }
    
    public string? Description { get; set; }
    
    public string? Category { get; set; }

    /// <summary>
    /// Niveau(x) de classe ciblé(s), liste séparée par virgules (ex: "Terminale,Premiere").
    /// Null = visible à tous les niveaux. Sert au filtrage du sélecteur matière
    /// à la génération de quiz/fiches IA (GET /api/subjects/categories?level=).
    /// </summary>
    public string? Level { get; set; }

    public string? ThumbnailUrl { get; set; }

    /// <summary>
    /// Adresse du document/fichier principal (épreuve, livre...) déposé à la
    /// création (Module 8 : le contrôleur n'avait auparavant aucun champ pour
    /// recevoir le fichier envoyé par le flux de publication professeur,
    /// contrat cassé entre le frontend multipart et ce endpoint JSON).
    /// </summary>
    public string? DocumentUrl { get; set; }

    public decimal Price { get; set; }

    public bool IsPublished { get; set; } = false;
    
    public int EnrollmentCount { get; set; } = 0;
    
    public bool IsFeatured { get; set; } = false;
    
    public decimal AverageRating { get; set; } = 0;
    
    public int TotalRatings { get; set; } = 0;

    public int? DownloadCount { get; set; } = 0;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? UpdatedAt { get; set; }

    public bool IsDeleted { get; set; } = false;

    /// <summary>
    /// Professeur auteur du contenu (Module 2, US-CAT-01/US-CAT-02 : filtre
    /// "Auteur Vérifié" et compteur "X enseignants ont utilisé ce contenu").
    /// Null pour le contenu historique/administratif sans auteur attribué.
    /// </summary>
    public int? AuthorUserId { get; set; }

    /// <summary>
    /// Module 7 : score de pertinence/valeur pédagogique (0-100) produit par
    /// WinAI à partir du contenu réel du fichier (Module 8 fournit la
    /// lecture). Null tant que le contenu n'a jamais été évalué (contenu
    /// historique, ou évaluation jamais aboutie) : voir
    /// <see cref="PlatformCommissionRate"/> pour la valeur de repli.
    /// </summary>
    public decimal? WinAiScore { get; set; }

    /// <summary>Justification lisible de l'évaluation WinAI (Module 7), affichée au professeur.</summary>
    public string? WinAiJustification { get; set; }

    /// <summary>
    /// Part retenue par la plateforme sur une vente de ce contenu (0.10 à
    /// 0.60), figée à l'évaluation. C'est cette valeur  et non le score
    /// qui est lue au moment de la vente (WalletService.SyncOrderAsync), pour
    /// qu'une vente déjà enregistrée ne soit jamais recalculée si la grille
    /// change ensuite.
    /// </summary>
    public decimal? PlatformCommissionRate { get; set; }

    public DateTime? WinAiScoreEvaluatedAt { get; set; }

    // Navigation properties
    public User? Author { get; set; }
    public ICollection<CourseContent> Contents { get; set; } = new List<CourseContent>();
    
    // ✅ Mark as [JsonIgnore] to prevent circular reference in JSON serialization
    [JsonIgnore]
    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
    
    [JsonIgnore]
    public ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();
    
    [JsonIgnore]
    public ICollection<Favorite> Favorites { get; set; } = new List<Favorite>();
    
    [JsonIgnore]
    public ICollection<LearningHistory> LearningHistories { get; set; } = new List<LearningHistory>();
    
    [JsonIgnore]
    public ICollection<Certificate> Certificates { get; set; } = new List<Certificate>();
    
    [JsonIgnore]
    public ICollection<Exam> Exams { get; set; } = new List<Exam>();
    
    [JsonIgnore]
    public ICollection<Quiz> Quizzes { get; set; } = new List<Quiz>();
    
    [JsonIgnore]
    public ICollection<Revision> Revisions { get; set; } = new List<Revision>();}