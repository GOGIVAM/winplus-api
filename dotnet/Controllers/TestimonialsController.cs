using Microsoft.AspNetCore.Authorization;
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
    /// Témoignages publics : uniquement des avis réels laissés en base, avec un
    /// commentaire écrit (avis sur un contenu, sur une formation, sur une séance
    /// de cours particulier non retirée par la modération). Aucune liste de
    /// secours : sans avis réel, la réponse est vide et les pages masquent la
    /// section. L'auteur est affiché « Prénom N. » ; l'adresse e-mail n'est
    /// jamais exposée (cet endpoint est anonyme).
    /// GET /api/testimonials?limit=5
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetTestimonials([FromQuery] int limit = 5)
    {
        try
        {
            limit = Math.Clamp(limit, 1, 20);
            _logger.LogInformation("Récupération des {Limit} témoignages", limit);

            var subjectReviews = await _context.Reviews
                .AsNoTracking()
                .Where(r => !r.IsDeleted && r.User != null && !r.User.IsDeleted
                         && r.Comment != null && r.Comment.Trim() != "")
                .OrderByDescending(r => r.CreatedAt)
                .Take(limit)
                .Select(r => new TestimonialRow("subject", r.Id, r.User!.FirstName, r.User.LastName,
                    r.Subject != null ? r.Subject.Title : null, r.Comment!, r.Rating, r.CreatedAt, r.IsVerifiedPurchase))
                .ToListAsync();

            var courseReviews = await _context.CourseReviews
                .AsNoTracking()
                .Where(r => !r.User.IsDeleted && r.Comment != null && r.Comment.Trim() != "")
                .OrderByDescending(r => r.CreatedAt)
                .Take(limit)
                .Select(r => new TestimonialRow("course", r.Id, r.User.FirstName, r.User.LastName,
                    r.Course.Title, r.Comment!, r.Rating, r.CreatedAt, r.IsVerified))
                .ToListAsync();

            // Avis de séance : seulement ceux qui sont visibles sur le profil du
            // tuteur (ni retirés par la modération, ni en attente de décision).
            var tutorReviews = await _context.TutorReviews
                .AsNoTracking()
                .Where(r => !r.IsHiddenByModeration && !(r.IsReported && r.ModerationDecision == null)
                         && r.Student != null && !r.Student.IsDeleted
                         && r.Comment != null && r.Comment.Trim() != "")
                .OrderByDescending(r => r.CreatedAt)
                .Take(limit)
                .Select(r => new TestimonialRow("tutor", r.Id, r.Student!.FirstName, r.Student.LastName,
                    "Cours particulier", r.Comment!, r.Rating, r.CreatedAt, true))
                .ToListAsync();

            var testimonials = subjectReviews.Concat(courseReviews).Concat(tutorReviews)
                .OrderByDescending(r => r.CreatedAt)
                .Take(limit)
                .Select(r => new
                {
                    id = $"{r.Source}-{r.Id}",
                    source = r.Source,
                    author = DisplayName(r.FirstName, r.LastName),
                    title = r.Context,
                    content = r.Comment.Trim(),
                    rating = Math.Clamp(r.Rating, 1, 5),
                    createdAt = r.CreatedAt,
                    isVerifiedPurchase = r.Verified
                })
                .ToList();

            return Ok(new { data = testimonials, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération des témoignages");
            return StatusCode(500, new { success = false, error = "Erreur serveur" });
        }
    }

    private sealed record TestimonialRow(string Source, int Id, string? FirstName, string? LastName,
        string? Context, string Comment, int Rating, DateTime CreatedAt, bool Verified);

    /// <summary>« Prénom N. » : jamais le nom complet ni l'e-mail sur une page publique.</summary>
    [NonAction]
    public static string DisplayName(string? firstName, string? lastName)
    {
        var first = (firstName ?? "").Trim();
        var last = (lastName ?? "").Trim();
        if (first.Length == 0 && last.Length == 0) return "Utilisateur WinPlus";
        if (first.Length == 0) return char.ToUpperInvariant(last[0]) + ".";
        return last.Length == 0 ? first : $"{first} {char.ToUpperInvariant(last[0])}.";
    }

    /// <summary>
    /// Ajouter un nouveau témoignage (requires auth)
    /// POST /api/testimonials
    /// </summary>
    [HttpPost]
    // Module 17 : l'endpoint ne portait aucune exigence d'authentification.
    // Il lisait bien la revendication d'identité (et refusait donc un appel
    // sans jeton), mais sans [Authorize] aucun échec d'authentification
    // n'était signalé comme tel : un jeton expiré ou invalide donnait une
    // réponse indiscernable d'un oubli de jeton. L'attribut rend le contrat
    // explicite et aligne cet endpoint sur le reste du projet.
    [Authorize]
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

