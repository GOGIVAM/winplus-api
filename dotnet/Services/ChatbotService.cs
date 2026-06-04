using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Repositories;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace Backend.Services;

/// <summary>
/// Interface pour le service Chatbot
/// </summary>
public interface IChatbotService
{
    Task<ChatResponse> SendMessageAsync(int userId, SendMessageRequest request);
    Task<ConversationResponse> CreateConversationAsync(int userId, CreateConversationRequest request);
    Task<PaginatedConversationsResponse> GetConversationsAsync(int userId, int page, int pageSize);
    Task<ConversationDetailResponse?> GetConversationByIdAsync(int userId, int conversationId);
    Task<ConversationResponse?> UpdateConversationAsync(int userId, int conversationId, UpdateConversationRequest request);
    Task<bool> DeleteConversationAsync(int userId, int conversationId);
    Task<MessageResponse?> AddFeedbackAsync(int userId, int messageId, MessageFeedbackRequest request);
    Task<ChatbotContextResponse?> GetContextAsync(int userId);
    Task<ChatbotContextResponse> SyncContextAsync(int userId, SyncContextRequest request);
}

/// <summary>
/// Service métier pour le chatbot
/// </summary>
public class ChatbotService : IChatbotService
{
    private readonly IChatbotRepository _repository;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ChatbotService> _logger;
    private readonly IConfiguration _configuration;

    public ChatbotService(
        IChatbotRepository repository,
        IHttpClientFactory httpClientFactory,
        ILogger<ChatbotService> logger,
        IConfiguration configuration)
    {
        _repository = repository;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Envoie un message au chatbot et reçoit une réponse
    /// </summary>
    public async Task<ChatResponse> SendMessageAsync(int userId, SendMessageRequest request)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        // Récupérer ou créer la conversation
        Conversation conversation;
        if (request.ConversationId.HasValue)
        {
            conversation = await _repository.GetConversationByIdForUserAsync(request.ConversationId.Value, userId, true)
                ?? throw new InvalidOperationException("Conversation not found");
        }
        else
        {
            // Créer une nouvelle conversation
            conversation = await _repository.CreateConversationAsync(new Conversation
            {
                UserId = userId,
                Title = GenerateConversationTitle(request.Content)
            });
        }

        // Créer le message utilisateur
        var userMessage = await _repository.CreateMessageAsync(new Message
        {
            ConversationId = conversation.Id,
            Role = "user",
            Content = request.Content,
            Attachments = request.Attachments != null ? JsonSerializer.Serialize(request.Attachments) : null
        });

        // Préparer la requête pour FastAPI/DeepSeek
        var messages = await _repository.GetMessagesForConversationAsync(conversation.Id);
        var fastapiRequest = await BuildFastApiRequestAsync(userId, messages, request.IncludeContext);

        // Appeler FastAPI/DeepSeek
        var fastapiResponse = await CallFastApiServiceAsync(fastapiRequest);
        stopwatch.Stop();

        // Créer le message assistant
        var assistantMessage = await _repository.CreateMessageAsync(new Message
        {
            ConversationId = conversation.Id,
            Role = "assistant",
            Content = fastapiResponse.Content,
            TokensUsed = fastapiResponse.TokensUsed,
            GenerationTimeMs = (int)stopwatch.ElapsedMilliseconds
        });

        _logger.LogInformation(
            "Chat message processed for user {UserId}, conversation {ConversationId}, tokens: {Tokens}, time: {Time}ms",
            userId, conversation.Id, fastapiResponse.TokensUsed, stopwatch.ElapsedMilliseconds);

        return new ChatResponse
        {
            ConversationId = conversation.Id,
            UserMessage = MapToMessageResponse(userMessage),
            AssistantMessage = MapToMessageResponse(assistantMessage),
            TotalTokensUsed = fastapiResponse.TokensUsed
        };
    }

    /// <summary>
    /// Crée une nouvelle conversation
    /// </summary>
    public async Task<ConversationResponse> CreateConversationAsync(int userId, CreateConversationRequest request)
    {
        var conversation = await _repository.CreateConversationAsync(new Conversation
        {
            UserId = userId,
            Title = request.Title,
            Tags = request.Tags != null ? JsonSerializer.Serialize(request.Tags) : null,
            Metadata = request.Metadata != null ? JsonSerializer.Serialize(request.Metadata) : null
        });

        return MapToConversationResponse(conversation);
    }

    /// <summary>
    /// Récupère les conversations paginées
    /// </summary>
    public async Task<PaginatedConversationsResponse> GetConversationsAsync(int userId, int page, int pageSize)
    {
        var conversations = await _repository.GetConversationsForUserAsync(userId, page, pageSize);
        var totalCount = await _repository.GetConversationCountForUserAsync(userId);
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        return new PaginatedConversationsResponse
        {
            Conversations = conversations.Select(MapToConversationResponse).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages,
            HasNextPage = page < totalPages,
            HasPreviousPage = page > 1
        };
    }

