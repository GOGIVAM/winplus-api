namespace Backend.Models.DTOs;

// ── Lot 9.6 (suivi 17.5) : support de séance généré par WinAI ─────────────
//
// Contenu structuré stocké en JSON (TutorSessionMaterial.ContentJson /
// SharedContentJson) et renvoyé tel quel aux clients. Aucun champ ne
// s'appelle « data » : le client web déballe automatiquement une enveloppe
// { data: … } (services/api.ts).

/// <summary>Support de séance : cours structuré, quiz et exercices.</summary>
public class TutorSessionMaterialContent
{
    public string Title { get; set; } = "";
    public string? Subject { get; set; }
    public string? Level { get; set; }
    public List<string> Objectives { get; set; } = new();
    public TutorSessionMaterialCourse Course { get; set; } = new();
    public List<TutorSessionMaterialQuizQuestion> Quiz { get; set; } = new();
    public List<TutorSessionMaterialExercise> Exercises { get; set; } = new();
    /// <summary>Séance de groupe : comment le support s'adapte au niveau du groupe.</summary>
    public string? GroupNote { get; set; }
}

public class TutorSessionMaterialCourse
{
    public string Introduction { get; set; } = "";
    public List<TutorSessionMaterialSection> Sections { get; set; } = new();
    public string Summary { get; set; } = "";
    public List<string> KeyPoints { get; set; } = new();
}

public class TutorSessionMaterialSection
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string? Example { get; set; }
}

public class TutorSessionMaterialQuizOption
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
}

public class TutorSessionMaterialQuizQuestion
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public List<TutorSessionMaterialQuizOption> Options { get; set; } = new();
    /// <summary>Masqué (null) pour un élève qui n'a pas encore répondu et pour le payeur.</summary>
    public string? CorrectOptionId { get; set; }
    /// <summary>Masqué comme la bonne réponse.</summary>
    public string? Explanation { get; set; }
}

public class TutorSessionMaterialExercise
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Statement { get; set; } = "";
    /// <summary>facile | moyen | difficile.</summary>
    public string Difficulty { get; set; } = "moyen";
    public List<string> Hints { get; set; } = new();
    /// <summary>Corrigé, masqué tant que l'élève n'a pas rendu ses réponses.</summary>
    public string? Solution { get; set; }
}

/// <summary>Génération (ou relance) par le tuteur.</summary>
public class GenerateTutorSessionMaterialRequestDto
{
    /// <summary>Consigne facultative pour orienter la génération (« plus d'exercices », « niveau plus simple »…).</summary>
    public string? Instructions { get; set; }
}

/// <summary>Modification de la copie de travail par le tuteur.</summary>
public class UpdateTutorSessionMaterialRequestDto
{
    public TutorSessionMaterialContent Content { get; set; } = new();
}

/// <summary>Réponses d'un élève : quiz { idQuestion: idOption } et exercices { idExercice: texte }.</summary>
public class SubmitTutorSessionMaterialRequestDto
{
    public Dictionary<string, string> QuizAnswers { get; set; } = new();
    public Dictionary<string, string> ExerciseAnswers { get; set; } = new();
}

public class TutorSessionMaterialSettingsDto
{
    public bool AutoGenerateOnAccept { get; set; }
}

/// <summary>Correction d'une question pour l'élève (après envoi).</summary>
public class TutorSessionMaterialQuestionResultDto
{
    public string QuestionId { get; set; } = "";
    public string? Answer { get; set; }
    public string? CorrectOptionId { get; set; }
    public bool IsCorrect { get; set; }
}

public class TutorSessionMaterialResultDto
{
    public int StudentUserId { get; set; }
    public string? StudentName { get; set; }
    public int MaterialVersion { get; set; }
    public int QuizScore { get; set; }
    public int QuizTotal { get; set; }
    public DateTime SubmittedAt { get; set; }
    /// <summary>Réponse donnée à une version plus ancienne que la version partagée.</summary>
    public bool IsOutdated { get; set; }
    /// <summary>Détail question par question (tuteur et élève lui-même).</summary>
    public List<TutorSessionMaterialQuestionResultDto> Questions { get; set; } = new();
    /// <summary>Réponses rédigées aux exercices (tuteur et élève lui-même).</summary>
    public Dictionary<string, string> ExerciseAnswers { get; set; } = new();
}

/// <summary>Vue du support pour le compte connecté (tuteur, élève participant ou payeur).</summary>
public class TutorSessionMaterialViewDto
{
    public int BookingId { get; set; }
    /// <summary>tutor | participant | payer.</summary>
    public string ViewerRole { get; set; } = "";
    public string BookingStatus { get; set; } = "";
    public string? Subject { get; set; }
    public List<string> Topics { get; set; } = new();
    public DateOnly SessionDate { get; set; }
    public string StartTime { get; set; } = "";
    public string EndTime { get; set; } = "";
    public int DurationMinutes { get; set; }
    public string? TutorName { get; set; }
    public int ParticipantCount { get; set; }
    public List<TutorBookingParticipantDto> Participants { get; set; } = new();
    public int? TutorEngagementId { get; set; }

    /// <summary>Un support existe et est visible du compte connecté.</summary>
    public bool Exists { get; set; }
    /// <summary>draft | shared (tuteur) ; shared pour les autres.</summary>
    public string? Status { get; set; }
    public int? Version { get; set; }
    public int? SharedVersion { get; set; }
    /// <summary>Tuteur : la copie de travail a changé depuis le dernier partage.</summary>
    public bool HasUnsharedChanges { get; set; }
    public int GenerationCount { get; set; }
    public bool AutoGenerated { get; set; }
    public DateTime? GeneratedAt { get; set; }
    public DateTime? SharedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Tuteur : copie de travail ; élève et payeur : version partagée (corrigés masqués si besoin).</summary>
    public TutorSessionMaterialContent? Content { get; set; }
    /// <summary>Les bonnes réponses et corrigés sont inclus dans <see cref="Content"/>.</summary>
    public bool SolutionsIncluded { get; set; }

    public bool CanGenerate { get; set; }
    public bool CanEdit { get; set; }
    public bool CanShare { get; set; }
    public bool CanSubmit { get; set; }
    /// <summary>Pourquoi la génération ou le partage n'est pas possible (statut de la séance).</summary>
    public string? BlockedReason { get; set; }

    /// <summary>Élève : son résultat (correction comprise).</summary>
    public TutorSessionMaterialResultDto? MyResult { get; set; }
    /// <summary>Tuteur : résultats de tous les participants ; parent ou institution payeur : notes des participants.</summary>
    public List<TutorSessionMaterialResultDto> Results { get; set; } = new();
}
