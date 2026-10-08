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
    private readonly IAiQuotaService _aiQuota;
    private readonly ITokenTopUpService _topUp;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ChatbotController> _logger;

    public ChatbotController(
        IChatbotService chatbotService,
        ApplicationDbContext dbContext,
        IAiQuotaService aiQuota,
        ITokenTopUpService topUp,
        IHttpClientFactory httpClientFactory,
        ILogger<ChatbotController> logger)
    {
        _chatbotService = chatbotService;
        _dbContext = dbContext;
        _aiQuota = aiQuota;
        _topUp = topUp;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Corps de la réponse 402 (décisions 8.5 et 8.10), partagé par le chemin
    /// REST (<see cref="SendMessage"/>) et le chemin streaming
    /// (<see cref="StreamChat"/>) : même structure dans les deux cas.
    ///
    /// - <c>limit</c> : « session » (5 h glissantes) ou « week » (7 jours
    ///   glissants) quelle limite bloque.
    /// - <c>resetsAt</c> : horodatage UTC ISO-8601 auquel l'utilisateur peut
    ///   de nouveau écrire ; <c>message</c> le reprend en toutes lettres.
    /// - Jamais de nombre de tokens (8.3). <c>tokensLeft</c> reste à 0 pour
    ///   compatibilité de forme avec les anciens clients.
    /// - <c>topUpAvailable</c> reste faux tant que le wallet du Module 1/14
    ///   n'existe pas (voir <see cref="ITokenTopUpService"/>).
    /// </summary>
    private object QuotaExceededBody(AiQuotaDecision decision)
    {
        var kind = decision.LimitKind ?? AiLimitKind.Week;
        return new
        {
            error = "quota_exceeded",
            limit = kind == AiLimitKind.Session ? "session" : "week",
            resetsAt = decision.ResetsAt,
            message = AiUsagePolicy.LimitMessage(kind, decision.ResetsAt),
            tokensLeft = 0,
            topUpAvailable = _topUp.IsAvailable,
            topUpMessage = _topUp.IsAvailable ? null : _topUp.UnavailableReason,
        };
    }

    /// <summary>
    /// Corps 503 quand le journal de quota est indisponible (table absente :
    /// script SQL non exécuté). Refus propre au lieu d'un 500 opaque (point C).
    /// </summary>
    private static object QuotaUnavailableBody() => new
    {
        error = "quota_unavailable",
        message = "WinAI est momentanément indisponible (maintenance en cours). Réessayez dans quelques minutes.",
    };

    /// <summary>
    /// Point E : une conversation fournie par le client n'est utilisable que
    /// si elle appartient à l'utilisateur authentifié et n'est pas supprimée
    /// (même règle que <c>ChatbotRepository.GetConversationByIdForUserAsync</c>).
    /// </summary>
    private Task<bool> ConversationBelongsToUserAsync(int conversationId, int userId, CancellationToken ct) =>
        _dbContext.Conversations
            .AsNoTracking()
            .AnyAsync(c => c.Id == conversationId && c.UserId == userId && !c.IsDeleted, ct);

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

            // Point E : la conversation doit appartenir à l'utilisateur, et
            // c'est vérifié AVANT toute réserve de quota.
            if (request.ConversationId is int requestedConvId
                && !await ConversationBelongsToUserAsync(requestedConvId, userId, HttpContext.RequestAborted))
                return NotFound(new { error = "Conversation not found" });

            // 8.7 : même ClientMessageId que le flux SSE quand ce POST est le
            // repli REST (ou un « Réessayer ») du même message utilisateur —
            // la réserve encore ouverte est rattachée, sans second décompte.
            var quota = await _aiQuota.CheckAndReserveAsync(userId, request.ClientMessageId, "rest");
            if (quota.Outcome == AiQuotaOutcome.Unavailable)
                return StatusCode(503, QuotaUnavailableBody());
            if (!quota.Allowed)
                return StatusCode(402, QuotaExceededBody(quota));

            // Point D : quoi qu'il arrive entre la réserve et la fin du
            // traitement (exception, FastAPI en échec), le bloc finally décide
            // de la finaliser ou de la libérer jamais de réserve orpheline.
            ChatResponse? response = null;
            try
            {
                response = await _chatbotService.SendMessageAsync(userId, request, quota.ReservationId);
                return Ok(response);
            }
            finally
            {
                if (response is not null && response.AiServiceSucceeded)
                {
                    // 8.1 : coût réel remonté par le fournisseur ; à défaut
                    // (usage absent), estimation sur la réponse servie.
                    var tokens = response.TotalTokensUsed > 0
                        ? response.TotalTokensUsed
                        : AiUsagePolicy.EstimateServedTokens(response.AssistantMessage?.Content?.Length ?? 0);
                    await _aiQuota.FinalizeUsageAsync(userId, quota.ReservationId, tokens, CancellationToken.None);
                }
                else
                {
                    // Aucune réponse réelle servie (exception, ou FastAPI en
                    // échec : ChatbotService renvoie alors un contenu de
                    // repli sans lever). La réserve est rendue seulement si
                    // CETTE tentative la détient encore.
                    await _aiQuota.ReleaseReservationAsync(userId, quota.ReservationId, quota.AttemptId, CancellationToken.None);
                }
            }
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
    private class ChatAttachmentResult
    {
        public string? TextForPrompt { get; set; }
    }

    private static readonly JsonSerializerOptions _snakeCaseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>
    /// Décrit une pièce jointe non textuelle (PDF, docx...) pour l'injecter
    /// dans le message envoyé à DeepSeek. RAG (voir topo validé : "tout
    /// document uploadé doit servir dans la base de connaissance") :
    /// délègue l'extraction réelle à Python (PyMuPDF/OCR, .NET n'a pas
    /// d'équivalent) via POST /api/rag/chat-attachment, qui renvoie un
    /// aperçu texte immédiat ET planifie l'ingestion complète en tâche de
    /// fond dans la base de connaissance PERSONNELLE de l'utilisateur.
    /// Avant : un PDF, même natif et parfaitement lisible, recevait
    /// toujours "contenu non extrait"  jamais lu.
    /// </summary>
    private async Task<string> DescribeDocumentAsync(StreamAttachment att)
    {
        const int MaxChars = 20_000; // garde-fou sur la fenêtre de contexte
        var name = string.IsNullOrWhiteSpace(att.FileName) ? "document" : att.FileName;
        var mime = att.MimeType ?? "application/octet-stream";

        var textual = mime.StartsWith("text/")
            || mime is "application/json" or "application/csv" or "text/csv";

        if (!textual)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("FastApiClient");
                using var req = new HttpRequestMessage(HttpMethod.Post, "/api/rag/chat-attachment")
                {
                    Content = JsonContent.Create(
                        new { data_url_or_base64 = att.Data, file_name = att.FileName },
                        options: _snakeCaseJsonOptions),
                };
                var authHeader = Request.Headers["Authorization"].ToString();
                if (!string.IsNullOrEmpty(authHeader))
                    req.Headers.TryAddWithoutValidation("Authorization", authHeader);

                var response = await client.SendAsync(req);
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<ChatAttachmentResult>(_snakeCaseJsonOptions);
                    if (!string.IsNullOrEmpty(result?.TextForPrompt))
                        return result.TextForPrompt;
                }
                _logger.LogWarning("Extraction de pièce jointe échouée : HTTP {Status}", (int)response.StatusCode);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Extraction de pièce jointe échouée (non bloquant, réponse dégradée)");
            }
            return $"[Pièce jointe : {name} ({mime}). Extraction indisponible pour le moment  demande à l'élève de recopier le passage utile, ou de joindre une photo de la page.]";
        }

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
            return $"[Pièce jointe : {name} ({mime})  contenu illisible.]";
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

        // Point E (sécurité) : le ConversationId fourni par le client n'est
        // utilisé que s'il appartient à l'utilisateur authentifié. Sans ce
        // contrôle, on pouvait faire lire à WinAI les 20 derniers messages de
        // la conversation d'un autre utilisateur (historique ci-dessous) et y
        // écrire. Vérifié AVANT la réserve de quota et avant tout octet de flux.
        if (request.ConversationId is int requestedConvId && requestedConvId != 0
            && !await ConversationBelongsToUserAsync(requestedConvId, userId, cancellationToken))
        {
            Response.StatusCode = 404;
            Response.ContentType = "application/json; charset=utf-8";
            await Response.WriteAsync(JsonSerializer.Serialize(new { error = "Conversation not found" }), cancellationToken);
            return;
        }

        // ⚠ Mur de quota WinAI (session 5 h + semaine 7 jours, décision 8.10).
        //
        // Cet endpoint est le chemin principal du frontend web (chat principal,
        // session d'étude, onglet WinAI parent, prédiction de réussite).
        // La vérification est faite ICI, avant toute écriture en base (aucune
        // conversation ni message n'est créé pour une requête refusée) et avant
        // le moindre octet de flux : les en-têtes SSE ne sont pas encore posés,
        // on peut donc répondre un vrai 402 (ou 503) application/json, avec la
        // même structure que SendMessage.
        //
        // Le mur réserve un forfait pour CE message (ClientMessageId, 8.7), puis
        // le coût réel le remplace en fin de flux (8.1, bloc finally).
        var quota = await _aiQuota.CheckAndReserveAsync(userId, request.ClientMessageId, "stream", cancellationToken);
        if (quota.Outcome == AiQuotaOutcome.Unavailable)
        {
            Response.StatusCode = 503;
            Response.ContentType = "application/json; charset=utf-8";
            await Response.WriteAsync(JsonSerializer.Serialize(QuotaUnavailableBody()), cancellationToken);
            return;
        }
        if (!quota.Allowed)
        {
            _logger.LogInformation("Stream WinAI refusé (limite {Limit} atteinte) pour l'utilisateur {UserId}", quota.LimitKind, userId);
            Response.StatusCode = 402;
            Response.ContentType = "application/json; charset=utf-8";
            await Response.WriteAsync(JsonSerializer.Serialize(QuotaExceededBody(quota)), cancellationToken);
            return;
        }

        // Coût réel observé dans le flux (événement d'usage final émis par
        // FastAPI). 0 = inconnu.
        int observedTokens = 0;
        // Vrai dès qu'un fragment de réponse du modèle a été relayé au client.
        bool servedContent = false;
        int relayedChars = 0;

        int conversationId = request.ConversationId ?? 0;
        bool isNew = conversationId == 0;

        // Point D : TOUT ce qui suit la réserve est dans ce try, dont le
        // finally décide de la finaliser ou de la libérer. Une exception entre
        // la réserve et le début du flux (écriture de la conversation, du
        // message, extraction de pièce jointe…) ne laisse plus de réserve
        // orpheline.
        try
        {
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
            // Pas d'en-tête X-Tokens-Left : aucun compteur brut exposé (8.3).

            // Emit conversationId to frontend on new conversation
            if (isNew)
            {
                await Response.WriteAsync($"data: {{\"conversationId\": {conversationId}}}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }

            // Last 20 messages for conversation context conversationId est
            // soit créée ci-dessus pour cet utilisateur, soit vérifiée comme
            // lui appartenant (point E).
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
            // été envoyé c'est le « le chatbot n'upload pas les fichiers ».
            var images    = request.Attachments?.Where(a => a.Type == "image").ToList()    ?? new();
            var documents = request.Attachments?.Where(a => a.Type != "image").ToList()    ?? new();
            var hasAny    = images.Count > 0 || documents.Count > 0;

            // Pré-calculé hors de la lambda .Select (synchrone, ne peut pas
            // await) : une seule pièce jointe document appelle RAG, pas une
            // par message d'historique.
            var documentDescriptions = new List<string>();
            foreach (var att in documents)
                documentDescriptions.Add(await DescribeDocumentAsync(att));

            var history = historyRaw.Select<dynamic, object>(h =>
            {
                if ((int)h.Id != savedMsgId || !hasAny)
                    return new { role = (string)h.role, content = (object)(string)h.content };

                var parts = new List<object>();
                if (!string.IsNullOrEmpty((string)h.content))
                    parts.Add(new { type = "text", text = (string)h.content });

                foreach (var att in images)
                    parts.Add(new { type = "image_url", image_url = new { url = att.Data } });

                foreach (var description in documentDescriptions)
                    parts.Add(new { type = "text", text = description });

                return new { role = (string)h.role, content = (object)parts };
            }).ToList();

            // Profil réel (niveau + inscriptions), recalculé en direct à chaque
            // message jamais depuis ChatbotContext (table de synchronisation
            // jamais alimentée en pratique par le frontend, voir
            // IChatbotService.GetLiveProfileContextAsync).
            var liveProfile = await _chatbotService.GetLiveProfileContextAsync(userId);

            // Module 13 (lot 6) : ce même endpoint est le chemin principal du
            // frontend web pour l'onglet WinAI parent (ParentWinAITab.tsx),
            // qui envoie déjà un `user_context.role`/`child_ids` dans le
            // corps de sa requête  mais StreamChatRequest ne déclare pas ce
            // champ, donc ASP.NET le désérialise en l'ignorant silencieusement
            // : il n'atteignait jamais FastAPI. Recalculé ici en direct depuis
            // la base plutôt que relayé tel quel depuis le client, pour que le
            // lien parent-enfant soit systématiquement vérifié côté serveur
            // (jamais un child_id arbitraire fourni par le client) et pour que
            // le mobile, qui n'envoie pas ce champ du tout, obtienne le même
            // contexte que le web.
            var role = await _dbContext.Users.Where(u => u.Id == userId).Select(u => u.Role).FirstOrDefaultAsync(cancellationToken);
            List<int>? childIds = null;
            if (role == "parent")
            {
                childIds = await _dbContext.ParentStudentLinks
                    .Where(l => l.ParentId == userId && l.Status == "accepted")
                    .Select(l => l.StudentId)
                    .ToListAsync(cancellationToken);
            }

            // Forward request to FastAPI stream endpoint
            var fastApiBody = new
            {
                messages = history,
                conversation_id = conversationId,
                // 8.7/8.8 : transmis à FastAPI pour que son propre contrôle de
                // quota reconnaisse la réserve posée ou rattachée ici.
                client_message_id = quota.ReservationId,
                max_tokens = 2000,
                temperature = 0.7,
                user_context = new
                {
                    role,
                    child_ids = childIds,
                    grade = liveProfile.Grade,
                    enrolled_subjects = liveProfile.EnrolledSubjects.Select(s => new { id = s.SubjectId, title = s.Title }),
                    enrolled_courses = liveProfile.EnrolledCourses.Select(c => new { id = c.CourseId, title = c.Title }),
                    force_language = string.IsNullOrEmpty(request.ForceLanguage) ? null : request.ForceLanguage,
                }
            };

            var httpClient = _httpClientFactory.CreateClient("FastApiClient");
            using var fastApiReq = new HttpRequestMessage(HttpMethod.Post, "/api/chatbot/stream");
            fastApiReq.Content = JsonContent.Create(fastApiBody);

            var authHeader = HttpContext.Request.Headers["Authorization"].ToString();
            if (!string.IsNullOrEmpty(authHeader))
                fastApiReq.Headers.TryAddWithoutValidation("Authorization", authHeader);

            using var fastApiRes = await httpClient.SendAsync(
                fastApiReq, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!fastApiRes.IsSuccessStatusCode)
            {
                // Rien servi : le finally libère la réserve.
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

                // 8.1 : événement d'usage final émis par FastAPI
                // ({"usage_final": true, "tokens_used": N}). Il sert au
                // décompte et n'est PAS relayé au client (8.3).
                if (line.Contains("\"usage_final\""))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(line[6..]);
                        if (doc.RootElement.TryGetProperty("tokens_used", out var t) && t.TryGetInt32(out var n))
                            observedTokens = Math.Max(observedTokens, n);
                    }
                    catch (JsonException) { /* événement illisible : estimation au finally */ }
                    continue;
                }

                await Response.WriteAsync(line + "\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);

                if (line[6..] == "[DONE]") break;

                // Un fragment de réponse du modèle a atteint le client : la
                // réserve ne peut plus être rendue (voir finally).
                if (line.Contains("\"delta\""))
                {
                    servedContent = true;
                    relayedChars += line.Length - 6;
                }
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
                if (!Response.HasStarted)
                {
                    Response.StatusCode = 500;
                    Response.ContentType = "text/event-stream";
                }
                await Response.WriteAsync("data: {\"error\": \"Stream error\"}\n\ndata: [DONE]\n\n");
                await Response.Body.FlushAsync(CancellationToken.None);
            }
            catch { }
        }
        finally
        {
            // Décompte, dans cet ordre (CancellationToken.None : le client a pu
            // se déconnecter, le coût fournisseur est engagé) :
            //  1. coût réel remonté par FastAPI → finalisé ;
            //  2. réponse servie mais usage absent (flux coupé avant
            //     l'événement final) → finalisé sur estimation : l'identifiant
            //     est consommé, il ne rouvrira pas de message gratuit (point A) ;
            //  3. rien servi (FastAPI en échec, exception avant le flux, flux
            //     abandonné avant le premier fragment, y compris l'abandon à
            //     8 s du client) → réserve libérée, si CETTE tentative la
            //     détient encore (un repli REST qui l'a rattachée la garde).
            if (observedTokens > 0)
                await _aiQuota.FinalizeUsageAsync(userId, quota.ReservationId, observedTokens, CancellationToken.None);
            else if (servedContent)
                await _aiQuota.FinalizeUsageAsync(userId, quota.ReservationId,
                    AiUsagePolicy.EstimateServedTokens(relayedChars), CancellationToken.None);
            else
                await _aiQuota.ReleaseReservationAsync(userId, quota.ReservationId, quota.AttemptId, CancellationToken.None);

            // Update conversation metadata
            if (conversationId != 0)
            {
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
