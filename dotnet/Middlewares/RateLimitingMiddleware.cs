using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Backend.Middlewares;

/// <summary>
/// Rate limiting middleware for authentication endpoints
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitingMiddleware> _logger;
    
    // Store requests per IP: IP -> (timestamp, count)
    private static readonly Dictionary<string, List<DateTime>> RequestLog = new();
    private readonly object _lockObject = new();

    // Configuration
    private const int LoginAttempts = 20;  // Augmenté pour les tests
    private const int LoginWindowMinutes = 15;
    private const int SignupAttempts = 50;  // Augmenté pour les tests
    private const int SignupWindowHours = 1;
    private const int PasswordResetAttempts = 30;  // Augmenté pour les tests
    private const int PasswordResetWindowHours = 1;

    public RateLimitingMiddleware(RequestDelegate next, ILogger<RateLimitingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var path = request.Path.Value ?? "";
        var clientIp = GetClientIpAddress(context);

        // Check rate limits for specific endpoints
        if (path.Contains("/auth/signin", StringComparison.OrdinalIgnoreCase))
        {
            if (!CheckRateLimit(clientIp, LoginAttempts, LoginWindowMinutes))
            {
                _logger.LogWarning("Rate limit exceeded for login from IP: {ClientIp}", clientIp);
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Too many login attempts. Please try again later.",
                    retryAfter = LoginWindowMinutes
                });
                return;
            }
        }
        else if (path.Contains("/auth/signup", StringComparison.OrdinalIgnoreCase))
        {
            if (!CheckRateLimit(clientIp, SignupAttempts, SignupWindowHours * 60))
            {
                _logger.LogWarning("Rate limit exceeded for signup from IP: {ClientIp}", clientIp);
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Too many signup attempts. Please try again later.",
                    retryAfter = SignupWindowHours
                });
                return;
            }
        }
        else if (path.Contains("/auth/forgot-password", StringComparison.OrdinalIgnoreCase))
        {
            if (!CheckRateLimit(clientIp, PasswordResetAttempts, PasswordResetWindowHours * 60))
            {
                _logger.LogWarning("Rate limit exceeded for password reset from IP: {ClientIp}", clientIp);
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Too many password reset attempts. Please try again later.",
                    retryAfter = PasswordResetWindowHours
                });
                return;
            }
        }

        await _next(context);
    }

    private bool CheckRateLimit(string clientIp, int maxAttempts, int windowMinutes)
    {
        lock (_lockObject)
        {
            var now = DateTime.UtcNow;
            var cutoffTime = now.AddMinutes(-windowMinutes);

            if (!RequestLog.ContainsKey(clientIp))
            {
                RequestLog[clientIp] = new List<DateTime>();
            }

            // Remove old requests outside the window
            RequestLog[clientIp] = RequestLog[clientIp]
                .Where(t => t > cutoffTime)
                .ToList();

            // Check if limit exceeded
            if (RequestLog[clientIp].Count >= maxAttempts)
            {
                return false;
            }

            // Add current request
            RequestLog[clientIp].Add(now);
            return true;
        }
    }

    private string GetClientIpAddress(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
        {
            var ips = forwardedFor.ToString().Split(',');
            return ips[0].Trim();
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
    }
}

/// <summary>
/// Extension method for adding rate limiting middleware
/// </summary>
public static class RateLimitingMiddlewareExtensions
{
    public static IApplicationBuilder UseRateLimiting(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<RateLimitingMiddleware>();
    }
}
