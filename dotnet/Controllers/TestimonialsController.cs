using Microsoft.AspNetCore.Mvc;
using Backend.Models.Entities;
using Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

/// <summary>
/// Controller pour gérer les témoignages utilisateurs
/// </summary>
[ApiController]
[Route("api/testimonials")]
[Produces("application/json")]
public class TestimonialsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<TestimonialsController> _logger;

    public TestimonialsController(ApplicationDbContext context, ILogger<TestimonialsController> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Récupère les témoignages avec limite optionnelle
    /// GET /api/testimonials?limit=5
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetTestimonials([FromQuery] int limit = 5)
    {
        try
        {
            _logger.LogInformation("Récupération des {Limit} témoignages", limit);

            var testimonials = await _context.Reviews
                .AsNoTracking()
                .Include(r => r.User)
                .Include(r => r.Subject)
                .Where(r => !r.IsDeleted && r.User != null)
                .OrderByDescending(r => r.CreatedAt)
                .Take(limit)
                .Select(r => new
                {
                    id = r.Id,
                    author = (r.User!.FirstName + " " + r.User.LastName).Trim(),
                    email = r.User!.Email,
                    title = r.Title ?? "Avis utilisateur",
                    content = r.Comment,
                    rating = r.Rating,
                    createdAt = r.CreatedAt,
                    subject = r.Subject != null ? r.Subject.Title : "Cours",
                    isVerifiedPurchase = r.IsVerifiedPurchase
                })
                .ToListAsync();

            return Ok(new { data = testimonials, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération des témoignages");
            return StatusCode(500, new { success = false, error = "Erreur serveur" });
        }
    }

    /// <summary>
    /// Ajouter un nouveau témoignage (requires auth)
    /// POST /api/testimonials
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateTestimonial([FromBody] CreateTestimonialRequest request)
    {
        try
        {
            // Get UserId from claims
            var userIdClaim = User.FindFirst("user_id")?.Value 
                           ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            
            if (!int.TryParse(userIdClaim, out var userId))
                return Unauthorized(new { success = false, error = "Non authentifié" });

            if (string.IsNullOrWhiteSpace(request.Comment))
                return BadRequest(new { success = false, error = "Le commentaire est requis" });

            if (request.Rating < 1 || request.Rating > 5)
                return BadRequest(new { success = false, error = "Note invalide (1-5)" });

            // Verify subject exists
            var subject = await _context.Subjects.FindAsync(request.SubjectId);
            if (subject == null)
                return BadRequest(new { success = false, error = "Cours introuvable" });

            var review = new Review
            {
                UserId = userId,
                SubjectId = request.SubjectId,
                Title = request.Title ?? "Avis utilisateur",
                Comment = request.Comment,
                Rating = request.Rating,
                IsVerifiedPurchase = false,
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow
            };

            _context.Reviews.Add(review);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetTestimonials), new { id = review.Id }, 
                new { success = true, id = review.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la création du témoignage");
            return StatusCode(500, new { success = false, error = "Erreur serveur" });
        }
    }
}

/// <summary>
/// DTO pour créer un nouveau témoignage
/// </summary>
public class CreateTestimonialRequest
{
    public int SubjectId { get; set; }
    public string? Title { get; set; }
    public string Comment { get; set; } = null!;
    public int Rating { get; set; } = 5;
}