    /// <summary>
    /// Récupère une conversation avec ses messages
    /// </summary>
    public async Task<ConversationDetailResponse?> GetConversationByIdAsync(int userId, int conversationId)
    {
        var conversation = await _repository.GetConversationByIdForUserAsync(conversationId, userId, true);
        if (conversation == null)
        {
            return null;
        }

        return new ConversationDetailResponse
        {
            Id = conversation.Id,
            Title = conversation.Title,
            Tags = DeserializeJsonList<string>(conversation.Tags),
            IsActive = conversation.IsActive,
            LastMessageAt = conversation.LastMessageAt,
            MessageCount = conversation.MessageCount,
            CreatedAt = conversation.CreatedAt,
            UpdatedAt = conversation.UpdatedAt,
            Messages = conversation.Messages.Select(MapToMessageResponse).ToList()
        };
    }

    /// <summary>
    /// Met à jour une conversation
    /// </summary>
    public async Task<ConversationResponse?> UpdateConversationAsync(int userId, int conversationId, UpdateConversationRequest request)
    {
        var conversation = await _repository.GetConversationByIdForUserAsync(conversationId, userId);
        if (conversation == null)
        {
            return null;
        }

        if (request.Title != null)
        {
            conversation.Title = request.Title;
        }
        if (request.Tags != null)
        {
            conversation.Tags = JsonSerializer.Serialize(request.Tags);
        }
        if (request.IsActive.HasValue)
        {
            conversation.IsActive = request.IsActive.Value;
        }

        var updated = await _repository.UpdateConversationAsync(conversation);
        return MapToConversationResponse(updated);
    }

    /// <summary>
    /// Supprime une conversation (soft delete)
    /// </summary>
    public async Task<bool> DeleteConversationAsync(int userId, int conversationId)
    {
        return await _repository.DeleteConversationAsync(conversationId, userId);
    }

    /// <summary>
    /// Ajoute un feedback sur un message
    /// </summary>
    public async Task<MessageResponse?> AddFeedbackAsync(int userId, int messageId, MessageFeedbackRequest request)
    {
        var message = await _repository.GetMessageByIdAsync(messageId);
        if (message == null)
        {
            return null;
        }

        // Vérifier que l'utilisateur a accès à cette conversation
        var conversation = await _repository.GetConversationByIdForUserAsync(message.ConversationId, userId);
        if (conversation == null)
        {
            return null;
        }

        message.FeedbackRating = request.Rating;
        message.FeedbackComment = request.Comment;

        var updated = await _repository.UpdateMessageAsync(message);
        _logger.LogInformation(
            "Feedback added for message {MessageId} by user {UserId}: rating={Rating}",
            messageId, userId, request.Rating);

        return MapToMessageResponse(updated);
    }

    /// <summary>
    /// Récupère le contexte utilisateur
    /// </summary>
    public async Task<ChatbotContextResponse?> GetContextAsync(int userId)
    {
        var context = await _repository.GetContextForUserAsync(userId);
        if (context == null)
        {
            return null;
        }

        return MapToChatbotContextResponse(context);
    }

    /// <summary>
    /// Synchronise le contexte utilisateur
    /// </summary>
    public async Task<ChatbotContextResponse> SyncContextAsync(int userId, SyncContextRequest request)
    {
        var context = new ChatbotContext
        {
            UserId = userId,
            EducationLevel = request.EducationLevel,
            Grade = request.Grade,
            UserObjectives = request.Objectives != null ? JsonSerializer.Serialize(request.Objectives) : null,
            EnrolledSubjects = request.EnrolledSubjects != null ? JsonSerializer.Serialize(request.EnrolledSubjects) : null,
            RecentActivity = request.RecentActivity != null ? JsonSerializer.Serialize(request.RecentActivity) : null,
            NavigationHistory = request.NavigationHistory != null ? JsonSerializer.Serialize(request.NavigationHistory) : null,
            Preferences = request.Preferences != null ? JsonSerializer.Serialize(request.Preferences) : null
        };

        var saved = await _repository.CreateOrUpdateContextAsync(context);
        _logger.LogInformation("Context synced for user {UserId}", userId);

        return MapToChatbotContextResponse(saved);
    }

    #region Private Helper Methods

    private async Task<FastApiChatRequest> BuildFastApiRequestAsync(int userId, List<Message> messages, bool includeContext)
    {
        var request = new FastApiChatRequest
        {
            Messages = messages.Select(m => new FastApiMessage
            {
                Role = m.Role,
                Content = m.Content,
                Attachments = DeserializeJsonList<AttachmentDto>(m.Attachments)
            }).ToList(),
            MaxTokens = _configuration.GetValue<int>("Chatbot:MaxTokens", 2000),
            Temperature = _configuration.GetValue<float>("Chatbot:Temperature", 0.7f)
        };

        if (includeContext)
        {
            var context = await _repository.GetContextForUserAsync(userId);
            if (context != null)
            {
                request.UserContext = MapToChatbotContextResponse(context);
                request.SystemPrompt = BuildSystemPrompt(request.UserContext);
            }
        }

        return request;
    }

