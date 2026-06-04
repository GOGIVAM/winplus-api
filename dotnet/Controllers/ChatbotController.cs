using Backend.Models.DTOs;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

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
    private readonly ILogger<ChatbotController> _logger;

    public ChatbotController(IChatbotService chatbotService, ILogger<ChatbotController> logger)
    {
        _chatbotService = chatbotService;
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
}
