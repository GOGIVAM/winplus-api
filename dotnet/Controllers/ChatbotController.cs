using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace Backend.Controllers;

/// <summary>
/// Contrôleur API pour le chatbot intelligent WinPlus
/// </summary>
[ApiController]
[Route("api/chatbot")]
[Authorize]
public class ChatbotController : ControllerBase
{
    private readonly IChatbotService _chatbotService;
    private readonly ApplicationDbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ChatbotController> _logger;

    public ChatbotController(
        IChatbotService chatbotService,
        ApplicationDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        ILogger<ChatbotController> logger)
    {
        _chatbotService = chatbotService;
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Vérifie si l'utilisateur a encore des tokens disponibles et en déduit un.
    /// Retourne (allowed: bool, tokensLeft: int).
    /// </summary>
    private async Task<(bool Allowed, int TokensLeft)> CheckAndDeductTokenAsync(int userId)
    {
        const int LibreMonthlyTokens = 0;
        const int StandardMonthlyTokens = 500;
        const int PremiumMonthlyTokens = 2000;
        const int FamilleMonthlyTokens = 3000;

        var sub = await _dbContext.Subscriptions
            .FirstOrDefaultAsync(s => s.UserId == userId && s.IsActive);

        if (sub == null)
            return (false, 0); // Pas d'abonnement actif

        // Réinitialisation mensuelle
        var now = DateTime.UtcNow;
        if (sub.TokensResetAt == null || sub.TokensResetAt < now.AddMonths(-1))
        {
            sub.TokensUsedThisMonth = 0;
            sub.TokensResetAt = now;
        }

        int monthlyLimit = sub.PlanName?.ToLower() switch
        {
            var p when p != null && p.Contains("famille") => FamilleMonthlyTokens,
            var p when p != null && p.Contains("premium") => PremiumMonthlyTokens,
            var p when p != null && p.Contains("standard") => StandardMonthlyTokens,
            _ => LibreMonthlyTokens,
        };

        if (monthlyLimit == 0)
            return (false, 0); // Plan libre → pas d'accès IA

        if (sub.TokensUsedThisMonth >= monthlyLimit)
            return (false, 0); // Quota épuisé

        sub.TokensUsedThisMonth += 1;
        await _dbContext.SaveChangesAsync();

        return (true, monthlyLimit - sub.TokensUsedThisMonth);
    }

    /// <summary>
    /// Récupère l'ID utilisateur depuis les claims JWT
    /// </summary>
    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst("userId") ?? User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
        {
            throw new UnauthorizedAccessException("User ID not found in token");
        }
        return userId;
    }

    /// <summary>
    /// POST /api/chatbot/message
    /// Envoie un message au chatbot et reçoit une réponse
    /// </summary>
    [HttpPost("message")]
    [ProducesResponseType(typeof(ChatResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ChatResponse>> SendMessage([FromBody] Backend.Models.DTOs.SendMessageRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Content))
            {
                return BadRequest(new { error = "Message content is required" });
            }

            var userId = GetCurrentUserId();

            var (allowed, tokensLeft) = await CheckAndDeductTokenAsync(userId);
            if (!allowed)
            {
                return StatusCode(402, new
                {
                    error = "quota_exceeded",
                    message = "Votre quota de tokens IA est épuisé pour ce mois. Passez à un plan supérieur.",
                    tokensLeft = 0,
                });
            }

            var response = await _chatbotService.SendMessageAsync(userId, request);

            Response.Headers.Append("X-Tokens-Left", tokensLeft.ToString());
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation in SendMessage");
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SendMessage");
            return StatusCode(500, new { error = "An error occurred while processing your message" });
        }
    }