    private string BuildSystemPrompt(ChatbotContextResponse context)
    {
        var prompt = """
            Tu es un assistant pédagogique intelligent pour WinPlus, une plateforme d'apprentissage.
            Tu aides les étudiants dans leurs révisions et préparation aux concours.
            
            Directives:
            - Réponds toujours en français sauf si l'utilisateur parle une autre langue
            - Sois pédagogue et encourage l'apprentissage
            - Utilise le LaTeX pour les équations mathématiques (format $..$ ou $$..$$)
            - Adapte ton niveau de langage au niveau de l'étudiant
            - Fournis des explications claires et structurées
            """;

        if (!string.IsNullOrEmpty(context.EducationLevel))
        {
            prompt += $"\n\nL'étudiant est au niveau: {context.EducationLevel}";
        }
        if (!string.IsNullOrEmpty(context.Grade))
        {
            prompt += $", classe: {context.Grade}";
        }
        if (context.EnrolledSubjects?.Any() == true)
        {
            prompt += $"\nMatières suivies: {string.Join(", ", context.EnrolledSubjects.Select(s => s.Title))}";
        }

        return prompt;
    }

    private async Task<FastApiChatResponse> CallFastApiServiceAsync(FastApiChatRequest request)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("FastApiClient");
            var response = await client.PostAsJsonAsync("/api/chatbot/chat", request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<FastApiChatResponse>();
                return result ?? new FastApiChatResponse
                {
                    Success = false,
                    Error = "Empty response from AI service"
                };
            }

            _logger.LogError("FastApi service returned {StatusCode}", response.StatusCode);
            return new FastApiChatResponse
            {
                Success = false,
                Error = $"AI service returned {response.StatusCode}",
                Content = "Désolé, je ne peux pas répondre pour le moment. Veuillez réessayer."
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to reach FastApi service");
            return new FastApiChatResponse
            {
                Success = false,
                Error = ex.Message,
                Content = "Le service IA n'est pas disponible actuellement. Veuillez réessayer plus tard."
            };
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "FastApi service request timeout");
            return new FastApiChatResponse
            {
                Success = false,
                Error = "Request timeout",
                Content = "La requête a pris trop de temps. Veuillez réessayer avec un message plus court."
            };
        }
    }

    private string GenerateConversationTitle(string firstMessage)
    {
        // Générer un titre basé sur les premiers mots du message
        var words = firstMessage.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var title = string.Join(" ", words.Take(6));
        
        if (words.Length > 6)
        {
            title += "...";
        }

        return title.Length > 100 ? title[..100] : title;
    }

    private static MessageResponse MapToMessageResponse(Message message)
    {
        return new MessageResponse
        {
            Id = message.Id,
            ConversationId = message.ConversationId,
            Role = message.Role,
            Content = message.Content,
            Attachments = DeserializeJsonList<AttachmentDto>(message.Attachments),
            TokensUsed = message.TokensUsed,
            FeedbackRating = message.FeedbackRating,
            FeedbackComment = message.FeedbackComment,
            GenerationTimeMs = message.GenerationTimeMs,
            CreatedAt = message.CreatedAt
        };
    }

    private static ConversationResponse MapToConversationResponse(Conversation conversation)
    {
        return new ConversationResponse
        {
            Id = conversation.Id,
            Title = conversation.Title,
            Tags = DeserializeJsonList<string>(conversation.Tags),
            IsActive = conversation.IsActive,
            LastMessageAt = conversation.LastMessageAt,
            MessageCount = conversation.MessageCount,
            CreatedAt = conversation.CreatedAt,
            UpdatedAt = conversation.UpdatedAt
        };
    }

    private static ChatbotContextResponse MapToChatbotContextResponse(ChatbotContext context)
    {
        return new ChatbotContextResponse
        {
            Id = context.Id,
            UserId = context.UserId,
            EducationLevel = context.EducationLevel,
            Grade = context.Grade,
            Objectives = DeserializeJsonList<string>(context.UserObjectives),
            EnrolledSubjects = DeserializeJsonList<EnrolledSubjectDto>(context.EnrolledSubjects),
            RecentActivity = DeserializeJsonList<RecentActivityDto>(context.RecentActivity),
            NavigationHistory = DeserializeJsonList<NavigationItemDto>(context.NavigationHistory),
            Preferences = DeserializeJsonDict(context.Preferences),
            LearningStyle = context.LearningStyle,
            CreatedAt = context.CreatedAt,
            UpdatedAt = context.UpdatedAt
        };
    }

    private static List<T>? DeserializeJsonList<T>(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<T>>(json);
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, object>? DeserializeJsonDict(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object>>(json);
        }
        catch
        {
            return null;
        }
    }

    #endregion
}
