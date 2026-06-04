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
    private const double PASSING_SCORE = 50.0;

    public QuizService(ApplicationDbContext context)
    {
        _context = context;
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
            QuestionsJson = JsonSerializer.Serialize(request.Questions),
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
            quiz.QuestionsJson = JsonSerializer.Serialize(request.Questions);
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
        return new QuizDto
        {
            Id = quiz.Id,
            Title = quiz.Title,
            Description = quiz.Description,
            SubjectId = quiz.SubjectId,
            ExamId = quiz.ExamId,
            Subject = quiz.Subject,
            QuestionsCount = 0, // Will be parsed from JSON if needed
            Difficulty = quiz.Difficulty,
            DurationMinutes = quiz.TimeLimit,
            IsPublished = quiz.IsPublished,
            TotalAttempts = quiz.Attempts,
            PassingAttempts = quiz.PassingAttempts,
            CreatedAt = quiz.CreatedAt,
            UpdatedAt = quiz.UpdatedAt,
            PublishedAt = quiz.PublishedAt,
        };
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
