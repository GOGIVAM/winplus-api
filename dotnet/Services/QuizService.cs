using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Backend.Services;

/// <summary>
/// Service pour gérer les Quiz
/// </summary>
public class QuizService : IQuizService
{
    private readonly ApplicationDbContext _context;
    private readonly IFastApiClient _fastApiClient;
    private readonly ILogger<QuizService> _logger;
    private const double PASSING_SCORE = 50.0;

    // ParsePlayQuestions et SubmitQuizAttemptAsync lisent QuestionsJson avec des
    // clés minuscules ("id", "question", "correctAnswer"...), le même format que
    // renvoie Python. JsonSerializer.Serialize(objet) sans cette policy produit
    // des clés PascalCase ("Id", "Question"...) que JsonElement.GetProperty ne
    // retrouve jamais (recherche sensible à la casse)  le quiz s'enregistrait
    // sans erreur mais s'affichait vide côté élève (questions/options blanches).
    private static readonly JsonSerializerOptions CamelCaseJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public QuizService(ApplicationDbContext context, IFastApiClient fastApiClient, ILogger<QuizService> logger)
    {
        _context = context;
        _fastApiClient = fastApiClient;
        _logger = logger;
    }

    public async Task<QuizDto?> GetQuizByIdAsync(int id)
    {
        var quiz = await _context.Quizzes
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == id && !q.IsDeleted);

