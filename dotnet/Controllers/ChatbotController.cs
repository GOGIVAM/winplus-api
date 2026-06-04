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
    public async Task<ActionResult<ChatResponse>> SendMessage([FromBody] SendMessageRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Content))
            {
                return BadRequest(new { error = "Message content is required" });
            }

            var userId = GetCurrentUserId();
            var response = await _chatbotService.SendMessageAsync(userId, request);
            
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
    [ProducesResponseType(typeof(ChatbotContextResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ChatbotContextResponse>> GetContext()
    {
        try
        {
            var userId = GetCurrentUserId();
            var context = await _chatbotService.GetContextAsync(userId);
            
            if (context == null)
            {
                return NotFound(new { error = "Context not found. Sync context first." });
            }

            return Ok(context);
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
    /// POST /api/chatbot/stream
    /// SSE — crée la conversation/message utilisateur, proxie le stream FastAPI, ré-émet les chunks.
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

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            Response.StatusCode = 400;
            await Response.WriteAsync("data: {\"error\": \"Message is required\"}\n\ndata: [DONE]\n\n", cancellationToken);
            return;
        }

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

        // Save user message
        _dbContext.Messages.Add(new Message
        {
            ConversationId = conversationId,
            Role = "user",
            Content = request.Message,
            CreatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync(cancellationToken);

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
        var history = await _dbContext.Messages
            .Where(m => m.ConversationId == conversationId && !m.IsDeleted)
            .OrderByDescending(m => m.CreatedAt)
            .Take(20)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new { role = m.Role, content = m.Content })
            .ToListAsync(cancellationToken);

        // Forward request to FastAPI stream endpoint
        var fastApiBody = new
        {
            messages = history,
            conversation_id = conversationId,
            max_tokens = 2000,
            temperature = 0.7
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
}
