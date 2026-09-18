using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Models.Entities;

namespace Backend.Services;

/// <summary>
/// Contenu narratif d'un album pour un enfant, sérialisé tel quel dans
/// ParentReport.Content. Pas de score global comme le Portefeuille : l'album
/// couvre une année entière et une évolution chiffrée y a sa place
/// (WeeklyParentReportService en montre déjà, ex. "score moyen 68%").
/// </summary>
public sealed record AlbumContent(
    string SchoolYear,
    string SubjectsWorked,
    string Progression,
    List<string> TopContents,
    string GoalsSummary,
    List<string> IntensityWeeks,
    string? Bulletin
);

public sealed record ChildAlbumOutcome(int ChildId, string ChildName, bool Success, string? SkipReason, AlbumContent? Content);

public sealed record ParentAlbumResult(int ParentId, List<ChildAlbumOutcome> Children);

public interface IYearlyAlbumService
{
    /// <summary>
    /// Génère l'album de fin d'année de tous les enfants actuellement liés
    /// (accepted) à ce parent, pour l'année scolaire donnée. persist=false ne
    /// touche jamais la base (aperçu dry-run).
    /// </summary>
    Task<ParentAlbumResult> GenerateAlbumForParentAsync(int parentId, string schoolYear, bool persist, CancellationToken ct = default);
}