        return quiz != null ? MapToDto(quiz) : null;
    }

    public async Task<IEnumerable<QuizDto>> GetAllQuizzesAsync(int page = 1, int pageSize = 20)
    {
        var quizzes = await _context.Quizzes
            .AsNoTracking()
            .Where(q => !q.IsDeleted)
            .OrderByDescending(q => q.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return quizzes.Select(MapToDto);
    }

    public async Task<IEnumerable<QuizDto>> GetQuizzesAsync(QuizSearchFilterDto filter)
    {
        var query = _context.Quizzes
            .AsNoTracking()
            .Where(q => !q.IsDeleted);

        // Filtres
        if (!string.IsNullOrEmpty(filter.Subject))
            query = query.Where(q => q.Subject == filter.Subject);

        if (filter.Difficulty.HasValue)
            query = query.Where(q => q.Difficulty == filter.Difficulty.ToString());

        if (filter.MinDuration.HasValue)
            query = query.Where(q => q.TimeLimit >= filter.MinDuration);

        if (filter.MaxDuration.HasValue)
            query = query.Where(q => q.TimeLimit <= filter.MaxDuration);

        if (filter.OnlyPublished.HasValue && filter.OnlyPublished.Value)
            query = query.Where(q => q.IsPublished);

        if (!string.IsNullOrEmpty(filter.SearchTerm))
            query = query.Where(q => q.Title.Contains(filter.SearchTerm) ||
                                     q.Description != null && q.Description.Contains(filter.SearchTerm));

        // Tri
        query = (filter.SortBy?.ToLower()) switch
        {
            "title" => filter.SortOrder == "asc" ? query.OrderBy(q => q.Title) : query.OrderByDescending(q => q.Title),
            "difficulty" => filter.SortOrder == "asc" ? query.OrderBy(q => q.Difficulty) : query.OrderByDescending(q => q.Difficulty),
            "duration" => filter.SortOrder == "asc" ? query.OrderBy(q => q.TimeLimit) : query.OrderByDescending(q => q.TimeLimit),
            _ => query.OrderByDescending(q => q.CreatedAt),
        };

        var quizzes = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        return quizzes.Select(MapToDto);
    }

    public async Task<IEnumerable<QuizDto>> GetQuizzesBySubjectAsync(string subject, int page = 1, int pageSize = 20)
    {
        var quizzes = await _context.Quizzes
            .AsNoTracking()
            .Where(q => q.Subject == subject && !q.IsDeleted)
            .OrderByDescending(q => q.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return quizzes.Select(MapToDto);
    }

    public async Task<IEnumerable<QuizDto>> GetQuizzesByDifficultyAsync(string difficulty, int page = 1, int pageSize = 20)
    {
        var quizzes = await _context.Quizzes
            .AsNoTracking()
            .Where(q => q.Difficulty == difficulty && !q.IsDeleted)
            .OrderByDescending(q => q.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return quizzes.Select(MapToDto);
    }

    public async Task<IEnumerable<QuizDto>> SearchQuizzesAsync(string searchTerm, int page = 1, int pageSize = 20)
    {
        var quizzes = await _context.Quizzes
            .AsNoTracking()
            .Where(q => !q.IsDeleted &&
                       (q.Title.Contains(searchTerm) ||
                        q.Description != null && q.Description.Contains(searchTerm) ||
                        q.Subject.Contains(searchTerm)))
            .OrderByDescending(q => q.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return quizzes.Select(MapToDto);
    }

    public async Task<IEnumerable<QuizDto>> GetPublishedQuizzesAsync(int page = 1, int pageSize = 20)
    {
        var quizzes = await _context.Quizzes
            .AsNoTracking()
            .Where(q => q.IsPublished && !q.IsDeleted)
            .OrderByDescending(q => q.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return quizzes.Select(MapToDto);
    }

    public async Task<QuizResultResponseDto> SubmitQuizAttemptAsync(int quizId, int userId, SubmitQuizAttemptRequestDto request)
    {
        // Charger le quiz avec les questions
        var quiz = await _context.Quizzes
            .FirstOrDefaultAsync(q => q.Id == quizId && !q.IsDeleted);

        if (quiz == null)
            throw new KeyNotFoundException($"Quiz with id {quizId} not found");

        // Parser les questions depuis JSON
        var questionsJson = JsonDocument.Parse(quiz.QuestionsJson);
        var questions = questionsJson.RootElement.EnumerateArray().ToList();

        if (questions.Count == 0)
            throw new InvalidOperationException("Quiz has no questions");

        // Évaluer les réponses
        int correctAnswers = 0;
        var questionResults = new List<QuizQuestionResultDto>();

        for (int i = 0; i < questions.Count; i++)
        {
            var question = questions[i];
            var questionId = question.GetProperty("id").GetString();
            var correctAnswer = question.GetProperty("correctAnswer").GetString();
            var userAnswer = request.Answers.FirstOrDefault(a => a.QuestionId == questionId)?.Answer ?? "";
            var isCorrect = userAnswer.Equals(correctAnswer, StringComparison.OrdinalIgnoreCase);

            if (isCorrect)
                correctAnswers++;

            questionResults.Add(new QuizQuestionResultDto
            {
                QuestionId = questionId,
                UserAnswer = userAnswer,
                CorrectAnswer = correctAnswer,
                IsCorrect = isCorrect,
                Explanation = question.TryGetProperty("explanation", out var exp)
                    ? exp.GetString()
                    : null,
                Points = question.TryGetProperty("points", out var pts)
                    ? pts.GetInt32()
                    : 1,
            });
        }

        // Calculer le score
        decimal score = (correctAnswers / (decimal)questions.Count) * 100;
        int timeSpentSeconds = request.TimeSpentSeconds;

        // Sauvegarder la tentative
        var attempt = new QuizAttempt
        {
            QuizId = quizId,
            UserId = userId,
            UserAnswersJson = JsonSerializer.Serialize(request.Answers),
            Score = score,
            TimeSpentSeconds = timeSpentSeconds,
            CompletedAt = DateTime.UtcNow,
            Passed = score >= (decimal)PASSING_SCORE,
        };

        _context.QuizAttempts.Add(attempt);

        // Incrémenter les statistiques du quiz
        quiz.Attempts++;
        if (score >= (decimal)PASSING_SCORE)
            quiz.PassingAttempts++;
        quiz.TotalScore += (decimal)score;

        _context.Quizzes.Update(quiz);
        await _context.SaveChangesAsync();

        // Retourner la réponse avec résultats
        return new QuizResultResponseDto
        {
            AttemptId = attempt.Id,
            Score = (double)score,
            Passed = attempt.Passed,
            TotalQuestions = questions.Count,
            CorrectAnswers = correctAnswers,
            TimeSpentSeconds = timeSpentSeconds,
            CompletedAt = attempt.CompletedAt,
            QuestionResults = questionResults,
        };
    }

    public async Task<QuizAttemptDto?> GetQuizAttemptAsync(int attemptId)
    {
        var attempt = await _context.QuizAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attemptId);

        return attempt != null ? MapAttemptToDto(attempt) : null;
    }

    public async Task<IEnumerable<QuizAttemptDto>> GetUserQuizAttemptsAsync(int userId, int quizId)
    {
        var attempts = await _context.QuizAttempts
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.QuizId == quizId)
            .OrderByDescending(a => a.CompletedAt)
            .ToListAsync();

        return attempts.Select(MapAttemptToDto);
    }

    public async Task<IEnumerable<QuizAttemptDto>> GetUserAllQuizAttemptsAsync(int userId, int page = 1, int pageSize = 20)
    {
        var attempts = await _context.QuizAttempts
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CompletedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return attempts.Select(MapAttemptToDto);
    }

    public async Task<QuizDto> CreateQuizAsync(CreateQuizRequestDto request)
    {
        var difficultyMap = new Dictionary<int, string> 
        { 
            { 1, "easy" }, 
            { 2, "medium" }, 
            { 3, "hard" } 
        };
        
        var quiz = new Quiz
        {
            Title = request.Title,
            Description = request.Description,
            SubjectId = request.SubjectId,
            ExamId = request.ExamId,
            Subject = request.Subject,
            QuestionsJson = JsonSerializer.Serialize(request.Questions, CamelCaseJson),
            Difficulty = difficultyMap.ContainsKey(request.Difficulty) ? difficultyMap[request.Difficulty] : "medium",
            TimeLimit = request.DurationMinutes,
            IsPublished = false,
            CreatedAt = DateTime.UtcNow,
        };

        _context.Quizzes.Add(quiz);
        await _context.SaveChangesAsync();

        return MapToDto(quiz);
    }

    public async Task<QuizDto> UpdateQuizAsync(int id, UpdateQuizRequestDto request)
    {
        var quiz = await _context.Quizzes.FirstOrDefaultAsync(q => q.Id == id && !q.IsDeleted);
        if (quiz == null)
            throw new KeyNotFoundException($"Quiz with id {id} not found");

        if (!string.IsNullOrEmpty(request.Title))
            quiz.Title = request.Title;
        if (request.Description != null)
            quiz.Description = request.Description;
        if (!string.IsNullOrEmpty(request.Subject))
            quiz.Subject = request.Subject;
        if (request.Questions != null && request.Questions.Any())
            quiz.QuestionsJson = JsonSerializer.Serialize(request.Questions, CamelCaseJson);
        if (request.Difficulty.HasValue)
        {
            var difficultyMap = new Dictionary<int, string> 
            { 
                { 1, "easy" }, 
                { 2, "medium" }, 
                { 3, "hard" } 
            };
            quiz.Difficulty = difficultyMap.ContainsKey(request.Difficulty.Value) ? difficultyMap[request.Difficulty.Value] : "medium";
        }
        if (request.DurationMinutes.HasValue)
            quiz.TimeLimit = request.DurationMinutes;

        quiz.UpdatedAt = DateTime.UtcNow;

        _context.Quizzes.Update(quiz);
        await _context.SaveChangesAsync();

        return MapToDto(quiz);
    }

    public async Task<QuizDto> PublishQuizAsync(int id)
    {
        var quiz = await _context.Quizzes.FirstOrDefaultAsync(q => q.Id == id && !q.IsDeleted);
        if (quiz == null)
            throw new KeyNotFoundException($"Quiz with id {id} not found");

        quiz.IsPublished = true;
        quiz.PublishedAt = DateTime.UtcNow;
        quiz.UpdatedAt = DateTime.UtcNow;

        _context.Quizzes.Update(quiz);
        await _context.SaveChangesAsync();

        return MapToDto(quiz);
    }

    public async Task<QuizDto> UnpublishQuizAsync(int id)
    {
        var quiz = await _context.Quizzes.FirstOrDefaultAsync(q => q.Id == id && !q.IsDeleted);
        if (quiz == null)
            throw new KeyNotFoundException($"Quiz with id {id} not found");

        quiz.IsPublished = false;
        quiz.UpdatedAt = DateTime.UtcNow;

        _context.Quizzes.Update(quiz);
        await _context.SaveChangesAsync();

        return MapToDto(quiz);
    }

    public async Task<bool> DeleteQuizAsync(int id)
    {
        var quiz = await _context.Quizzes.FirstOrDefaultAsync(q => q.Id == id);
        if (quiz == null)
            return false;

        quiz.IsDeleted = true;
        quiz.UpdatedAt = DateTime.UtcNow;

        _context.Quizzes.Update(quiz);
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<object> GetQuizStatsAsync(int id)
    {
        var quiz = await _context.Quizzes
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == id && !q.IsDeleted);

        if (quiz == null)
            throw new KeyNotFoundException($"Quiz with id {id} not found");

        double averageScore = quiz.Attempts > 0 
            ? (double)quiz.TotalScore / quiz.Attempts 
            : 0;

        return new
        {
            TotalAttempts = quiz.Attempts,
            PassingAttempts = quiz.PassingAttempts,
            AverageScore = averageScore,
            PassRate = quiz.Attempts > 0 ? (quiz.PassingAttempts / (double)quiz.Attempts) * 100 : 0,
        };
    }

    public async Task<double> GetQuizAverageScoreAsync(int id)
    {
        var quiz = await _context.Quizzes
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == id && !q.IsDeleted);

        if (quiz == null)
            throw new KeyNotFoundException($"Quiz with id {id} not found");

        return quiz.Attempts > 0 
            ? (double)quiz.TotalScore / quiz.Attempts 
            : 0;
    }

    private QuizDto MapToDto(Quiz quiz)
    {
        var questions = ParsePlayQuestions(quiz.QuestionsJson);
        return new QuizDto
        {
            Id = quiz.Id,
            Title = quiz.Title,
            Description = quiz.Description,
            SubjectId = quiz.SubjectId,
            ExamId = quiz.ExamId,
            Subject = quiz.Subject,
            QuestionsCount = questions.Count,
            Questions = questions,
            Difficulty = quiz.Difficulty,
            DurationMinutes = quiz.TimeLimit,
            IsPublished = quiz.IsPublished,
            TotalAttempts = quiz.Attempts,
            PassingAttempts = quiz.PassingAttempts,
            CreatedAt = quiz.CreatedAt,
            UpdatedAt = quiz.UpdatedAt,
            PublishedAt = quiz.PublishedAt,
            IsAIGenerated = quiz.IsAIGenerated,
        };
    }

    private static List<QuizPlayQuestionDto> ParsePlayQuestions(string questionsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(questionsJson) ? "[]" : questionsJson);
            return doc.RootElement.EnumerateArray().Select(q => new QuizPlayQuestionDto
            {
                Id = q.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "",
                Question = q.TryGetProperty("question", out var qEl) ? qEl.GetString() ?? "" : "",
                Options = q.TryGetProperty("options", out var optsEl) && optsEl.ValueKind == JsonValueKind.Array
                    ? optsEl.EnumerateArray().Select(o => o.GetString() ?? "").ToList()
                    : new List<string>(),
            }).ToList();
        }
        catch
        {
            return new List<QuizPlayQuestionDto>();
        }
    }

    /// <summary>
    /// Renvoie le quiz d'évaluation déjà généré pour cette épreuve, ou en
    /// génère un nouveau à partir du contenu réel du PDF (via le service
    /// Python /api/exam-quiz/generate) la première fois. Le quiz est ensuite
    /// réutilisé pour toutes les tentatives suivantes  régénérer à chaque
    /// fois donnerait des questions différentes d'une tentative à l'autre,
    /// rendant les scores incomparables.
    /// </summary>
    public async Task<QuizDto> GetOrCreateExamQuizAsync(int examId)
    {
        var existing = await _context.Quizzes
            .Where(q => q.ExamId == examId && !q.IsDeleted)
            .OrderByDescending(q => q.CreatedAt)
            .FirstOrDefaultAsync();
        if (existing != null)
            return MapToDto(existing);

        var exam = await _context.Exams.FirstOrDefaultAsync(e => e.Id == examId && !e.IsDeleted);
        if (exam == null)
            throw new KeyNotFoundException("Épreuve introuvable.");
        if (string.IsNullOrWhiteSpace(exam.DocumentUrl))
            throw new InvalidOperationException("Cette épreuve n'a pas de fichier PDF associé.");

        var (generated, errorDetail) = await _fastApiClient.GenerateExamQuizAsync(examId, exam.DocumentUrl, exam.Title, exam.Category);
        if (generated == null || generated.Count == 0)
        {
            // Le message vient de Python quand disponible (ex: "PDF scanné, contenu
            // illisible") : un message générique identique dans tous les cas
            // masquait la vraie cause (dépendance manquante, S3, etc.) autant pour
            // l'utilisateur que pour le débogage.
            var message = !string.IsNullOrWhiteSpace(errorDetail)
                ? $"Impossible de générer une évaluation : {errorDetail}"
                : "Impossible de générer une évaluation : le service IA n'a pas répondu. Réessayez dans quelques instants.";
            throw new InvalidOperationException(message);
        }

        var quiz = new Quiz
        {
            Title = $"Évaluation  {exam.Title}",
            Description = $"Épreuve chronométrée générée à partir du contenu de « {exam.Title} ».",
            Subject = exam.Category ?? "Général",
            Difficulty = exam.Difficulty ?? "moyen",
            QuestionsJson = JsonSerializer.Serialize(generated, CamelCaseJson),
            TimeLimit = exam.DurationMinutes ?? 30,
            PassingScore = 50,
            SubjectId = exam.SubjectId,
            ExamId = examId,
            IsAIGenerated = true,
            IsPublished = true,
            PublishedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };

        _context.Quizzes.Add(quiz);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Quiz d'évaluation généré pour l'épreuve {ExamId} ({Count} questions)", examId, generated.Count);
        return MapToDto(quiz);
    }

    public async Task<QuizDto> GenerateAIQuizAsync(int userId, string? subject, string? topic)
    {
        var resolvedSubject = subject;

        // Repli 1 : matière où le dernier score de quiz est le plus faible.
        if (string.IsNullOrWhiteSpace(resolvedSubject))
        {
            var recentScores = await _context.QuizAttempts
                .Where(a => a.UserId == userId)
                .GroupBy(a => a.Quiz.Subject)
                .Select(g => new { Subject = g.Key, LatestScore = g.OrderByDescending(a => a.CompletedAt).First().Score })
                .ToListAsync();

            resolvedSubject = recentScores.OrderBy(s => s.LatestScore).FirstOrDefault()?.Subject;
        }

        // Repli 2 : catégorie de la dernière épreuve téléchargée. Un élève qui
        // vient d'arriver n'a pas encore de tentative de quiz, mais peut déjà
        // avoir consulté des épreuves  ça reste un signal réel, pas un choix
        // arbitraire.
        if (string.IsNullOrWhiteSpace(resolvedSubject))
        {
            resolvedSubject = await _context.DownloadHistories
                .Where(d => d.UserId == userId)
                .OrderByDescending(d => d.CreatedAt)
                .Join(_context.Subjects, d => d.SubjectId, s => s.Id, (d, s) => s.Category)
                .FirstOrDefaultAsync(c => !string.IsNullOrWhiteSpace(c));
        }

        // Repli 3 : ni quiz ni téléchargement  on laisse DeepSeek choisir une
        // matière pertinente à partir du niveau scolaire, de la moyenne
        // (bulletin, saisie par l'élève ou un parent lié) et des objectifs
        // actifs de l'élève (ex: « progresser en maths » dans un objectif).
        string? contextHint = null;
        var level = await _context.Users.Where(u => u.Id == userId).Select(u => u.Level).FirstOrDefaultAsync();
        if (string.IsNullOrWhiteSpace(resolvedSubject))
        {
            var goals = await _context.Goals
                .Where(g => g.UserId == userId && g.Status == "active")
                .Select(g => (g.Title ?? "") + (g.Description != null ? " : " + g.Description : ""))
                .Take(3)
                .ToListAsync();

            var latestGrade = await _context.AcademicRecords
                .Where(r => r.StudentId == userId)
                .OrderByDescending(r => r.SchoolYear)
                .Select(r => new { r.SchoolYear, r.AverageGrade })
                .FirstOrDefaultAsync();

            if (goals.Count == 0 && latestGrade == null)
                throw new InvalidOperationException("Passe un quiz, télécharge une épreuve ou définis un objectif pour qu'on sache sur quelle matière t'entraîner.");

            var hints = new List<string>(goals);
            if (latestGrade != null)
                hints.Add($"Moyenne scolaire {latestGrade.SchoolYear} : {latestGrade.AverageGrade}/20");
            contextHint = string.Join("; ", hints);
        }

        var questions = await _fastApiClient.GenerateSubjectQuizAsync(userId, resolvedSubject, topic, level, contextHint);
        if (questions == null || questions.Questions.Count == 0)
            throw new InvalidOperationException("La génération du quiz a échoué, réessaie dans un instant.");

        resolvedSubject ??= questions.Subject ?? "Général";

        var quiz = new Quiz
        {
            Title = topic != null ? $"Quiz  {topic}" : $"Quiz  {resolvedSubject}",
            Description = $"Généré par WinAI pour cibler tes lacunes en {resolvedSubject}.",
            Subject = resolvedSubject,
            QuestionsJson = JsonSerializer.Serialize(questions.Questions, CamelCaseJson),
            Difficulty = "medium",
            TimeLimit = 15,
            PassingScore = 50,
            IsAIGenerated = true,
            IsPublished = true,
            PublishedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };

        _context.Quizzes.Add(quiz);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Quiz d'entraînement IA généré pour l'utilisateur {UserId} en {Subject} ({Count} questions)", userId, resolvedSubject, questions.Questions.Count);
        return MapToDto(quiz);
    }

    private QuizAttemptDto MapAttemptToDto(QuizAttempt attempt)
    {
        return new QuizAttemptDto
        {
            Id = attempt.Id,
            QuizId = attempt.QuizId,
            UserId = attempt.UserId,
            Score = attempt.Score,
            TimeSpentSeconds = attempt.TimeSpentSeconds ?? 0,
            Passed = attempt.Passed,
            CompletedAt = attempt.CompletedAt,
        };
    }
}
