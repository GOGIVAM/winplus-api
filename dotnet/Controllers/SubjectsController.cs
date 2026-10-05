using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Backend.Extensions;
using Backend.Services;
using Backend.Models.Entities;
using Backend.Models.DTOs;
using Backend.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Backend.Controllers;

[ApiController]
[Route("api/subjects")]
public class SubjectsController : ControllerBase
{
    private readonly ISubjectService _subjectService;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<SubjectsController> _logger;
    private readonly IFastApiClient _fastApiClient;
    private readonly IConfiguration _configuration;
    private readonly IStorageService _storage;
    private readonly IContentAccessService _contentAccess;
    private readonly IDocumentWatermarkService _watermark;
    private readonly IWalletService _wallet;
    private readonly INtfyService _ntfy;

    public SubjectsController(
        ISubjectService subjectService,
        ApplicationDbContext context,
        ILogger<SubjectsController> logger,
        IFastApiClient fastApiClient,
        IConfiguration configuration,
        IStorageService storage,
        IContentAccessService contentAccess,
        IDocumentWatermarkService watermark,
        IWalletService wallet,
        INtfyService ntfy)
    {
        _subjectService = subjectService;
        _context = context;
        _logger = logger;
        _fastApiClient = fastApiClient;
        _configuration = configuration;
        _storage = storage;
        _contentAccess = contentAccess;
        _watermark = watermark;
        _wallet = wallet;
        _ntfy = ntfy;
    }

    private class PythonRecsResponse { public List<object>? Recommendations { get; set; } }

    /// <summary>
    /// Subject ne porte pas ExamType/Level/Year/Session/Durée/Difficulté :
    /// ces champs vivent sur l'Exam créé à l'upload et rattaché via
    /// Exam.SubjectId. Sans cet enrichissement, le catalogue retombait sur
    /// les valeurs par défaut du frontend ("BEPC", année courante...) au
    /// lieu des vraies informations saisies par l'admin. Quand plusieurs
    /// Exam partagent un même Subject (catégories historiques), on prend
    /// le plus récent.
    /// </summary>
    private async Task<Dictionary<int, Exam>> GetPrimaryExamsAsync(IEnumerable<int> subjectIds)
    {
        var ids = subjectIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, Exam>();

        var exams = await _context.Exams.AsNoTracking()
            .Where(e => e.SubjectId != null && ids.Contains(e.SubjectId.Value) && !e.IsDeleted)
            .ToListAsync();

        return exams
            .GroupBy(e => e.SubjectId!.Value)
            .Select(g => g.OrderByDescending(e => e.CreatedAt).First())
            .ToDictionary(e => e.SubjectId!.Value);
    }

    private static object ProjectSubject(Subject s, Exam? exam, bool isAuthorVerified = false) => new
    {
        id = s.Id,
        title = s.Title,
        description = s.Description,
        category = s.Category,
        thumbnailUrl = s.ThumbnailUrl,
        price = s.Price,
        isPublished = s.IsPublished,
        enrollmentCount = s.EnrollmentCount,
        isFeatured = s.IsFeatured,
        averageRating = s.AverageRating,
        totalRatings = s.TotalRatings,
        downloadCount = exam?.DownloadCount ?? s.DownloadCount ?? 0,
        createdAt = s.CreatedAt,
        updatedAt = s.UpdatedAt,
        isDeleted = s.IsDeleted,
        // Module 2, US-CAT-01/US-CAT-02 : filtre "Auteur Vérifié" côté
        // professeur-acheteur, basé sur le badge "Vérifié Diplôme" (Module 1).
        authorUserId = s.AuthorUserId,
        isAuthorVerified = isAuthorVerified,
        // Nécessaire pour le "mode évaluation" (POST /quizzes/exam/{examId}) :
        // sans cet id, le frontend n'a aucun moyen de savoir quelle épreuve
        // précise évaluer derrière ce Subject.
        examId = exam?.Id,
        examType = exam?.ExamType,
        level = exam?.Level,
        // Niveau(x) de classe ciblé(s) par LA MATIÈRE elle-même (Subject.Level,
        // taggée en admin), distinct de `level` ci-dessus qui vient de l'Exam
        // (ex: "Terminale C" pour cette épreuve précise). Sert au sélecteur
        // matière+difficulté de la génération de quiz/fiches IA.
        subjectLevel = s.Level,
        year = exam?.Year,
        session = exam?.Session,
        durationMinutes = exam?.DurationMinutes,
        difficulty = exam?.Difficulty,
        hasCorrection = !string.IsNullOrEmpty(exam?.CorrectionUrl),
    };