public sealed class YearlyAlbumService : IYearlyAlbumService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<YearlyAlbumService> _logger;

    public YearlyAlbumService(ApplicationDbContext db, ILogger<YearlyAlbumService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Accepte "2024-2025", "2024/2025", "24-25", "24/25" (espaces tolérés).
    /// Rejette tout ce qui n'est pas deux années consécutives  une saisie
    /// libre du même champ que AcademicRecord.SchoolYear, jamais fiable telle
    /// quelle.
    /// </summary>
    public static (int Start, int End)? NormalizeSchoolYear(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var cleaned = raw.Trim().Replace("/", "-").Replace(" ", "");
        var parts = cleaned.Split('-');
        if (parts.Length != 2) return null;
        if (!TryParseYearPart(parts[0], out var start)) return null;
        if (!TryParseYearPart(parts[1], out var end)) return null;
        if (start < 100) start += 2000;
        if (end < 100) end += 2000;
        if (end != start + 1) return null;
        return (start, end);
    }

    private static bool TryParseYearPart(string s, out int year)
        => int.TryParse(s, out year) && year is > 0 and < 10000;

    public async Task<ParentAlbumResult> GenerateAlbumForParentAsync(int parentId, string schoolYear, bool persist, CancellationToken ct = default)
    {
        var normalized = NormalizeSchoolYear(schoolYear)
            ?? throw new ArgumentException($"Année scolaire mal formée : « {schoolYear} ». Format attendu : 2024-2025.");

        var periodStart = new DateTime(normalized.Start, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = new DateTime(normalized.End, 7, 31, 23, 59, 59, DateTimeKind.Utc);
        var canonicalSchoolYear = $"{normalized.Start}-{normalized.End}";

        var links = await _db.ParentStudentLinks.AsNoTracking()
            .Where(l => l.ParentId == parentId)
            .ToListAsync(ct);

        var outcomes = new List<ChildAlbumOutcome>();

        foreach (var link in links)
        {
            if (ct.IsCancellationRequested) break;

            var childName = await _db.Users.AsNoTracking()
                .Where(u => u.Id == link.StudentId)
                .Select(u => u.FirstName)
                .FirstOrDefaultAsync(ct) ?? $"Enfant {link.StudentId}";

            if (link.Status != "accepted")
            {
                // Cas limite explicitement demandé : le lien existe (l'enfant a
                // été suivi à un moment) mais n'est plus accepted aujourd'hui
                // ce modèle ne distingue pas "jamais accepté" de "délié en cours
                // d'année" (pas d'historique de statut), donc les deux tombent
                // ici plutôt que d'inventer une distinction que la donnée ne
                // permet pas de faire.
                outcomes.Add(new ChildAlbumOutcome(link.StudentId, childName, false,
                    "Enfant non lié (délié ou jamais accepté) au moment de la génération.", null));
                continue;
            }

            try
            {
                var content = await BuildAlbumContentAsync(link.StudentId, canonicalSchoolYear, periodStart, periodEnd, childName, ct);

                if (persist)
                {
                    _db.ParentReports.Add(new ParentReport
                    {
                        ParentId = parentId,
                        ChildId = link.StudentId,
                        ReportType = "AlbumAnnuel",
                        Content = JsonSerializer.Serialize(content),
                        EmitterType = "System",
                    });
                }

                outcomes.Add(new ChildAlbumOutcome(link.StudentId, childName, true, null, content));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to build yearly album for child {ChildId}", link.StudentId);
                outcomes.Add(new ChildAlbumOutcome(link.StudentId, childName, false, "Erreur lors du calcul.", null));
            }
        }

        if (persist)
            await _db.SaveChangesAsync(ct);

        return new ParentAlbumResult(parentId, outcomes);
    }

    private async Task<AlbumContent> BuildAlbumContentAsync(
        int childId, string canonicalSchoolYear, DateTime periodStart, DateTime periodEnd, string childName, CancellationToken ct)
    {
        // ── Matières travaillées : contenus consultés (DownloadHistories), groupés par matière.
        var subjectTitles = await (
            from d in _db.DownloadHistories
            where d.UserId == childId && d.CreatedAt >= periodStart && d.CreatedAt <= periodEnd
            join s in _db.Subjects on d.SubjectId equals s.Id
            select s.Title
        ).Distinct().ToListAsync(ct);

        var subjectsWorked = subjectTitles.Count == 0
            ? $"{childName} n'a pas encore de contenu consulté enregistré sur cette période."
            : $"{childName} a travaillé {(subjectTitles.Count == 1 ? "la matière suivante" : $"{subjectTitles.Count} matières")} cette année : {string.Join(", ", subjectTitles)}.";

        // ── Contenus les plus consultés : top 3-5 par (SubjectId, ExamId).
        var topContents = await (
            from d in _db.DownloadHistories
            where d.UserId == childId && d.CreatedAt >= periodStart && d.CreatedAt <= periodEnd
            group d by new { d.SubjectId, d.ExamId } into g
            orderby g.Count() descending
            select new { g.Key.SubjectId, g.Key.ExamId, Count = g.Count() }
        ).Take(5).ToListAsync(ct);

        var topContentLabels = new List<string>();
        foreach (var tc in topContents)
        {
            var subjectTitle = await _db.Subjects.AsNoTracking()
                .Where(s => s.Id == tc.SubjectId).Select(s => s.Title).FirstOrDefaultAsync(ct) ?? "Matière";
            string label = subjectTitle;
            if (tc.ExamId != null)
            {
                var examTitle = await _db.Exams.AsNoTracking()
                    .Where(e => e.Id == tc.ExamId).Select(e => e.Title).FirstOrDefaultAsync(ct);
                if (!string.IsNullOrWhiteSpace(examTitle)) label = $"{examTitle} ({subjectTitle})";
            }
            topContentLabels.Add($"{label}  consulté {tc.Count} fois");
        }

        // ── Progression : score moyen des premiers quiz vs des derniers quiz de l'année.
        var quizAttempts = await _db.QuizAttempts.AsNoTracking()
            .Where(a => a.UserId == childId && a.CompletedAt >= periodStart && a.CompletedAt <= periodEnd)
            .OrderBy(a => a.CompletedAt)
            .Select(a => new { a.Score, a.CompletedAt })
            .ToListAsync(ct);

        string progression;
        if (quizAttempts.Count == 0)
        {
            progression = $"{childName} n'a pas fait de quiz enregistré sur cette période.";
        }
        else if (quizAttempts.Count < 4)
        {
            var avg = quizAttempts.Average(a => a.Score);
            progression = $"{childName} a fait {quizAttempts.Count} quiz cette année, avec un score moyen de {avg:F0}%  pas assez de quiz pour dégager une tendance.";
        }
        else
        {
            var quarter = Math.Max(1, quizAttempts.Count / 4);
            var startAvg = quizAttempts.Take(quarter).Average(a => a.Score);
            var endAvg = quizAttempts.Skip(quizAttempts.Count - quarter).Average(a => a.Score);
            var delta = endAvg - startAvg;
            var trend = delta > 3 ? "progressé" : delta < -3 ? "reculé" : "resté stable";
            progression = $"{childName} a {trend} sur l'année : score moyen de {startAvg:F0}% en début d'année à {endAvg:F0}% en fin d'année, sur {quizAttempts.Count} quiz au total.";
        }

        // ── Objectifs atteints vs non atteints (Goals.TargetDate dans la période).
        var goals = await _db.Goals.AsNoTracking()
            .Where(g => g.UserId == childId && g.TargetDate >= periodStart && g.TargetDate <= periodEnd)
            .Select(g => new { g.Title, g.Status })
            .ToListAsync(ct);

        var completedGoals = goals.Where(g => g.Status == GoalStatus.Completed).ToList();
        var otherGoals = goals.Where(g => g.Status != GoalStatus.Completed).ToList();

        string goalsSummary;
        if (goals.Count == 0)
        {
            goalsSummary = $"{childName} n'avait pas d'objectif avec une échéance sur cette année scolaire.";
        }
        else
        {
            goalsSummary = $"{childName} a atteint {completedGoals.Count} objectif{(completedGoals.Count != 1 ? "s" : "")} sur {goals.Count} cette année";
            if (completedGoals.Count > 0)
                goalsSummary += $" : {string.Join(", ", completedGoals.Take(5).Select(g => g.Title))}";
            goalsSummary += ".";
            if (otherGoals.Count > 0)
                goalsSummary += $" {otherGoals.Count} autre{(otherGoals.Count != 1 ? "s" : "")} n'{(otherGoals.Count != 1 ? "ont" : "a")} pas abouti.";
        }

        // ── Moments d'intensité : semaines (DownloadHistories + QuizAttempts) avec activité > 2x la moyenne.
        var activityDates = await _db.DownloadHistories.AsNoTracking()
            .Where(d => d.UserId == childId && d.CreatedAt >= periodStart && d.CreatedAt <= periodEnd)
            .Select(d => d.CreatedAt)
            .ToListAsync(ct);
        activityDates.AddRange(await _db.QuizAttempts.AsNoTracking()
            .Where(a => a.UserId == childId && a.CompletedAt >= periodStart && a.CompletedAt <= periodEnd)
            .Select(a => a.CompletedAt)
            .ToListAsync(ct));

        var intensityWeeks = new List<string>();
        if (activityDates.Count > 0)
        {
            var byWeek = activityDates
                .GroupBy(d => System.Globalization.ISOWeek.GetWeekOfYear(d) + d.Year * 100)
                .Select(g => new { WeekStart = g.Min(d => StartOfIsoWeek(d)), Count = g.Count() })
                .ToList();

            var averagePerWeek = byWeek.Average(w => w.Count);
            intensityWeeks = byWeek
                .Where(w => w.Count > averagePerWeek * 2)
                .OrderBy(w => w.WeekStart)
                .Select(w => $"Semaine du {w.WeekStart:d MMMM} : activité intense ({w.Count} action{(w.Count != 1 ? "s" : "")}).")
                .ToList();
        }

        // ── Bulletin : AcademicRecord dont le SchoolYear (texte libre) normalise vers la même année.
        var records = await _db.AcademicRecords.AsNoTracking()
            .Where(r => r.StudentId == childId)
            .Select(r => new { r.SchoolYear, r.AverageGrade })
            .ToListAsync(ct);

        string? bulletin = null;
        var target = NormalizeSchoolYear(canonicalSchoolYear);
        var match = records.FirstOrDefault(r => NormalizeSchoolYear(r.SchoolYear) == target);
        if (match != null)
            bulletin = $"Moyenne du bulletin pour {canonicalSchoolYear} : {match.AverageGrade:F1}/20.";

        return new AlbumContent(
            canonicalSchoolYear,
            subjectsWorked,
            progression,
            topContentLabels,
            goalsSummary,
            intensityWeeks,
            bulletin
        );
    }

    private static DateTime StartOfIsoWeek(DateTime date)
    {
        var day = (int)date.DayOfWeek;
        var diff = day == 0 ? -6 : 1 - day; // lundi = premier jour
        return date.Date.AddDays(diff);
    }
}