    /// <summary>
    /// POST /api/chatbot/conversations
    /// Crée une nouvelle conversation
    /// </summary>
    [HttpPost("conversations")]
    [ProducesResponseType(typeof(ConversationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ConversationResponse>> CreateConversation([FromBody] CreateConversationRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            var conversation = await _chatbotService.CreateConversationAsync(userId, request);
            
            return CreatedAtAction(nameof(GetConversation), new { id = conversation.Id }, conversation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CreateConversation");
            return StatusCode(500, new { error = "An error occurred while creating the conversation" });
        }
    }

    /// <summary>
    /// GET /api/chatbot/conversations
    /// Récupère la liste des conversations de l'utilisateur (paginée)
    /// </summary>
    [HttpGet("conversations")]
    [ProducesResponseType(typeof(PaginatedConversationsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PaginatedConversationsResponse>> GetConversations(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 20;

            var userId = GetCurrentUserId();
            var result = await _chatbotService.GetConversationsAsync(userId, page, pageSize);
            
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetConversations");
            return StatusCode(500, new { error = "An error occurred while retrieving conversations" });
        }
    }

    /// <summary>
    /// GET /api/chatbot/conversations/{id}
    /// Récupère une conversation spécifique avec ses messages
    /// </summary>
    [HttpGet("conversations/{id:int}")]
    [ProducesResponseType(typeof(ConversationDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ConversationDetailResponse>> GetConversation(int id)
    {
        try
        {
            var userId = GetCurrentUserId();
            var conversation = await _chatbotService.GetConversationByIdAsync(userId, id);
            
            if (conversation == null)
            {
                return NotFound(new { error = "Conversation not found" });
            }

            return Ok(conversation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetConversation");
            return StatusCode(500, new { error = "An error occurred while retrieving the conversation" });
        }
    }

    /// <summary>
    /// PATCH /api/chatbot/conversations/{id}
    /// Met à jour une conversation (titre, tags, état)
    /// </summary>
    [HttpPatch("conversations/{id:int}")]
    [ProducesResponseType(typeof(ConversationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ConversationResponse>> UpdateConversation(
        int id,
        [FromBody] UpdateConversationRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            var conversation = await _chatbotService.UpdateConversationAsync(userId, id, request);
            
            if (conversation == null)
            {
                return NotFound(new { error = "Conversation not found" });
            }

            return Ok(conversation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in UpdateConversation");
            return StatusCode(500, new { error = "An error occurred while updating the conversation" });
        }
    }

    /// <summary>
    /// DELETE /api/chatbot/conversations/{id}
    /// Supprime une conversation (soft delete)
    /// </summary>
    [HttpDelete("conversations/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteConversation(int id)
    {
        try
        {
            var userId = GetCurrentUserId();
            var deleted = await _chatbotService.DeleteConversationAsync(userId, id);
            
            if (!deleted)
            {
                return NotFound(new { error = "Conversation not found" });
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in DeleteConversation");
            return StatusCode(500, new { error = "An error occurred while deleting the conversation" });
        }
    }

    /// <summary>
    /// POST /api/chatbot/messages/{id}/feedback
    /// Ajoute un feedback sur un message (like/dislike)
    /// </summary>
    [HttpPost("messages/{id:int}/feedback")]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<MessageResponse>> AddFeedback(
        int id,
        [FromBody] MessageFeedbackRequest request)
    {
        try
        {
            if (request.Rating < -1 || request.Rating > 1)
            {
                return BadRequest(new { error = "Rating must be -1, 0, or 1" });
            }

            var userId = GetCurrentUserId();
            var message = await _chatbotService.AddFeedbackAsync(userId, id, request);
            
            if (message == null)
            {
                return NotFound(new { error = "Message not found or access denied" });
            }

            return Ok(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in AddFeedback");
            return StatusCode(500, new { error = "An error occurred while adding feedback" });
        }
    }

    /// <summary>
    /// GET /api/chatbot/context
    /// Récupère le contexte utilisateur pour le chatbot
    /// </summary>
    [HttpGet("context")]
    // Le front interroge cette route au chargement de l'application, avant toute
    // connexion. Un contexte vide est une réponse valide : on répond 200 au lieu
    // de polluer les logs avec un 401 « Bearer MISSING » à chaque visite.
    [AllowAnonymous]
    [ProducesResponseType(typeof(ChatbotContextResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChatbotContextResponse>> GetContext()
    {
        try
        {
            if (User?.Identity?.IsAuthenticated != true)
                return Ok(new ChatbotContextResponse());

            var userId = GetCurrentUserId();
            var context = await _chatbotService.GetContextAsync(userId);

            // Aucun contexte encore enregistré : on le construit à la volée depuis
            // les données réelles de l'utilisateur, au lieu de renvoyer un 404.
            // L'ancien comportement faisait échouer l'appel pour tout nouvel
            // utilisateur, à chaque ouverture de l'application.
            if (context == null)
            {
                try
                {
                    context = await _chatbotService.SyncContextAsync(userId, new SyncContextRequest());
                }
                catch (Exception syncEx)
                {
                    _logger.LogWarning(syncEx, "Auto-sync du contexte impossible pour {UserId}", userId);
                }
            }

            // Toujours 200 : un contexte vide est un état valide, pas une erreur.
            return Ok(context ?? new ChatbotContextResponse());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetContext");
            return StatusCode(500, new { error = "An error occurred while retrieving context" });
        }
    }

    /// <summary>
    /// POST /api/chatbot/context/sync
    /// Synchronise le contexte utilisateur (niveau, matières, activités)
    /// </summary>
    [HttpPost("context/sync")]
    [ProducesResponseType(typeof(ChatbotContextResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ChatbotContextResponse>> SyncContext([FromBody] SyncContextRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            var context = await _chatbotService.SyncContextAsync(userId, request);

            return Ok(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SyncContext");
            return StatusCode(500, new { error = "An error occurred while syncing context" });
        }
    }

    /// <summary>
    /// Rend un document joint exploitable par le modèle.
    ///
    /// Les formats texte (txt, csv, md, json) sont décodés et insérés tels
    /// quels. Les formats binaires (PDF, docx, xlsx) demandent une extraction
    /// dédiée : voir la note ci-dessous. En attendant, le nom et le type sont
    /// annoncés au modèle, ce qui vaut mieux que de laisser croire qu'aucun
    /// fichier n'a été envoyé.
    ///
    /// TODO extraction PDF : ajouter le paquet PdfPig puis, pour
    /// mimeType == "application/pdf" :
    ///     using var pdf = UglyToad.PdfPig.PdfDocument.Open(bytes);
    ///     var text = string.Join("\n", pdf.GetPages().Select(p => p.Text));
    /// Même principe pour .docx avec DocumentFormat.OpenXml.
    /// </summary>
    private static string DescribeDocument(StreamAttachment att)
    {
        const int MaxChars = 20_000; // garde-fou sur la fenêtre de contexte
        var name = string.IsNullOrWhiteSpace(att.FileName) ? "document" : att.FileName;
        var mime = att.MimeType ?? "application/octet-stream";

        var textual = mime.StartsWith("text/")
            || mime is "application/json" or "application/csv" or "text/csv";

        if (!textual)
            return $"[Pièce jointe : {name} ({mime}). Le contenu binaire n'est pas encore extrait côté serveur — demande à l'élève de recopier le passage utile, ou de joindre une photo de la page.]";

        try
        {
            // Le front envoie une data URL : data:<mime>;base64,<payload>
            var payload = att.Data;
            var comma = payload.IndexOf(',');
            if (payload.StartsWith("data:") && comma > 0)
                payload = payload[(comma + 1)..];

            var text = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            if (text.Length > MaxChars)
                text = text[..MaxChars] + "\n[…document tronqué]";

            return $"[Contenu du fichier joint « {name} » ({mime})]\n{text}";
        }
        catch (Exception)
        {
            return $"[Pièce jointe : {name} ({mime}) — contenu illisible.]";
        }
    }

    /// <summary>
    /// POST /api/chatbot/stream
    /// SSE  crée la conversation/message utilisateur, proxie le stream FastAPI, ré-émet les chunks.
    /// </summary>
    [HttpPost("stream")]
    public async Task StreamChat([FromBody] StreamChatRequest request, CancellationToken cancellationToken)
    {
        int userId;
        try { userId = GetCurrentUserId(); }
        catch
        {
            Response.StatusCode = 401;
            await Response.WriteAsync("data: {\"error\": \"Unauthorized\"}\n\ndata: [DONE]\n\n", cancellationToken);
            return;
        }

        // Envoyer une image seule, sans légende, est un usage normal : on ne
        // refuse que si le message ET les pièces jointes sont vides.
        var hasAttachments = request.Attachments?.Count > 0;
        if (string.IsNullOrWhiteSpace(request.Message) && !hasAttachments)
        {
            Response.StatusCode = 400;
            await Response.WriteAsync("data: {\"error\": \"Message is required\"}\n\ndata: [DONE]\n\n", cancellationToken);
            return;
        }
        if (string.IsNullOrWhiteSpace(request.Message))
            request.Message = "Analyse le document ci-joint.";

        // Create or retrieve conversation
        int conversationId = request.ConversationId ?? 0;
        bool isNew = conversationId == 0;

        if (isNew)
        {
            var title = request.Message.Length > 50 ? request.Message[..50] + "…" : request.Message;
            var conv = new Conversation
            {
                UserId = userId,
                Title = title,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _dbContext.Conversations.Add(conv);
            await _dbContext.SaveChangesAsync(cancellationToken);
            conversationId = conv.Id;
        }

        // Save user message (text only in DB; images stay in-memory for this request)
        var userMsg = new Message
        {
            ConversationId = conversationId,
            Role = "user",
            Content = request.Message,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Messages.Add(userMsg);
        await _dbContext.SaveChangesAsync(cancellationToken);
        var savedMsgId = userMsg.Id;

        // Setup SSE response headers
        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        // Emit conversationId to frontend on new conversation
        if (isNew)
        {
            await Response.WriteAsync($"data: {{\"conversationId\": {conversationId}}}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }

        // Fetch last 20 messages for conversation context
        var historyRaw = await _dbContext.Messages
            .Where(m => m.ConversationId == conversationId && !m.IsDeleted)
            .OrderByDescending(m => m.CreatedAt)
            .Take(20)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new { m.Id, role = m.Role, content = m.Content })
            .ToListAsync(cancellationToken);

        // Injection des pièces jointes dans le message utilisateur courant.
        //
        // Avant : seules les pièces de type "image" étaient transmises. Le
        // composer du front crée des pièces de type "document" pour tout ce qui
        // n'est pas une image (PDF, docx, csv…) : elles étaient donc jetées
        // silencieusement et le modèle répondait comme si aucun fichier n'avait
        // été envoyé — c'est le « le chatbot n'upload pas les fichiers ».
        var images    = request.Attachments?.Where(a => a.Type == "image").ToList()    ?? new();
        var documents = request.Attachments?.Where(a => a.Type != "image").ToList()    ?? new();
        var hasAny    = images.Count > 0 || documents.Count > 0;

        var history = historyRaw.Select<dynamic, object>(h =>
        {
            if ((int)h.Id != savedMsgId || !hasAny)
                return new { role = (string)h.role, content = (object)(string)h.content };

            var parts = new List<object>();
            if (!string.IsNullOrEmpty((string)h.content))
                parts.Add(new { type = "text", text = (string)h.content });

            foreach (var att in images)
                parts.Add(new { type = "image_url", image_url = new { url = att.Data } });

            foreach (var att in documents)
                parts.Add(new { type = "text", text = DescribeDocument(att) });

            return new { role = (string)h.role, content = (object)parts };
        }).ToList();

        // Forward request to FastAPI stream endpoint
        var fastApiBody = new
        {
            messages = history,
            conversation_id = conversationId,
            max_tokens = 2000,
            temperature = 0.7,
            user_context = string.IsNullOrEmpty(request.ForceLanguage) ? null : new
            {
                force_language = request.ForceLanguage
            }
        };

        var httpClient = _httpClientFactory.CreateClient("FastApiClient");
        using var fastApiReq = new HttpRequestMessage(HttpMethod.Post, "/api/chatbot/stream");
        fastApiReq.Content = JsonContent.Create(fastApiBody);

        var authHeader = HttpContext.Request.Headers["Authorization"].ToString();
        if (!string.IsNullOrEmpty(authHeader))
            fastApiReq.Headers.TryAddWithoutValidation("Authorization", authHeader);

        try
        {
            using var fastApiRes = await httpClient.SendAsync(
                fastApiReq, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!fastApiRes.IsSuccessStatusCode)
            {
                _logger.LogError("FastAPI stream returned {Status} for user {UserId}", fastApiRes.StatusCode, userId);
                await Response.WriteAsync("data: {\"error\": \"AI service unavailable\"}\n\ndata: [DONE]\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
                return;
            }

            using var stream = await fastApiRes.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new System.IO.StreamReader(stream);

            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!line.StartsWith("data: ")) continue;

                await Response.WriteAsync(line + "\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);

                if (line[6..] == "[DONE]") break;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Stream cancelled for user {UserId} on conv {ConvId}", userId, conversationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stream proxy error for user {UserId}", userId);
            try
            {
                await Response.WriteAsync("data: {\"error\": \"Stream error\"}\n\ndata: [DONE]\n\n");
                await Response.Body.FlushAsync(CancellationToken.None);
            }
            catch { }
        }
        finally
        {
            // Update conversation metadata
            try
            {
                var conv = await _dbContext.Conversations.FindAsync(new object[] { conversationId }, CancellationToken.None);
                if (conv != null)
                {
                    conv.LastMessageAt = DateTime.UtcNow;
                    conv.UpdatedAt = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync(CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update conversation metadata for conv {ConvId}", conversationId);
            }
        }
    }

    /// <summary>
    /// GET /api/chatbot/memories
    /// Liste les mémoires WinAI persistantes de l'utilisateur connecté.
    /// </summary>
    [HttpGet("memories")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMemories()
    {
        try
        {
            var userId = GetCurrentUserId();
            var memories = await _dbContext.UserAIMemories
                .Where(m => m.UserId == userId)
                .OrderByDescending(m => m.UpdatedAt)
                .Select(m => new
                {
                    id = m.Id,
                    type = m.MemoryType,
                    content = m.Content,
                    createdAt = m.CreatedAt,
                    updatedAt = m.UpdatedAt,
                })
                .ToListAsync();
            return Ok(memories);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetMemories");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// DELETE /api/chatbot/memories/{id}
    /// Supprime une mémoire WinAI appartenant à l'utilisateur connecté.
    /// </summary>
    [HttpDelete("memories/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteMemory(int id)
    {
        try
        {
            var userId = GetCurrentUserId();
            var memory = await _dbContext.UserAIMemories
                .FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);
            if (memory == null)
                return NotFound(new { error = "Memory not found" });
            _dbContext.UserAIMemories.Remove(memory);
            await _dbContext.SaveChangesAsync();
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in DeleteMemory");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}