    /// <summary>
    /// Identité de l'appelant pour le filtrage des brouillons : null si
    /// anonyme. Les endpoints de catalogue sont volontairement ouverts (la
    /// vitrine publique doit rester consultable sans compte), on ne peut donc
    /// pas s'appuyer sur [Authorize] pour lire l'utilisateur.
    /// </summary>
    private (int? userId, bool isAdmin) CurrentViewer()
    {
        try
        {
            if (User?.Identity?.IsAuthenticated != true) return (null, false);
            return (User.GetUserId(), User.IsAdmin());
        }
        catch (UnauthorizedAccessException)
        {
            return (null, false);
        }
    }

    /// <summary>
    /// Un contenu non publié ne doit apparaître dans aucune liste publique
    /// (Module 17) : seul son auteur et un administrateur voient leurs
    /// brouillons. Sans ce filtre, protéger la publication ne sert à rien
    /// puisque les brouillons sont déjà exposés.
    /// </summary>
    private bool IsVisibleToViewer(Subject s, int? userId, bool isAdmin) =>
        s.IsPublished || isAdmin || (userId.HasValue && s.AuthorUserId == userId.Value);

    /// <summary>
    /// Même règle que <see cref="IsVisibleToViewer"/>, appliquée dans la
    /// requête SQL (passe de clôture du lot 0) : les listes publiques
    /// (populaires, vedettes, récents) filtraient après avoir pris les N
    /// premiers contenus, et renvoyaient donc moins d'éléments que demandé
    /// dès qu'un brouillon figurait parmi eux.
    /// </summary>
    private IQueryable<Subject> VisibleSubjectsQuery(int? userId, bool isAdmin) =>
        _context.Subjects
            .WhereNotDeleted()
            .AsNoTracking()
            .Where(s => s.IsPublished || isAdmin || (userId != null && s.AuthorUserId == userId));

    private static int ClampListLimit(int limit) => limit < 1 || limit > 100 ? 20 : limit;

