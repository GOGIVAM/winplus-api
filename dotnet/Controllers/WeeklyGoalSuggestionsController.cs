using Backend.Data;
using Backend.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace Backend.Controllers;

/// <summary>
/// Suggestions d'objectifs hebdomadaires (section « Objectifs » du dashboard).
///
///   GET /api/users/me/weekly-goal/suggestions
///
/// Complète WeeklyGoalController, qui porte déjà GET/PUT/DELETE sur
/// /api/users/me/weekly-goal : rien n'est dupliqué ici, ni entité, ni table, ni
/// route  seule la suggestion manquait.
///
/// La réponse contient toujours trois objectifs applicables en un clic. On
/// interroge d'abord WinAI (FastAPI, POST /api/ai/goal-suggestions, budget 6 s) ;
/// en cas d'échec les suggestions sont calculées ici. Le champ `aiPowered` dit
/// lequel des deux a répondu, et le front affiche « calcul local » quand il vaut
/// false : la section n'est jamais vide, même sans service IA déployé.
/// </summary>
[ApiController]
[Route("api/users/me/weekly-goal")]
[Authorize]
public class WeeklyGoalSuggestionsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WeeklyGoalSuggestionsController> _logger;

    public WeeklyGoalSuggestionsController(
        ApplicationDbContext db,
        IHttpClientFactory httpClientFactory,
        ILogger<WeeklyGoalSuggestionsController> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public record Suggestion(string Title, string Reason, int StudyHoursTarget, int QuizTarget, int DownloadsTarget);

    private static DateTime CurrentWeekStart()
    {
        var today = DateTime.UtcNow.Date;
        var delta = ((int)today.DayOfWeek + 6) % 7;   // lundi = 0
        return DateTime.SpecifyKind(today.AddDays(-delta), DateTimeKind.Utc);
    }

    [HttpGet("suggestions")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var weekStart = CurrentWeekStart();
        var weekEnd = weekStart.AddDays(7);

        var level = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.Level)
            .FirstOrDefaultAsync(ct);

        // Réalisé de la semaine  mêmes sources que WeeklyGoalController.
        var minutes = await _db.StudySessions
            .Where(s => s.UserId == userId && s.CreatedAt >= weekStart && s.CreatedAt < weekEnd)
            .SumAsync(s => (int?)s.Duration, ct) ?? 0;

        var quizDone = await _db.QuizAttempts
            .CountAsync(q => q.UserId == userId && q.IsCompleted
                          && q.CompletedAt >= weekStart && q.CompletedAt < weekEnd, ct);

        var downloadsDone = await _db.DownloadHistories
            .CountAsync(d => d.UserId == userId
                          && d.CreatedAt >= weekStart && d.CreatedAt < weekEnd, ct);

        // Matières où la moyenne des tentatives reste sous 55 %.
        var weak = await _db.QuizAttempts
            .Where(a => a.UserId == userId && a.IsCompleted)
            .Join(_db.Quizzes, a => a.QuizId, q => q.Id, (a, q) => new { q.Subject, a.Score })
            .GroupBy(x => x.Subject)
            .Select(g => new { Subject = g.Key, Avg = g.Average(x => x.Score) })
            .Where(x => x.Avg < 55m)
            .OrderBy(x => x.Avg)
            .Take(4)
            .Select(x => x.Subject)
            .ToListAsync(ct);

        var done = new { studyHours = Math.Round(minutes / 60d, 1), quiz = quizDone, downloads = downloadsDone };

        // 1. WinAI. Budget serré : la section doit s'afficher tout de suite.
        try
        {
            var client = _httpClientFactory.CreateClient("FastApiClient");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(6));

            var res = await client.PostAsJsonAsync("/api/ai/goal-suggestions", new
            {
                user_id = userId,
                level,
                weak_subjects = weak,
                done_this_week = done,
            }, cts.Token);

            if (res.IsSuccessStatusCode)
            {
                var payload = await res.Content.ReadFromJsonAsync<AiResponse>(cancellationToken: cts.Token);
                if (payload?.Suggestions is { Count: > 0 })
                {
                    return Ok(new
                    {
                        success = true,
                        aiPowered = true,
                        data = new { level, weakSubjects = weak, done, suggestions = payload.Suggestions, insight = payload.Insight },
                    });
                }
            }
            else
            {
                _logger.LogInformation("WinAI goal-suggestions a répondu {Code}, repli local.", (int)res.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "WinAI goal-suggestions indisponible, repli local.");
        }

        // 2. Repli déterministe, mêmes champs.
        var isExamYear = level != null && Regex.IsMatch(
            level, "terminale|tle|3e|3eme|3ème|bac|bepc|concours", RegexOptions.IgnoreCase);

        var weakHead = string.Join(" et ", weak.Take(2));
        var weakList = string.Join(", ", weak.Take(3));

        var suggestions = new List<Suggestion>
        {
            new(
                isExamYear ? "Rythme examen" : "Rythme régulier",
                isExamYear
                    ? "Année d'examen : volume soutenu et révisions quotidiennes."
                    : "Un socle tenable sur toute l'année, sans surcharge.",
                isExamYear ? 12 : 7, isExamYear ? 5 : 3, isExamYear ? 4 : 2),
            new(
                weak.Count > 0 ? "Rattrapage ciblé" : "Consolidation",
                weak.Count > 0
                    ? "Concentré sur " + weakHead + ", vos matières les plus fragiles."
                    : "Entretenir les acquis avec deux séances de quiz par semaine.",
                weak.Count > 0 ? 10 : 5, weak.Count > 0 ? 6 : 2, 3),
            quizDone == 0
                ? new("Reprise en douceur",
                      "Aucun quiz cette semaine : repartir petit vaut mieux que ne pas repartir.",
                      3, 1, 1)
                : new("Palier suivant",
                      quizDone + " quiz déjà passés cette semaine : monter d'un cran reste atteignable.",
                      9, quizDone + 2, 3),
        };

        return Ok(new
        {
            success = true,
            aiPowered = false,
            data = new
            {
                level,
                weakSubjects = weak,
                done,
                suggestions,
                insight = weak.Count > 0
                    ? "Vos résultats les plus faibles sont en " + weakList
                      + ". Un objectif qui y consacre deux séances par semaine est le plus rentable."
                    : "Aucune matière en difficulté marquée : un objectif de maintien suffit cette semaine.",
            },
        });
    }

    private class AiResponse
    {
        public List<object>? Suggestions { get; set; }
        public string? Insight { get; set; }
    }
}
