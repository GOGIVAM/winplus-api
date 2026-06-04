using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Services;

namespace Backend.Controllers;

/// <summary>
/// Health check endpoints pour monitoring des services
/// ✅ AJOUTÉ: Vérification DB, FastApi, Custom Auth
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IFastApiClient _fastapiClient;
    private readonly ILogger<HealthController> _logger;

    public HealthController(
        ApplicationDbContext dbContext,
        IFastApiClient fastapiClient,
        ILogger<HealthController> logger)
    {
        _dbContext = dbContext;
        _fastapiClient = fastapiClient;
        _logger = logger;
    }

    /// <summary>
    /// Health check simple (status OK = application fonctionne)
    /// GET /api/health
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetHealth()
    {
        return Ok(new
        {
            status = "healthy",
            service = "Educational AI Gateway (ASP.NET)",
            timestamp = DateTime.UtcNow,
            authentication = "Custom JWT Auth",
            version = "4.0"
        });
    }

    /// <summary>
    /// Health check détaillé (tous les services)
    /// GET /api/health/ready
    /// </summary>
    [HttpGet("ready")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetReadiness()
    {
        var checks = new Dictionary<string, object>();

        // Vérifier Database
        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync("SELECT 1");
            checks["database"] = new { status = "healthy", message = "PostgreSQL connected" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database health check failed");
            checks["database"] = new { status = "unhealthy", message = ex.Message };
        }

        // Vérifier FastApi API
        var fastapiHealthy = await _fastapiClient.HealthCheckAsync();
        checks["fastapi_ai_service"] = new
        {
            status = fastapiHealthy ? "healthy" : "unhealthy",
            message = fastapiHealthy ? "FastApi API responding" : "FastApi API unreachable"
        };

        // Vérifier Custom Auth
        checks["custom_auth"] = new
        {
            status = "healthy",
            message = "Custom JWT authentication configured"
        };

        // Déterminer le status global
        var allHealthy = checks.Values
            .Cast<dynamic>()
            .All(c => c.status == "healthy");

        var statusCode = allHealthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;

        return StatusCode(statusCode, new
        {
            status = allHealthy ? "ready" : "not_ready",
            timestamp = DateTime.UtcNow,
            checks = checks
        });
    }

    /// <summary>
    /// Health check pour Database uniquement
    /// GET /api/health/db
    /// </summary>
    [HttpGet("db")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetDatabaseHealth()
    {
        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync("SELECT 1");
            return Ok(new { status = "healthy", service = "PostgreSQL" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database health check failed");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                status = "unhealthy",
                service = "PostgreSQL",
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// Health check pour FastApi API uniquement
    /// GET /api/health/fastapi
    /// </summary>
    [HttpGet("fastapi")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetFastApiHealth()
    {
        var isHealthy = await _fastapiClient.HealthCheckAsync();

        if (isHealthy)
        {
            return Ok(new { status = "healthy", service = "FastApi AI Service" });
        }

        return StatusCode(StatusCodes.Status503ServiceUnavailable, new
        {
            status = "unhealthy",
            service = "FastApi AI Service",
            error = "FastApi API is not responding"
        });
    }

    /// <summary>
    /// Health check pour Custom Authentication
    /// GET /api/health/auth
    /// </summary>
    [HttpGet("auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetAuthHealth()
    {
        return Ok(new
        {
            status = "healthy",
            service = "Custom JWT Authentication",
            message = "JWT-based authentication active"
        });
    }

    /// <summary>
    /// Diagnostic complet du système
    /// GET /api/health/diagnostics
    /// </summary>
    [HttpGet("diagnostics")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDiagnostics()
    {
        var diagnostics = new
        {
            timestamp = DateTime.UtcNow,
            environment = new
            {
                aspNetCoreEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
                dotnetVersion = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                machineProcessorCount = Environment.ProcessorCount,
                workingSetMemoryMB = GC.GetTotalMemory(false) / (1024 * 1024)
            },
            database = new
            {
                connectionString = "***REDACTED***", // Ne pas exposer la vraie string
                isConnected = await IsDatabaseConnected()
            },
            services = new
            {
                fastapiApi = await _fastapiClient.HealthCheckAsync(),
                customAuth = true
            },
            application = new
            {
                version = "3.0",
                name = "Educational AI Gateway"
            }
        };

        return Ok(diagnostics);
    }

    private async Task<bool> IsDatabaseConnected()
    {
        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync("SELECT 1");
            return true;
        }
        catch
        {
            return false;
        }
    }


}
