using System.ComponentModel.DataAnnotations;

namespace Backend.Models.DTOs;

#region Request DTOs

/// <summary>
/// DTO pour envoyer un message au chatbot
/// </summary>
public class SendMessageRequest
{
    /// <summary>
    /// ID de la conversation existante (optionnel pour nouvelle conv.)
    /// </summary>
    public int? ConversationId { get; set; }

    /// <summary>
    /// Contenu du message utilisateur
    /// </summary>
    [Required]
    [MinLength(1)]
    [MaxLength(10000)]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Pièces jointes (images en base64, URLs)
    /// </summary>
    public List<AttachmentDto>? Attachments { get; set; }

    /// <summary>
    /// Inclure le contexte utilisateur dans la requête
    /// </summary>
    public bool IncludeContext { get; set; } = true;
}

/// <summary>
/// DTO pour une pièce jointe
/// </summary>
public class AttachmentDto
{
    /// <summary>
    /// Type: "image", "file", "equation"
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Type { get; set; } = "image";

    /// <summary>
    /// URL ou contenu base64
    /// </summary>
    [Required]
    public string Data { get; set; } = string.Empty;

    /// <summary>
    /// Nom du fichier (optionnel)
    /// </summary>
    [MaxLength(255)]
    public string? FileName { get; set; }

    /// <summary>
    /// Type MIME
    /// </summary>
    [MaxLength(100)]
    public string? MimeType { get; set; }
}

/// <summary>
/// DTO pour créer une conversation
/// </summary>
public class CreateConversationRequest
{
    /// <summary>
    /// Titre de la conversation
    /// </summary>
    [MaxLength(255)]
    public string Title { get; set; } = "Nouvelle conversation";

    /// <summary>
    /// Tags pour catégoriser
    /// </summary>
    public List<string>? Tags { get; set; }

    /// <summary>
    /// Métadonnées additionnelles
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// DTO pour mettre à jour une conversation
/// </summary>
public class UpdateConversationRequest
{
    /// <summary>
    /// Nouveau titre
    /// </summary>
    [MaxLength(255)]
    public string? Title { get; set; }

    /// <summary>
    /// Nouveaux tags
    /// </summary>
    public List<string>? Tags { get; set; }

    /// <summary>
    /// Activer/désactiver la conversation
    /// </summary>
    public bool? IsActive { get; set; }
}

/// <summary>
/// DTO pour le feedback sur un message
/// </summary>
public class MessageFeedbackRequest
{
    /// <summary>
    /// Note: -1 (négatif), 0 (neutre), 1 (positif)
    /// </summary>
    [Range(-1, 1)]
    public int Rating { get; set; }

    /// <summary>
    /// Commentaire optionnel
    /// </summary>
    [MaxLength(1000)]
    public string? Comment { get; set; }
}

/// <summary>
/// DTO pour synchroniser le contexte utilisateur
/// </summary>
public class SyncContextRequest
{
    /// <summary>
    /// Niveau d'études
    /// </summary>
    [MaxLength(50)]
    public string? EducationLevel { get; set; }

    /// <summary>
    /// Classe/Année
    /// </summary>
    [MaxLength(50)]
    public string? Grade { get; set; }

    /// <summary>
    /// Objectifs d'apprentissage
    /// </summary>
    public List<string>? Objectives { get; set; }

    /// <summary>
    /// Matières inscrites avec progression
    /// </summary>
    public List<EnrolledSubjectDto>? EnrolledSubjects { get; set; }

    /// <summary>
    /// Activités récentes
    /// </summary>
    public List<RecentActivityDto>? RecentActivity { get; set; }

    /// <summary>
    /// Historique de navigation
    /// </summary>
    public List<NavigationItemDto>? NavigationHistory { get; set; }

