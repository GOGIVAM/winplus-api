using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Models.Entities;

namespace Backend.Services;

/// <summary>
/// Service hébergé (professeur_complete.md Module 9, US-FOR-07 ; étendu Module 8, 8A) :
/// alerte quotidiennement chaque professeur du nombre d'élèves inactifs sur chacune
/// de ses formations publiées, au-delà du seuil configuré par formation
/// (Course.InactivityThresholdDays, 7 par défaut), et détecte le décrochage (inactivité
/// combinée à une baisse de score de quiz) via FastAPI /api/winai/detection-decrochage,
/// en persistant les alertes dans AlertesDecrochage pour une vue transversale
/// toutes formations (TeacherController.GetAlertesDecrochage). Tourne une fois par
/// jour — cette cadence suffit à elle seule à éviter les alertes en double pour la
/// notification ntfy ; les alertes de décrochage, elles, sont dédupliquées via un
/// index unique partiel (CourseId, StudentUserId) WHERE Traitee = FALSE.
/// </summary>
public sealed class CourseInactivityAlertService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CourseInactivityAlertService> _logger;

    public CourseInactivityAlertService(IServiceScopeFactory scopeFactory, ILogger<CourseInactivityAlertService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CourseInactivityAlertService started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "CourseInactivityAlertService run failed."); }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ntfy = scope.ServiceProvider.GetRequiredService<INtfyService>();
        var httpFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();

        var courses = await db.Courses.AsNoTracking()
            .Where(c => c.Status == "published")
            .Select(c => new { c.Id, c.Title, c.InstructorId, c.InactivityThresholdDays })
            .ToListAsync(ct);

        foreach (var course in courses)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var enrollments = await db.CourseEnrollments.AsNoTracking()
                    .Where(e => e.CourseId == course.Id && e.IsActive && e.CompletedAt == null)
                    .Include(e => e.User)
                    .Select(e => new { e.UserId, e.EnrolledAt, Name = e.User!.FirstName + " " + e.User!.LastName })
                    .ToListAsync(ct);
                if (enrollments.Count == 0) continue;

                var userIds = enrollments.Select(e => e.UserId).ToList();
                var lastProgressByUser = await db.LessonProgress.AsNoTracking()
                    .Where(p => p.CourseId == course.Id && userIds.Contains(p.UserId))
                    .GroupBy(p => p.UserId)
                    .Select(g => new { UserId = g.Key, Last = g.Max(p => p.UpdatedAt) })
                    .ToDictionaryAsync(x => x.UserId, x => x.Last, ct);

                var now = DateTime.UtcNow;
                var inactiveCount = enrollments.Count(e =>
                {
                    var last = lastProgressByUser.TryGetValue(e.UserId, out var l) ? l : e.EnrolledAt;
                    return (now - last).TotalDays >= course.InactivityThresholdDays;
                });

                if (inactiveCount > 0)
                {
                    await ntfy.PublishAsync($"winplus-user-{course.InstructorId}",
                        "Élèves inactifs",
                        $"{inactiveCount} élève{(inactiveCount > 1 ? "s sont inactifs" : " est inactif")} depuis {course.InactivityThresholdDays} jours sur « {course.Title} ».",
                        userId: course.InstructorId, type: "CourseInactivity");
                }

                await DetectDropoutAsync(db, httpFactory, course.Id, course.Title, enrollments.Select(e => (e.UserId, e.Name, e.EnrolledAt)).ToList(), lastProgressByUser, now, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed inactivity check for course {CourseId}", course.Id);
            }
        }
    }

    /// <summary>
    /// Calcule les signaux d'engagement (jours d'inactivité + tendance des scores de
    /// quiz de la formation), les envoie à FastAPI pour scoring, et persiste une
    /// AlerteDecrochage pour chaque élève à risque n'ayant pas déjà une alerte non
    /// traitée pour cette formation (Module 8, 8A).
    /// </summary>
    private async Task DetectDropoutAsync(
        ApplicationDbContext db,
        IHttpClientFactory httpFactory,
        int courseId,
        string courseTitle,
        List<(int UserId, string Name, DateTime EnrolledAt)> enrollments,
        Dictionary<int, DateTime> lastProgressByUser,
        DateTime now,
        CancellationToken ct)
    {
        // Pré-filtre : seuls les élèves inactifs depuis au moins 7 jours peuvent
        // déclencher une alerte côté scoring — inutile d'envoyer les autres.
        var atRiskCandidates = enrollments
            .Select(e => new
            {
                e.UserId,
                e.Name,
                DaysInactive = (int)(now - (lastProgressByUser.TryGetValue(e.UserId, out var l) ? l : e.EnrolledAt)).TotalDays,
            })
            .Where(e => e.DaysInactive >= 7)
            .ToList();
        if (atRiskCandidates.Count == 0) return;

        var quizIds = await db.CourseLessons.AsNoTracking()
            .Where(l => l.CourseId == courseId && l.QuizId != null)
            .Select(l => l.QuizId!.Value)
            .Distinct()
            .ToListAsync(ct);

        var candidateUserIds = atRiskCandidates.Select(c => c.UserId).ToList();
        var recentScoresByUser = new Dictionary<int, List<float>>();
        if (quizIds.Count > 0)
        {
            var attempts = await db.QuizAttempts.AsNoTracking()
                .Where(a => quizIds.Contains(a.QuizId) && candidateUserIds.Contains(a.UserId))
                .OrderBy(a => a.CompletedAt)
                .Select(a => new { a.UserId, a.Score, a.CompletedAt })
                .ToListAsync(ct);
            foreach (var group in attempts.GroupBy(a => a.UserId))
                recentScoresByUser[group.Key] = group.TakeLast(5).Select(a => (float)a.Score).ToList();
        }

        var payload = new
        {
            course_title = courseTitle,
            students = atRiskCandidates.Select(c => new
            {
                user_id = c.UserId,
                name = c.Name,
                days_inactive = c.DaysInactive,
                recent_quiz_scores = recentScoresByUser.TryGetValue(c.UserId, out var scores) ? scores : new List<float>(),
            }),
        };

        JsonElement root;
        try
        {
            var client = httpFactory.CreateClient("FastApiClient");
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/winai/detection-decrochage")
            {
                Content = JsonContent.Create(payload),
            };
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(20));
            var res = await client.SendAsync(req, cts.Token);
            if (!res.IsSuccessStatusCode) return;
            root = await res.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "detection-decrochage call failed for course {CourseId}", courseId);
            return;
        }

        if (!root.TryGetProperty("alerts", out var alertsEl) || alertsEl.ValueKind != JsonValueKind.Array) return;

        var existingUntreated = await db.AlertesDecrochage.AsNoTracking()
            .Where(a => a.CourseId == courseId && !a.Traitee)
            .Select(a => a.StudentUserId)
            .ToListAsync(ct);
        var existingSet = existingUntreated.ToHashSet();

        foreach (var alert in alertsEl.EnumerateArray())
        {
            var userId = alert.GetProperty("user_id").GetInt32();
            if (existingSet.Contains(userId)) continue; // déjà une alerte non traitée pour cet élève

            var niveau = alert.TryGetProperty("niveau", out var n) ? n.GetString() ?? "faible" : "faible";
            var signaux = alert.TryGetProperty("signaux", out var s) && s.ValueKind == JsonValueKind.Array
                ? s.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList()
                : new List<string>();

            db.AlertesDecrochage.Add(new AlerteDecrochage
            {
                CourseId = courseId,
                StudentUserId = userId,
                Niveau = niveau,
                SignauxJson = JsonSerializer.Serialize(signaux),
                DateDetection = now,
            });
        }
        await db.SaveChangesAsync(ct);
    }
}
