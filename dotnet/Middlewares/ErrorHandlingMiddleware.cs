using System.Net;
using System.Text.Json;

namespace Backend.Middlewares;

/// <summary>
/// Middleware pour la gestion centralisée des exceptions
/// </summary>
public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
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
            
            // Si la réponse a déjà commencé, on ne peut rien faire
            if (context.Response.HasStarted)
            {
                _logger.LogWarning("Exception survenue après le début de la réponse - impossible d'envoyer une réponse d'erreur");
                // Ne pas re-throw - cela causerait ERR_INCOMPLETE_CHUNKED_ENCODING
                return;
            }

            await HandleExceptionAsync(context, exception);
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