    [HttpGet]
    [ProducesResponseType(typeof(PaginationResponse<Subject>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? q = null,
        [FromQuery] string? category = null,
        [FromQuery] string? difficulty = null,
        [FromQuery] bool verifiedAuthorOnly = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string sortBy = "createdAt",
        [FromQuery] string sortOrder = "desc")
    {
        try
        {
            if (page < 1) page = 1;
            pageSize = Math.Clamp(pageSize, 1, 500);

            var (viewerId, viewerIsAdmin) = CurrentViewer();
            var query = _context.Subjects
                .Where(s => !s.IsDeleted)
                // Module 17 : les brouillons ne sortent jamais du catalogue
                // public  seul leur auteur, ou un administrateur, les voit.
                .Where(s => s.IsPublished || viewerIsAdmin || (viewerId != null && s.AuthorUserId == viewerId))
                .AsQueryable();

            if (verifiedAuthorOnly)
                query = query.Where(s => s.AuthorUserId != null &&
                    _context.TutorProfiles.Any(tp => tp.UserId == s.AuthorUserId && tp.IsDiplomaVerified));

            if (!string.IsNullOrWhiteSpace(q))
            {
                var lower = q.ToLower();
                query = query.Where(s =>
                    s.Title.ToLower().Contains(lower) ||
                    (s.Description != null && s.Description.ToLower().Contains(lower)));
            }

            if (!string.IsNullOrWhiteSpace(category))
                query = query.Where(s => s.Category != null && s.Category.ToLower() == category.ToLower());

            if (!string.IsNullOrWhiteSpace(difficulty))
                query = query.Where(s => _context.Exams.Any(e =>
                    e.SubjectId == s.Id && !e.IsDeleted &&
                    e.Difficulty != null && e.Difficulty.ToLower() == difficulty.ToLower()));

            var totalCount = await query.CountAsync();

            var isAscending = sortOrder.ToLower() != "desc";
            query = sortBy.ToLower() switch
            {
                "price" => isAscending ? query.OrderBy(s => s.Price) : query.OrderByDescending(s => s.Price),
                "title" => isAscending ? query.OrderBy(s => s.Title) : query.OrderByDescending(s => s.Title),
                // "Les épreuves les plus téléchargées" (landing page) : sans ce cas,
                // sortBy=popular retombait silencieusement sur createdAt desc et le
                // titre de la section ne correspondait à rien de réel.
                "popular" => isAscending ? query.OrderBy(s => s.DownloadCount) : query.OrderByDescending(s => s.DownloadCount),
                "recent" => query.OrderByDescending(s => s.CreatedAt),
                "featured" => query.OrderByDescending(s => s.IsFeatured).ThenByDescending(s => s.CreatedAt),
                _ => isAscending ? query.OrderBy(s => s.CreatedAt) : query.OrderByDescending(s => s.CreatedAt)
            };

            var subjects = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            var examMap = await GetPrimaryExamsAsync(subjects.Select(s => s.Id));
            var verifiedAuthorIds = await GetVerifiedAuthorIdsAsync(subjects.Select(s => s.AuthorUserId));
            var projected = subjects.Select(s => ProjectSubject(s, examMap.GetValueOrDefault(s.Id),
                s.AuthorUserId.HasValue && verifiedAuthorIds.Contains(s.AuthorUserId.Value))).ToList();
            var response = new PaginationResponse<object>(projected, totalCount, page, pageSize);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération des cours");
            return StatusCode(500, "Erreur serveur");
        }
    }

    private async Task<HashSet<int>> GetVerifiedAuthorIdsAsync(IEnumerable<int?> authorUserIds)
    {
        var ids = authorUserIds.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        if (ids.Count == 0) return new HashSet<int>();
        var verified = await _context.TutorProfiles
            .Where(tp => ids.Contains(tp.UserId) && tp.IsDiplomaVerified)
            .Select(tp => tp.UserId)
            .ToListAsync();
        return verified.ToHashSet();
    }

    /// <summary>
    /// Helper method to sort subjects
    /// </summary>
    private List<Subject> SortSubjects(IEnumerable<Subject> subjects, string sortBy, string sortOrder)
    {
        var isAscending = sortOrder?.ToLower() != "desc";
        
        var sorted = sortBy?.ToLower() switch
        {
            "price" => isAscending 
                ? subjects.OrderBy(s => s.Price).ToList()
                : subjects.OrderByDescending(s => s.Price).ToList(),
            "title" => isAscending
                ? subjects.OrderBy(s => s.Title).ToList()
                : subjects.OrderByDescending(s => s.Title).ToList(),
            "createdAt" => isAscending
                ? subjects.OrderBy(s => s.CreatedAt).ToList()
                : subjects.OrderByDescending(s => s.CreatedAt).ToList(),
            _ => subjects.OrderByDescending(s => s.CreatedAt).ToList() // Default sort
        };
        
        return sorted;
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        try
        {
            var subject = await _subjectService.GetSubjectByIdAsync(id);
            if (subject == null)
                return NotFound();

            var (viewerId, viewerIsAdmin) = CurrentViewer();
            if (!IsVisibleToViewer(subject, viewerId, viewerIsAdmin))
                return NotFound();

            var examMap = await GetPrimaryExamsAsync(new[] { id });
            var isAuthorVerified = subject.AuthorUserId.HasValue &&
                await _context.TutorProfiles.AnyAsync(tp => tp.UserId == subject.AuthorUserId && tp.IsDiplomaVerified);

            // "X enseignants ont utilisé ce contenu dans leurs formations" (US-CAT-02) :
            // nombre de professeurs distincts (autres que l'auteur) l'ayant
            // ajouté à une leçon via "Ajouter à une formation".
            var usedByTeachersCount = await (
                from l in _context.CourseLessons
                join c in _context.Courses on l.CourseId equals c.Id
                where l.SourceSubjectId == id
                select c.InstructorId
            ).Distinct().CountAsync();

            var projected = System.Text.Json.Nodes.JsonNode.Parse(
                System.Text.Json.JsonSerializer.Serialize(ProjectSubject(subject, examMap.GetValueOrDefault(id), isAuthorVerified)))!.AsObject();
            projected["usedByTeachersCount"] = usedByTeachersCount;

            // "Assigné par [Nom]" (US-CLA-04) : affiché uniquement si l'élève
            // connecté y accède via une assignation de classe (pas un achat).
            try
            {
                var userId = User.GetUserId();
                var assignerName = await (
                    from tcc in _context.TeacherClassContents
                    join cs in _context.TeacherClassStudents on tcc.TeacherClassId equals cs.TeacherClassId
                    join teacher in _context.Users on tcc.AssignedByUserId equals teacher.Id
                    where tcc.SubjectId == id && cs.StudentId == userId
                    select teacher.FirstName + " " + teacher.LastName
                ).FirstOrDefaultAsync();
                if (assignerName != null) projected["assignedByTeacherName"] = assignerName;
            }
            catch (UnauthorizedAccessException) { /* utilisateur anonyme : pas d'assignation à afficher */ }

            return Ok(projected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération du cours {SubjectId}", id);
            return StatusCode(500, "Erreur serveur");
        }
    }

    /// <summary>
    /// Création d'une matière  réservée au professeur et à l'administrateur
    /// (Module 17 : cet endpoint n'exigeait aucune authentification et liait
    /// directement l'entité au corps de la requête).
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "InstructorOnly")]
    public async Task<IActionResult> Create([FromBody] SubjectCreateRequest request)
    {
        try
        {
            var userId = User.GetUserId();
            var isAdmin = User.IsAdmin();

            var subject = new Subject
            {
                Title = request.Title.Trim(),
                Description = request.Description,
                Category = request.Category,
                Level = request.Level,
                ThumbnailUrl = request.ThumbnailUrl,
                // FCFA : devise sans sous-unité, jamais de montant fractionnaire.
                Price = decimal.Round(request.Price, 0, MidpointRounding.AwayFromZero),
                // La publication reste une décision humaine passant par
                // l'endpoint d'approbation administrateur.
                IsPublished = false,
                // L'auteur détermine à qui revient le revenu de la vente : il
                // est déduit du jeton, jamais accepté depuis la requête. Le
                // contenu créé par un administrateur reste sans auteur, comme
                // le contenu historique, pour ne pas créditer un compte
                // d'administration des revenus d'une vente.
                AuthorUserId = isAdmin ? null : userId,
            };

            var created = await _subjectService.CreateSubjectAsync(subject);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la création du cours");
            return StatusCode(500, "Erreur serveur");
        }
    }

    /// <summary>
    /// Mise à jour d'une matière  réservée à l'auteur du contenu et à
    /// l'administrateur, sur une liste blanche de champs (Module 17).
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Policy = "InstructorOnly")]
    public async Task<IActionResult> Update(int id, [FromBody] SubjectUpdateRequest request)
    {
        try
        {
            var existing = await _subjectService.GetSubjectByIdAsync(id);
            if (existing == null) return NotFound();

            if (!User.IsAdmin() && existing.AuthorUserId != User.GetUserId())
                return StatusCode(403, new { error = "Vous ne pouvez modifier que vos propres contenus." });

            if (!string.IsNullOrWhiteSpace(request.Title)) existing.Title = request.Title.Trim();
            if (request.Price.HasValue)
                existing.Price = decimal.Round(request.Price.Value, 0, MidpointRounding.AwayFromZero);

            existing.Description = request.Description;
            existing.Category = request.Category;
            existing.Level = request.Level;
            existing.ThumbnailUrl = request.ThumbnailUrl;
            existing.UpdatedAt = DateTime.UtcNow;

            var updated = await _subjectService.UpdateSubjectAsync(existing);
            return Ok(updated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la mise à jour du cours {SubjectId}", id);
            return StatusCode(500, "Erreur serveur");
        }
    }

    /// <summary>
    /// Suppression d'une matière  réservée à l'auteur du contenu et à
    /// l'administrateur (Module 17).
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = "InstructorOnly")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var existing = await _subjectService.GetSubjectByIdAsync(id);
            if (existing == null) return NotFound();

            if (!User.IsAdmin() && existing.AuthorUserId != User.GetUserId())
                return StatusCode(403, new { error = "Vous ne pouvez supprimer que vos propres contenus." });

            // Module 21 (décision §4.F/§6.4) : la suppression coupe l'accès
            // pour tout le monde, y compris les acheteurs passés, mais plus
            // silencieusement. Avant de marquer le contenu supprimé (ce qui le
            // fait sortir du filtre de la bibliothèque), chaque acheteur est
            // remboursé par crédit de portefeuille (jamais par tentative de
            // remboursement réel vers son moyen de paiement d'origine) et le
            // revenu déjà compté pour l'auteur est contre-passé.
            var paidItems = await _context.OrderItems.AsNoTracking()
                .Where(oi => oi.SubjectId == id && PaidOrderStatus.All.Contains(oi.Order.Status.ToLower()))
                .Select(oi => new { oi.Id, oi.PriceAtPurchase, BuyerId = oi.Order.UserId, oi.Order.OrderNumber })
                .ToListAsync();

            foreach (var item in paidItems.Where(i => i.BuyerId.HasValue))
            {
                try
                {
                    await _wallet.PostAsync(new WalletEntry(item.BuyerId, WalletEntryTypes.ContentRemovalRefund,
                        item.PriceAtPurchase, $"content_removal_refund:orderitem:{item.Id}",
                        $"Remboursement : « {existing.Title} » retiré de la vente", "OrderItem", item.Id));

                    var saleEntry = await _wallet.FindByKeyAsync(WalletService.CatalogSaleKey(item.Id));
                    if (saleEntry != null)
                        await _wallet.ReverseAsync(saleEntry.Id, $"Contenu retiré de la vente : « {existing.Title} »");

                    await _ntfy.PublishAsync($"winplus-user-{item.BuyerId}", "Contenu retiré de la vente",
                        $"« {existing.Title} » n'est plus disponible sur WinPlus. Le montant payé ({item.PriceAtPurchase:0} XAF) " +
                        "a été crédité sur votre portefeuille WinPlus.",
                        userId: item.BuyerId, type: "ContentRemoved", relatedEntityType: "subject", relatedEntityId: id);
                }
                catch (Exception ex)
                {
                    // Un échec sur un acheteur ne doit pas empêcher la
                    // suppression ni bloquer le traitement des autres : il est
                    // journalisé pour reprise manuelle (pas de réconciliation
                    // automatique dédiée à ce cas, contrairement au journal de
                    // vente lui-même).
                    _logger.LogError(ex,
                        "Remboursement/contre-passation non posés pour l'acheteur {BuyerId} du contenu {SubjectId} supprimé (OrderItem {OrderItemId})",
                        item.BuyerId, id, item.Id);
                }
            }

            var result = await _subjectService.DeleteSubjectAsync(id);
            if (!result)
                return NotFound();
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la suppression du cours {SubjectId}", id);
            return StatusCode(500, "Erreur serveur");
        }
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string? q, 
        [FromQuery] int? limit = 50, 
        [FromQuery] string? sort = null,
        [FromQuery] bool? isFree = null)
    {
        try
        {
            var results = await _subjectService.SearchSubjectsAsync(q ?? "");

            var (viewerId, viewerIsAdmin) = CurrentViewer();
            results = results.Where(s => IsVisibleToViewer(s, viewerId, viewerIsAdmin));
            
            // ✅ AJOUTÉ: Filtre par isFree (prix = 0)
            if (isFree.HasValue && isFree.Value)
            {
                results = results.Where(s => s.Price == 0 || s.Price == null);
            }
            
            // Apply sorting if specified
            if (!string.IsNullOrEmpty(sort))
            {
                results = sort.ToLower() switch
                {
                    "popular" => results.OrderByDescending(s => s.EnrollmentCount),
                    "newest" => results.OrderByDescending(s => s.CreatedAt),
                    "alphabetical" => results.OrderBy(s => s.Title),
                    "rating" => results.OrderByDescending(s => s.AverageRating),
                    _ => results
                };
            }
            
            // Apply limit
            if (limit.HasValue && limit.Value > 0)
            {
                results = results.Take(limit.Value);
            }
            
            return Ok(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la recherche de cours");
            return StatusCode(500, "Erreur serveur");
        }
    }

    /// <summary>
    /// Obtenir les cours populaires
    /// GET /api/subjects/popular
    /// </summary>
    [HttpGet("popular")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPopular([FromQuery] int limit = 10)
    {
        try
        {
            _logger.LogInformation("Récupération des {Limit} cours populaires", limit);
            
            var (viewerId, viewerIsAdmin) = CurrentViewer();
            limit = ClampListLimit(limit);
            var popular = await VisibleSubjectsQuery(viewerId, viewerIsAdmin)
                .OrderByDescending(s => s.EnrollmentCount)
                .Take(limit)
                .ToListAsync();

            // Enrichir avec viewCount calculé depuis LearningHistories
            var enrichedSubjects = new List<dynamic>();
            foreach (var subject in popular)
            {
                var viewCount = await _context.LearningHistories
                    .Where(l => l.SubjectId == subject.Id)
                    .CountAsync();

                enrichedSubjects.Add(new
                {
                    id = subject.Id,
                    title = subject.Title,
                    description = subject.Description,
                    category = subject.Category,
                    thumbnailUrl = subject.ThumbnailUrl,
                    price = subject.Price,
                    isPublished = subject.IsPublished,
                    enrollmentCount = subject.EnrollmentCount,
                    averageRating = subject.AverageRating,
                    totalRatings = subject.TotalRatings,
                    viewCount = viewCount,
                    downloadCount = subject.DownloadCount ?? 0,
                    isFeatured = subject.IsFeatured
                });
            }

            return Ok(enrichedSubjects);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération des cours populaires");
            return StatusCode(500, "Erreur serveur");
        }
    }

    /// <summary>
    /// Obtenir les cours en vedette (featured)
    /// GET /api/subjects/featured
    /// </summary>
    [HttpGet("featured")]
    [ProducesResponseType(typeof(List<Subject>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFeatured([FromQuery] int limit = 10)
    {
        try
        {
            _logger.LogInformation("Récupération des {Limit} cours en vedette", limit);
            var (viewerId, viewerIsAdmin) = CurrentViewer();
            limit = ClampListLimit(limit);
            return Ok(await VisibleSubjectsQuery(viewerId, viewerIsAdmin)
                .Where(s => s.IsFeatured)
                .Include(s => s.Contents)
                .OrderByDescending(s => s.CreatedAt)
                .Take(limit)
                .ToListAsync());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération des cours en vedette");
            return StatusCode(500, "Erreur serveur");
        }
    }

    /// <summary>
    /// Obtenir les cours récents
    /// GET /api/subjects/recent
    /// </summary>
    [HttpGet("recent")]
    [ProducesResponseType(typeof(List<Subject>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRecent([FromQuery] int limit = 10)
    {
        try
        {
            _logger.LogInformation("Récupération des {Limit} cours récents", limit);
            var (viewerId, viewerIsAdmin) = CurrentViewer();
            limit = ClampListLimit(limit);
            return Ok(await VisibleSubjectsQuery(viewerId, viewerIsAdmin)
                .Include(s => s.Contents)
                .OrderByDescending(s => s.CreatedAt)
                .Take(limit)
                .ToListAsync());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération des cours récents");
            return StatusCode(500, "Erreur serveur");
        }
    }

    /// <summary>
    /// Obtenir les cours par catégorie
    /// GET /api/subjects/by-category/{categoryName}
    /// </summary>
    [HttpGet("by-category/{name}")]
    [ProducesResponseType(typeof(PaginationResponse<Subject>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByCategory(
        string name,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            _logger.LogInformation("Récupération des cours pour la catégorie {Category}", name);
            var results = await _subjectService.GetSubjectsByCategoryAsync(name);

            var (viewerId, viewerIsAdmin) = CurrentViewer();
            results = results.Where(s => IsVisibleToViewer(s, viewerId, viewerIsAdmin));

            var paginated = results
                .OrderByDescending(s => s.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var response = new PaginationResponse<Subject>(paginated, results.Count(), page, pageSize);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération des cours par catégorie");
            return StatusCode(500, "Erreur serveur");
        }
    }

    /// <summary>
    /// Cours ayant au moins une épreuve du type d'examen donné (BEPC, Probatoire,
    /// Baccalauréat...). Le Subject n'a pas lui-même de champ "type d'examen" :
    /// cette information vit sur les Exam liés (Exam.SubjectId). Avant cet
    /// endpoint, le catalogue combinait un filtre matière (sur Subject.Category)
    /// avec un filtre examen tiré de /api/exams/by-type/{type}  deux jeux de
    /// données disjoints, sans lien entre les Subject affichés et les Exam
    /// renvoyés, d'où des combinaisons de filtres qui ne retrouvaient jamais
    /// les épreuves pourtant bien liées en base (ex: Mathématiques + BEPC).
    /// GET /api/subjects/by-exam-type/{examType}
    /// </summary>
    [HttpGet("by-exam-type/{examType}")]
    [ProducesResponseType(typeof(PaginationResponse<Subject>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByExamType(
        string examType,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            if (page < 1) page = 1;
            pageSize = Math.Clamp(pageSize, 1, 500);

            var (viewerId, viewerIsAdmin) = CurrentViewer();
            var query = _context.Subjects
                .Where(s => !s.IsDeleted && _context.Exams.Any(e =>
                    e.SubjectId == s.Id && !e.IsDeleted &&
                    e.ExamType.ToLower() == examType.ToLower()))
                .Where(s => s.IsPublished || viewerIsAdmin || (viewerId != null && s.AuthorUserId == viewerId))
                .OrderByDescending(s => s.CreatedAt);

            var totalCount = await query.CountAsync();
            var subjects = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            var subjectIds = subjects.Select(s => s.Id).ToList();
            var matchingExams = await _context.Exams.AsNoTracking()
                .Where(e => e.SubjectId != null && subjectIds.Contains(e.SubjectId.Value) && !e.IsDeleted
                            && e.ExamType.ToLower() == examType.ToLower())
                .ToListAsync();
            var examMap = matchingExams
                .GroupBy(e => e.SubjectId!.Value)
                .Select(g => g.OrderByDescending(e => e.CreatedAt).First())
                .ToDictionary(e => e.SubjectId!.Value);
            var projected = subjects.Select(s => ProjectSubject(s, examMap.GetValueOrDefault(s.Id))).ToList();

            var response = new PaginationResponse<object>(projected, totalCount, page, pageSize);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération des cours par type d'examen {ExamType}", examType);
            return StatusCode(500, "Erreur serveur");
        }
    }

    /// <summary>
    /// Récupère toutes les catégories disponibles
    /// </summary>
    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories([FromQuery] string? level = null)
    {
        try
        {
            var categories = await _subjectService.GetCategoriesAsync(level);
            return Ok(categories);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération des catégories");
            return StatusCode(500, "Erreur serveur");
        }
    }

    /// <summary>
    /// Récupère les filtres disponibles pour la recherche
    /// </summary>
    [HttpGet("filters")]
    public async Task<IActionResult> GetFilters()
    {
        try
        {
            var filters = await _subjectService.GetFiltersAsync();
            return Ok(filters);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération des filtres");
            return StatusCode(500, "Erreur serveur");
        }
    }

    /// <summary>
    /// Document que la visionneuse peut afficher pour un sujet du catalogue
    /// (Module 44, décision §11.1) : l'énoncé d'une épreuve, son corrigé, ou
    /// le fichier d'un livre. Les leçons de formation (Course/CourseLesson,
    /// Module 6/27) ne passent jamais par ici.
    /// </summary>
    private enum ViewerDocumentKind { Document, Correction }

    private sealed record ViewerDocument(Subject Subject, Exam? Exam, string StorageUrl, ViewerDocumentKind Kind);

    /// <summary>
    /// Vérifie qu'un utilisateur a le droit de consulter un sujet (gratuit,
    /// abonné, acheté ou assigné via une classe  Module 17) et retrouve le
    /// fichier demandé. Seul point d'entrée de la visionneuse : aucune route
    /// ne renvoie plus l'adresse du fichier au client.
    ///
    /// Sources, dans l'ordre :
    ///   - énoncé : Exam.DocumentUrl de l'épreuve la plus récente, sinon le
    ///     CourseContent.DocumentUrl du sujet (livre du catalogue, créé par
    ///     AdminLibraryController) ;
    ///   - corrigé : Exam.CorrectionUrl de l'épreuve la plus récente qui en a un.
    /// </summary>
    private async Task<(ViewerDocument? doc, IActionResult? error)> ResolveViewerDocumentAsync(int id, ViewerDocumentKind kind)
    {
        var subject = await _subjectService.GetSubjectByIdAsync(id);
        if (subject == null)
            return (null, NotFound(new { error = "Document introuvable." }));

        // Un brouillon n'est consultable que par son auteur ou un
        // administrateur, comme dans le catalogue (Module 17).
        var (viewerId, viewerIsAdmin) = CurrentViewer();
        if (!IsVisibleToViewer(subject, viewerId, viewerIsAdmin))
            return (null, NotFound(new { error = "Document introuvable." }));

        if (subject.Price > 0)
        {
            int userId;
            try { userId = User.GetUserId(); }
            catch (UnauthorizedAccessException)
            {
                return (null, StatusCode(401, new { error = "Veuillez vous connecter pour accéder à ce document." }));
            }

            if (!await _contentAccess.HasPaidContentAccessAsync(userId, subject, User.IsAdmin()))
                return (null, StatusCode(403, new { error = "Veuillez acheter ce contenu pour pouvoir y accéder." }));
        }

        if (kind == ViewerDocumentKind.Correction)
        {
            var examWithCorrection = await _context.Exams
                .Where(e => e.SubjectId == id && !e.IsDeleted && e.CorrectionUrl != null && e.CorrectionUrl != "")
                .OrderByDescending(e => e.CreatedAt)
                .FirstOrDefaultAsync();

            if (examWithCorrection == null)
                return (null, NotFound(new { error = "Le corrigé n'est pas encore disponible pour cette épreuve." }));

            return (new ViewerDocument(subject, examWithCorrection, examWithCorrection.CorrectionUrl!, kind), null);
        }

        var exam = await _context.Exams
            .Where(e => e.SubjectId == id && !e.IsDeleted && e.DocumentUrl != null && e.DocumentUrl != "")
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync();

        if (exam != null)
            return (new ViewerDocument(subject, exam, exam.DocumentUrl!, kind), null);

        // Livre du catalogue : pas d'Exam, le fichier vit sur le CourseContent.
        var bookUrl = await _context.CourseContents.AsNoTracking()
            .Where(c => c.SubjectId == id && c.DocumentUrl != null && c.DocumentUrl != "")
            .OrderBy(c => c.OrderIndex).ThenBy(c => c.Id)
            .Select(c => c.DocumentUrl)
            .FirstOrDefaultAsync();

        if (!string.IsNullOrEmpty(bookUrl))
            return (new ViewerDocument(subject, null, bookUrl, kind), null);

        return (null, NotFound(new { error = "Le fichier n'est pas encore disponible pour ce contenu." }));
    }

    /// <summary>
    /// Libellé du filigrane : identité du compte connecté, lue en base (jamais
    /// fournie par le client), plus la date de consultation. L'identifiant
    /// interne permet de retrouver le compte même si le nom a été recadré.
    /// </summary>
    private async Task<string> BuildWatermarkLabelAsync(int userId)
    {
        var user = await _context.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.FirstName, u.LastName, u.Email })
            .FirstOrDefaultAsync();

        var name = $"{user?.FirstName} {user?.LastName}".Trim();
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(name)) parts.Add(name);
        if (!string.IsNullOrWhiteSpace(user?.Email)) parts.Add(user!.Email);
        parts.Add($"#{userId}");
        parts.Add(DateTime.UtcNow.ToString("dd/MM/yyyy HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture));
        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Sert le PDF d'une épreuve, de son corrigé ou d'un livre à la
    /// visionneuse intégrée (web : pdf.js, mobile : lecteur embarqué), avec un
    /// filigrane nominatif incrusté côté serveur sur chaque page (Module 44,
    /// décisions §11.3/§11.4). Aucune adresse de fichier n'est jamais exposée :
    /// le client reçoit les octets via une requête authentifiée, pas un lien
    /// enregistrable ou partageable. Ce n'est pas un verrou absolu (capture
    /// d'écran toujours possible) : le filigrane en trace alors l'origine.
    ///
    /// GET /api/subjects/{id}/view?kind=document|correction&amp;preview=true|false
    /// </summary>
    [HttpGet("{id}/view")]
    [Authorize]
    public async Task<IActionResult> ViewStream(int id, [FromQuery] bool preview = false, [FromQuery] string? kind = null)
    {
        var docKind = string.Equals(kind, "correction", StringComparison.OrdinalIgnoreCase)
            ? ViewerDocumentKind.Correction
            : ViewerDocumentKind.Document;

        var (doc, error) = await ResolveViewerDocumentAsync(id, docKind);
        if (error != null) return error;

        var userId = User.GetUserId();

        byte[] stamped;
        try
        {
            var bucket = _storage.Bucket;
            var s3Key = ExtractS3Key(doc!.StorageUrl, bucket);

            using var s3 = _storage.CreateS3Client();
            using var obj = await s3.GetObjectAsync(bucket, s3Key, HttpContext.RequestAborted);

            // Le filigrane est obligatoire : si l'incrustation échoue, on
            // refuse la consultation plutôt que de servir le fichier d'origine.
            var label = await BuildWatermarkLabelAsync(userId);
            stamped = await _watermark.StampAsync(obj.ResponseStream, label, HttpContext.RequestAborted);
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la préparation du document {Kind} du sujet {SubjectId}", docKind, id);
            return StatusCode(500, new { error = "Impossible de charger le document pour le moment." });
        }

        // Une ligne par (utilisateur, épreuve), pas par consultation :
        // rouvrir la visionneuse plusieurs fois gonflait l'historique et
        // le compteur « Épreuves téléchargées » à l'infini. C'est aussi
        // l'hypothèse déjà faite ailleurs (GetExamsRecommended exclut les
        // épreuves « déjà téléchargées » par un simple test d'existence).
        //
        // preview=true : appel silencieux de génération de vignette
        // (RealPagePreview du catalogue charge les 2 premières pages de
        // chaque épreuve gratuite dès l'affichage de la carte, sans que
        // l'élève n'ait rien demandé)  ça ne doit jamais compter comme une
        // consultation ni apparaître dans l'historique. Le corrigé n'est
        // pas compté non plus : il accompagne une épreuve déjà comptée.
        if (!preview && docKind == ViewerDocumentKind.Document)
        {
            try
            {
                var examId = doc!.Exam?.Id;
                var alreadyLogged = await _context.DownloadHistories
                    .AnyAsync(d => d.UserId == userId && d.SubjectId == id && d.ExamId == examId);
                if (!alreadyLogged)
                {
                    if (doc.Exam != null) doc.Exam.DownloadCount += 1;
                    _context.DownloadHistories.Add(new DownloadHistory
                    {
                        UserId = userId,
                        SubjectId = id,
                        ExamId = examId,
                        FileName = $"{doc.Subject.Title}.pdf",
                        CreatedAt = DateTime.UtcNow,
                    });
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                // L'historique est une statistique : son échec ne doit pas
                // priver l'utilisateur d'un document auquel il a droit.
                _logger.LogWarning(ex, "Historique de consultation non enregistré pour le sujet {SubjectId}", id);
            }
        }

        // Pas de Content-Disposition: attachment ni de nom de fichier : le
        // document reste « en ligne », destiné à la visionneuse. no-store :
        // aucune copie en cache HTTP (navigateur, proxy) au-delà de
        // l'affichage en cours.
        Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, private";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(stamped, "application/pdf");
    }

    /// <summary>
    /// Ancienne route de téléchargement (URL S3 présignée de 15 min, avec
    /// repli sur l'URL brute du fichier). Retirée par la décision §11.4 :
    /// épreuves, corrigés et livres ne sont plus jamais téléchargeables, pour
    /// personne, y compris les abonnés. Conservée uniquement pour répondre
    /// explicitement aux anciennes versions des applications au lieu d'une
    /// 404/405 muette  elle ne renvoie jamais d'adresse de fichier.
    /// GET|POST /api/subjects/{id}/download → 410
    /// </summary>
    [AcceptVerbs("GET", "POST", Route = "{id}/download")]
    public IActionResult Download(int id) =>
        StatusCode(StatusCodes.Status410Gone, new
        {
            error = "Le téléchargement des épreuves, corrigés et livres n'est plus disponible. Ouvrez le document dans la visionneuse de l'application.",
            viewerAvailable = true,
        });

    private static string ExtractS3Key(string documentUrl, string bucket)
    {
        if (documentUrl.StartsWith("s3://"))
        {
            var uri = new Uri(documentUrl);
            return uri.AbsolutePath.TrimStart('/');
        }
        if (documentUrl.StartsWith("http"))
        {
            var uri = new Uri(documentUrl);
            return uri.AbsolutePath.TrimStart('/');
        }
        return documentUrl;
    }

    /// <summary>
    /// Récupère les cours similaires  proxy vers le service Python IA
    /// </summary>
    [HttpGet("{id}/similar")]
    public async Task<IActionResult> GetSimilar(int id, [FromQuery] int limit = 5)
    {
        try
        {
            var result = await _fastApiClient.GetAsync<PythonRecsResponse>($"/api/recommendations/{id}?limit={limit}");
            if (result?.Recommendations != null)
                return Ok(result.Recommendations);

            // Fallback: similarity par catégorie depuis la DB locale
            var similar = await _subjectService.GetSimilarSubjectsAsync(id, limit);
            return Ok(similar);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération des cours similaires pour {SubjectId}", id);
            return StatusCode(500, "Erreur serveur");
        }
    }
}
