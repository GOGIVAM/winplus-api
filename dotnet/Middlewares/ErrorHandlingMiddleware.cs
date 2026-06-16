using System.Net;
using System.Text.Json;
using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Middlewares;

/// <summary>
/// Middleware pour la gestion centralisée des exceptions
/// </summary>
public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;
    private readonly IServiceProvider _services;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger, IServiceProvider services)
    {
        _next     = next;
        _logger   = logger;
        _services = services;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Exception non gérée");

            // Persist to ApplicationLogs (best-effort, deduplication 24h)
            _ = PersistLogAsync(exception, context.Request.Path);

            // Si la réponse a déjà commencé, on ne peut rien faire
            if (context.Response.HasStarted)
            {
                _logger.LogWarning("Exception survenue après le début de la réponse - impossible d'envoyer une réponse d'erreur");
                return;
            }

            await HandleExceptionAsync(context, exception);
        }
    }

    private async Task PersistLogAsync(Exception exception, string requestPath)
    {
        try
        {
            await using var scope = _services.CreateAsyncScope();
            var db      = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var message = exception.Message.Length > 4000 ? exception.Message[..4000] : exception.Message;
            var category = exception.GetType().Name;
            var since   = DateTime.UtcNow.AddHours(-24);

            // Dedup: if same message+category in last 24h, skip insert
            var exists = await db.ApplicationLogs
                .AnyAsync(l => l.Message == message && l.Category == category && l.CreatedAt >= since);

            if (!exists)
            {
                db.ApplicationLogs.Add(new ApplicationLog
                {
                    Level       = "Error",
                    Category    = category,
                    Message     = message,
                    Exception   = exception.ToString().Length > 8000 ? exception.ToString()[..8000] : exception.ToString(),
                    StackTrace  = exception.StackTrace?.Length > 4000 ? exception.StackTrace[..4000] : exception.StackTrace,
                    RequestPath = requestPath.Length > 500 ? requestPath[..500] : requestPath,
                    CreatedAt   = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
            }
        }
        catch (Exception logEx)
        {
            _logger.LogWarning("Could not persist to ApplicationLogs: {Msg}", logEx.Message);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        // Déterminer le status code approprié
        var statusCode = exception switch
        {
            UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
            ArgumentException => StatusCodes.Status400BadRequest,
            KeyNotFoundException => StatusCodes.Status404NotFound,
            InvalidOperationException => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        var response = new ErrorResponse
        {
            Message = exception.Message,
            Code = exception.GetType().Name,
            Error = GetErrorMessage(exception),
            Timestamp = DateTime.UtcNow
        };

        await context.Response.WriteAsJsonAsync(response);
    }

    private static string GetErrorMessage(Exception exception)
    {
        return exception switch
        {
            UnauthorizedAccessException => "Non autorisé",
            ArgumentException => "Argument invalide",
            KeyNotFoundException => "Ressource non trouvée",
            InvalidOperationException => "Opération invalide",
            _ => "Une erreur serveur interne s'est produite"
        };
    }
}

/// <summary>
/// Format standardisé des réponses d'erreur
/// </summary>
public class ErrorResponse
{
    /// <summary>
    /// Classe d'erreur générale
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Message d'erreur détaillé
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Code d'erreur spécifique
    /// </summary>
    public string Code { get; set; } = "ERROR";

    /// <summary>
    /// Détails additionnels (stack trace en développement)
    /// </summary>
    public object? Details { get; set; }

    /// <summary>
    /// Timestamp de l'erreur
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Extensions pour enregistrer le middleware
/// </summary>
public static class ErrorHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseErrorHandling(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ErrorHandlingMiddleware>();
    }
}