    /// <summary>
    /// Préférences utilisateur
    /// </summary>
    public Dictionary<string, object>? Preferences { get; set; }
}

/// <summary>
/// DTO pour une matière inscrite
/// </summary>
public class EnrolledSubjectDto
{
    public int SubjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public decimal Progress { get; set; }
    public DateTime? LastAccessedAt { get; set; }
}

/// <summary>
/// DTO pour une activité récente
/// </summary>
public class RecentActivityDto
{
    public string Type { get; set; } = string.Empty;
    public int? SubjectId { get; set; }
    public string? SubjectTitle { get; set; }
    public int? ContentId { get; set; }
    public decimal? Score { get; set; }
    public DateTime At { get; set; }
}

/// <summary>
/// DTO pour un élément de navigation
/// </summary>
public class NavigationItemDto
{
    public string Path { get; set; } = string.Empty;
    public string? Title { get; set; }
    public DateTime At { get; set; }
}

#endregion

#region Response DTOs

/// <summary>
/// DTO de réponse pour un message
/// </summary>
public class MessageResponse
{
    public int Id { get; set; }
    public int ConversationId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public List<AttachmentDto>? Attachments { get; set; }
    public int? TokensUsed { get; set; }
    public int? FeedbackRating { get; set; }
    public string? FeedbackComment { get; set; }
    public int? GenerationTimeMs { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// DTO de réponse après envoi d'un message
/// </summary>
public class ChatResponse
{
    /// <summary>
    /// ID de la conversation
    /// </summary>
    public int ConversationId { get; set; }

    /// <summary>
    /// Message utilisateur
    /// </summary>
    public MessageResponse UserMessage { get; set; } = null!;

    /// <summary>
    /// Réponse de l'assistant
    /// </summary>
    public MessageResponse AssistantMessage { get; set; } = null!;

    /// <summary>
    /// Tokens totaux utilisés
    /// </summary>
    public int TotalTokensUsed { get; set; }
}

/// <summary>
/// DTO de réponse pour une conversation
/// </summary>
public class ConversationResponse
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public List<string>? Tags { get; set; }
    public bool IsActive { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public int MessageCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Aperçu du dernier message
    /// </summary>
    public string? LastMessagePreview { get; set; }
}

/// <summary>
/// DTO de réponse détaillée pour une conversation avec messages
/// </summary>
public class ConversationDetailResponse : ConversationResponse
{
    public List<MessageResponse> Messages { get; set; } = new();
}

/// <summary>
/// DTO de réponse paginée pour les conversations
/// </summary>
public class PaginatedConversationsResponse
{
    public List<ConversationResponse> Conversations { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public bool HasNextPage { get; set; }
    public bool HasPreviousPage { get; set; }
}

/// <summary>
/// DTO de réponse pour le contexte utilisateur
/// </summary>
public class ChatbotContextResponse
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string? EducationLevel { get; set; }
    public string? Grade { get; set; }
    public List<string>? Objectives { get; set; }
    public List<EnrolledSubjectDto>? EnrolledSubjects { get; set; }
    public List<RecentActivityDto>? RecentActivity { get; set; }
    public List<NavigationItemDto>? NavigationHistory { get; set; }
    public Dictionary<string, object>? Preferences { get; set; }
    public string? LearningStyle { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

#endregion

#region Internal DTOs (Communication FastApi)

/// <summary>
/// DTO pour la requête vers FastApi/DeepSeek
/// </summary>
public class FastApiChatRequest
{
    public List<FastApiMessage> Messages { get; set; } = new();
    public string? SystemPrompt { get; set; }
    public ChatbotContextResponse? UserContext { get; set; }
    public int MaxTokens { get; set; } = 2000;
    public float Temperature { get; set; } = 0.7f;
}

/// <summary>
/// DTO pour un message dans la requête FastApi
/// </summary>
public class FastApiMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public List<AttachmentDto>? Attachments { get; set; }
}

/// <summary>
/// DTO pour la réponse de FastApi/DeepSeek
/// </summary>
public class FastApiChatResponse
{
    public string Content { get; set; } = string.Empty;
    public int TokensUsed { get; set; }
    public int GenerationTimeMs { get; set; }
    public string? Model { get; set; }
    public bool Success { get; set; } = true;
    public string? Error { get; set; }
}

#endregion
