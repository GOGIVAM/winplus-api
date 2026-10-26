using System.Text.Json;
using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Décision du 2026-10-26 : un examen de formation (quiz de fin de module ou
/// examen final) se repasse sans limite, mais après 3 échecs consécutifs il se
/// rouvre 24 h après le dernier échec. Pendant la pause, WinAI propose de quoi
/// combler les lacunes : plan de révision, quiz d'entraînement sur les
/// questions ratées, session de travail guidée, leçons du module à revoir.
///
/// Échec : score sous le seuil de l'examen (examen final :
/// <see cref="Course.FinalExamPassScorePercent"/> ; quiz de module : score
/// minimum du module suivant s'il en exige un, sinon 50 %). Une tentative en
/// attente de correction par le professeur (questions ouvertes) ne compte pas.
/// Les quiz hors formation ne sont pas concernés.
/// </summary>
public static class CourseExamRetakePolicy
{
    public const int FailuresBeforePause = 3;
    public static readonly TimeSpan Pause = TimeSpan.FromHours(24);
    public const decimal DefaultPassScore = 50m;

    /// <summary>Examen de formation : formation, module (null pour l'examen final) et seuil de réussite.</summary>
    public sealed record CourseExam(int CourseId, int? SectionId, bool IsFinal, decimal PassScore, string CourseTitle);

    public sealed record RetakeStatus(bool Locked, DateTime? LockedUntil, int ConsecutiveFailures, decimal PassScore);

    public sealed record WeakPoint(string Question, string? Explanation, string? Topic, int Missed);

    /// <summary>L'examen de formation que porte ce quiz, ou null pour un quiz ordinaire.</summary>
    public static async Task<CourseExam?> FindAsync(ApplicationDbContext db, int quizId, CancellationToken ct = default)
    {
        var final = await db.Courses.AsNoTracking()
            .Where(c => c.FinalExamQuizId == quizId)
            .Select(c => new { c.Id, c.Title, c.FinalExamPassScorePercent })
            .FirstOrDefaultAsync(ct);
        if (final != null)
            return new CourseExam(final.Id, null, true, final.FinalExamPassScorePercent, final.Title);

        var lesson = await db.CourseLessons.AsNoTracking()
            .Where(l => l.QuizId == quizId)
            .Select(l => new { l.CourseId, l.SectionId })
            .FirstOrDefaultAsync(ct);
        if (lesson == null) return null;

        var sections = await db.CourseSections.AsNoTracking()
            .Where(s => s.CourseId == lesson.CourseId)
            .OrderBy(s => s.Position)
            .Select(s => new { s.Id, s.UnlockRule, s.MinScore })
            .ToListAsync(ct);
        var index = sections.FindIndex(s => s.Id == lesson.SectionId);
        var next = index >= 0 && index + 1 < sections.Count ? sections[index + 1] : null;
        // Même règle que CourseAccessService : le module suivant exige ce score sur les quiz de celui-ci.
        var pass = next is { UnlockRule: "min_score", MinScore: int min } ? min : DefaultPassScore;
        var title = await db.Courses.AsNoTracking().Where(c => c.Id == lesson.CourseId).Select(c => c.Title).FirstOrDefaultAsync(ct) ?? "";
        return new CourseExam(lesson.CourseId, lesson.SectionId, false, pass, title);
    }

    /// <summary>Échecs consécutifs les plus récents et date de réouverture.</summary>
    public static async Task<RetakeStatus> StatusAsync(ApplicationDbContext db, int userId, int quizId, decimal passScore,
        DateTime now, CancellationToken ct = default)
    {
        var recent = await db.QuizAttempts.AsNoTracking()
            .Where(a => a.UserId == userId && a.QuizId == quizId && a.Status != "PendingReview")
            .OrderByDescending(a => a.CompletedAt).ThenByDescending(a => a.Id)
            .Take(FailuresBeforePause)
            .Select(a => new { a.Score, a.CompletedAt })
            .ToListAsync(ct);

        var failures = 0;
        foreach (var a in recent)
        {
            if (a.Score >= passScore) break;
            failures++;
        }
        if (failures < FailuresBeforePause) return new RetakeStatus(false, null, failures, passScore);

        var until = recent[0].CompletedAt + Pause;
        return until > now
            ? new RetakeStatus(true, until, failures, passScore)
            : new RetakeStatus(false, null, failures, passScore);
    }

    /// <summary>Questions le plus souvent ratées sur les dernières tentatives (questions à choix seulement).</summary>
    public static async Task<List<WeakPoint>> WeakPointsAsync(ApplicationDbContext db, int userId, Quiz quiz, int take = 5,
        CancellationToken ct = default)
    {
        var answersJson = await db.QuizAttempts.AsNoTracking()
            .Where(a => a.UserId == userId && a.QuizId == quiz.Id)
            .OrderByDescending(a => a.CompletedAt).ThenByDescending(a => a.Id)
            .Take(FailuresBeforePause)
            .Select(a => a.UserAnswersJson)
            .ToListAsync(ct);

        var questions = ParseQuestions(quiz.QuestionsJson);
        var missed = new Dictionary<string, int>();
        foreach (var json in answersJson)
        {
            foreach (var (questionId, answer) in ParseAnswers(json))
            {
                if (!questions.TryGetValue(questionId, out var q) || q.Correct == null) continue;
                if (!string.Equals(answer?.Trim(), q.Correct.Trim(), StringComparison.OrdinalIgnoreCase))
                    missed[questionId] = missed.GetValueOrDefault(questionId) + 1;
            }
        }
        return missed.OrderByDescending(m => m.Value)
            .Take(take)
            .Select(m => new WeakPoint(questions[m.Key].Text, questions[m.Key].Explanation, questions[m.Key].Topic, m.Value))
            .ToList();
    }

    private sealed record ParsedQuestion(string Text, string? Correct, string? Explanation, string? Topic);

    private static Dictionary<string, ParsedQuestion> ParseQuestions(string json)
    {
        var result = new Dictionary<string, ParsedQuestion>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;
            foreach (var q in doc.RootElement.EnumerateArray())
            {
                if (q.ValueKind != JsonValueKind.Object) continue;
                var id = Str(q, "id");
                var text = Str(q, "question");
                if (id == null || text == null) continue;
                if (Str(q, "type") == "open") continue;
                result[id] = new ParsedQuestion(text, Str(q, "correctAnswer"), Str(q, "explanation"), Str(q, "topic"));
            }
        }
        catch (JsonException) { }
        return result;
    }

    private static IEnumerable<(string QuestionId, string? Answer)> ParseAnswers(string json)
    {
        var list = new List<(string, string?)>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
            foreach (var a in doc.RootElement.EnumerateArray())
            {
                if (a.ValueKind != JsonValueKind.Object) continue;
                var id = Str(a, "questionId") ?? Str(a, "QuestionId");
                if (id != null) list.Add((id, Str(a, "answer") ?? Str(a, "Answer")));
            }
        }
        catch (JsonException) { }
        return list;
    }

    private static string? Str(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        };
    }
}

/// <summary>Tentative refusée pendant la pause de 24 h (réponse 423 avec la date de réouverture).</summary>
public sealed class CourseExamPausedException : InvalidOperationException
{
    public DateTime LockedUntil { get; }
    public int CourseId { get; }

    public CourseExamPausedException(DateTime lockedUntil, int courseId)
        : base("Pause de révision : cet examen se rouvre 24 h après le troisième échec.")
    {
        LockedUntil = lockedUntil;
        CourseId = courseId;
    }
}
