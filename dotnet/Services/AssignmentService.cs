using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Devoirs et corrections (Module 4  professeur_complete.md). Une copie
/// arrive de deux façons possibles (Submission.Source) : l'élève la soumet
/// lui-même depuis son espace, ou le professeur l'uploade pour lui (copie
/// papier scannée)  les deux alimentent la même file de correction.
/// </summary>
public class AssignmentService : IAssignmentService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AssignmentService> _logger;
    private readonly INtfyService _ntfy;

    public AssignmentService(ApplicationDbContext context, ILogger<AssignmentService> logger, INtfyService ntfy)
    {
        _context = context;
        _logger = logger;
        _ntfy = ntfy;
    }

    public async Task<AssignmentDto> CreateAssignmentAsync(int teacherId, CreateAssignmentRequestDto request)
    {
        var klass = await _context.TeacherClasses.FirstOrDefaultAsync(c => c.Id == request.TeacherClassId && c.TeacherId == teacherId);
        if (klass == null) throw new InvalidOperationException("Classe introuvable ou non autorisée.");

        if (request.QuizId.HasValue)
        {
            var quizExists = await _context.Quizzes.AnyAsync(q => q.Id == request.QuizId.Value && !q.IsDeleted);
            if (!quizExists) throw new InvalidOperationException("Quiz ou épreuve introuvable.");
        }

        var assignment = new Assignment
        {
            TeacherId = teacherId,
            TeacherClassId = request.TeacherClassId,
            Title = request.Title.Trim(),
            StatementText = request.StatementText,
            ReferenceAnswerText = request.ReferenceAnswerText,
            ReferenceAnswerFileUrl = request.ReferenceAnswerFileUrl,
            QuizId = request.QuizId,
            MaxScore = request.QuizId.HasValue ? 100 : (request.MaxScore is > 0 and <= 100 ? request.MaxScore : 20),
            DueDate = request.DueDate,
        };
        _context.Assignments.Add(assignment);
        await _context.SaveChangesAsync();

        // Module 10 : notifier chaque élève de la classe  l'opération métier
        // (devoir déjà enregistré ci-dessus) ne doit jamais échouer à cause
        // d'une notification. Une classe vide n'envoie simplement rien.
        try
        {
            var studentIds = await _context.TeacherClassStudents
                .Where(cs => cs.TeacherClassId == request.TeacherClassId)
                .Select(cs => cs.StudentId)
                .ToListAsync();

            if (studentIds.Count > 0)
            {
                var dueInfo = assignment.DueDate.HasValue
                    ? $" à rendre pour le {assignment.DueDate.Value:dd/MM/yyyy}"
                    : "";
                foreach (var studentId in studentIds)
                {
                    await _ntfy.PublishAsync(
                        $"winplus-user-{studentId}",
                        "Nouveau devoir",
                        $"« {assignment.Title} » a été donné à ta classe « {klass.Name} »{dueInfo}.",
                        userId: studentId,
                        type: "Assignment",
                        relatedEntityType: "Assignment",
                        relatedEntityId: assignment.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Notification de création du devoir {AssignmentId} échouée (devoir créé malgré tout)", assignment.Id);
        }

        return await MapToDtoAsync(assignment, teacherId: teacherId);
    }

    public async Task<List<AssignmentDto>> GetTeacherAssignmentsAsync(int teacherId)
    {
        var assignments = await _context.Assignments
            .AsNoTracking()
            .Include(a => a.TeacherClass)
            .Where(a => a.TeacherId == teacherId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        var result = new List<AssignmentDto>();
        foreach (var a in assignments)
            result.Add(await MapToDtoAsync(a, teacherId: teacherId));
        return result;
    }

    public async Task<AssignmentDto?> GetAssignmentAsync(int teacherId, int assignmentId)
    {
        var a = await _context.Assignments.AsNoTracking().Include(x => x.TeacherClass)
            .FirstOrDefaultAsync(x => x.Id == assignmentId && x.TeacherId == teacherId);
        return a == null ? null : await MapToDtoAsync(a, teacherId: teacherId);
    }

    public async Task SetRubricAsync(int assignmentId, string rubricJson)
    {
        var a = await _context.Assignments.FirstOrDefaultAsync(x => x.Id == assignmentId);
        if (a == null) return;
        a.RubricJson = rubricJson;
        await _context.SaveChangesAsync();
    }

    public async Task<AssignmentDto> SetReferenceAnswerAsync(int teacherId, int assignmentId, string? referenceAnswerText, string? referenceAnswerFileUrl)
    {
        var a = await _context.Assignments.Include(x => x.TeacherClass)
            .FirstOrDefaultAsync(x => x.Id == assignmentId && x.TeacherId == teacherId)
            ?? throw new KeyNotFoundException("Devoir introuvable ou non autorisé.");
        a.ReferenceAnswerText = referenceAnswerText;
        a.ReferenceAnswerFileUrl = referenceAnswerFileUrl;
        await _context.SaveChangesAsync();
        return await MapToDtoAsync(a, teacherId: teacherId);
    }

    public async Task<List<AssignmentDto>> GetStudentAssignmentsAsync(int studentId)
    {
        var classIds = await _context.TeacherClassStudents
            .Where(cs => cs.StudentId == studentId)
            .Select(cs => cs.TeacherClassId)
            .ToListAsync();
        if (classIds.Count == 0) return new List<AssignmentDto>();

        var assignments = await _context.Assignments
            .AsNoTracking()
            .Include(a => a.TeacherClass)
            .Where(a => classIds.Contains(a.TeacherClassId))
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        var result = new List<AssignmentDto>();
        foreach (var a in assignments)
            result.Add(await MapToDtoAsync(a, studentId: studentId));
        return result;
    }

    public async Task<PendingCorrectionDto> StudentSubmitAsync(int studentId, int assignmentId, StudentSubmitRequestDto request)
    {
        var assignment = await _context.Assignments.FirstOrDefaultAsync(a => a.Id == assignmentId)
            ?? throw new KeyNotFoundException("Devoir introuvable.");
        if (assignment.QuizId.HasValue)
            throw new InvalidOperationException("Ce devoir est un quiz/épreuve : réponds-y depuis l'écran du quiz, pas par ce formulaire.");
        var isMember = await _context.TeacherClassStudents.AnyAsync(cs => cs.TeacherClassId == assignment.TeacherClassId && cs.StudentId == studentId);
        if (!isMember) throw new InvalidOperationException("Tu n'es pas dans la classe concernée par ce devoir.");
        if (string.IsNullOrWhiteSpace(request.Content) && string.IsNullOrWhiteSpace(request.FileUrl))
            throw new InvalidOperationException("Ajoute un texte ou un fichier avant de soumettre.");

        var existing = await _context.Submissions.FirstOrDefaultAsync(s => s.AssignmentId == assignmentId && s.StudentId == studentId);
        if (existing != null)
            throw new InvalidOperationException("Tu as déjà soumis ce devoir.");

        var submission = new Submission
        {
            AssignmentId = assignmentId,
            StudentId = studentId,
            Content = request.Content,
            FileUrl = request.FileUrl,
            Source = "student",
            Status = "pending",
        };
        _context.Submissions.Add(submission);
        await _context.SaveChangesAsync();

        // Module 10 : notifier le professeur propriétaire du devoir.
        try
        {
            var student = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == studentId);
            await _ntfy.PublishAsync(
                $"winplus-user-{assignment.TeacherId}",
                "Nouvelle copie à corriger",
                $"{FullName(student)} a soumis sa copie pour « {assignment.Title} ».",
                userId: assignment.TeacherId,
                type: "Submission",
                relatedEntityType: "Submission",
                relatedEntityId: submission.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Notification de soumission {SubmissionId} échouée (copie enregistrée malgré tout)", submission.Id);
        }

        return await MapSubmissionToDtoAsync(submission, assignment);
    }

    public async Task<PendingCorrectionDto> TeacherUploadSubmissionAsync(int teacherId, int assignmentId, TeacherUploadSubmissionRequestDto request)
    {
        var assignment = await _context.Assignments.FirstOrDefaultAsync(a => a.Id == assignmentId && a.TeacherId == teacherId)
            ?? throw new KeyNotFoundException("Devoir introuvable ou non autorisé.");
        var isMember = await _context.TeacherClassStudents.AnyAsync(cs => cs.TeacherClassId == assignment.TeacherClassId && cs.StudentId == request.StudentId);
        if (!isMember) throw new InvalidOperationException("Cet élève n'est pas dans la classe concernée.");
        if (string.IsNullOrWhiteSpace(request.Content) && string.IsNullOrWhiteSpace(request.FileUrl))
            throw new InvalidOperationException("Ajoute un texte ou un fichier (copie scannée) avant d'importer.");

        var submission = await _context.Submissions.FirstOrDefaultAsync(s => s.AssignmentId == assignmentId && s.StudentId == request.StudentId);
        if (submission == null)
        {
            submission = new Submission { AssignmentId = assignmentId, StudentId = request.StudentId, Source = "teacher_upload" };
            _context.Submissions.Add(submission);
        }
        submission.Content = request.Content ?? submission.Content;
        submission.FileUrl = request.FileUrl ?? submission.FileUrl;
        submission.Status = "pending";
        submission.SubmittedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return await MapSubmissionToDtoAsync(submission, assignment);
    }

    public async Task<List<PendingCorrectionDto>> GetPendingCorrectionsAsync(int teacherId, string filter = "pending")
    {
        var query = _context.Submissions
            .AsNoTracking()
            .Include(s => s.Assignment)
            .Include(s => s.Student)
            .Where(s => s.Assignment!.TeacherId == teacherId);

        query = filter switch
        {
            "corrected" => query.Where(s => s.Status == "corrected"),
            "all" => query,
            _ => query.Where(s => s.Status == "pending" || s.Status == "draft"),
        };

        var submissions = await query.OrderBy(s => s.SubmittedAt).ToListAsync();
        var result = new List<PendingCorrectionDto>();
        foreach (var s in submissions)
            result.Add(await MapSubmissionToDtoAsync(s, s.Assignment!));
        return result;
    }

    public async Task<PendingCorrectionDto?> GetSubmissionAsync(int teacherId, int submissionId)
    {
        var s = await _context.Submissions.AsNoTracking()
            .Include(x => x.Assignment).Include(x => x.Student)
            .FirstOrDefaultAsync(x => x.Id == submissionId && x.Assignment!.TeacherId == teacherId);
        return s == null ? null : await MapSubmissionToDtoAsync(s, s.Assignment!);
    }

    public async Task<PendingCorrectionDto> GradeSubmissionAsync(int teacherId, int submissionId, GradeSubmissionRequestDto request)
    {
        var s = await _context.Submissions.Include(x => x.Assignment).Include(x => x.Student)
            .FirstOrDefaultAsync(x => x.Id == submissionId && x.Assignment!.TeacherId == teacherId)
            ?? throw new KeyNotFoundException("Copie introuvable.");

        var isDraft = string.Equals(request.Status, "draft", StringComparison.OrdinalIgnoreCase);
        if (isDraft)
        {
            s.Status = "draft";
            s.Score = request.Note;
            s.Comment = request.Comment;
            s.DraftUpdatedAt = DateTime.UtcNow;
        }
        else
        {
            if (request.Note is null || request.Note < 0 || request.Note > s.Assignment!.MaxScore)
                throw new InvalidOperationException($"La note doit être comprise entre 0 et {s.Assignment!.MaxScore}.");
            var wasAlreadyCorrected = s.Status == "corrected";
            s.Status = "corrected";
            s.Score = request.Note;
            s.Comment = request.Comment;
            s.GradedAt = DateTime.UtcNow;
            s.GradedByUserId = teacherId;

            // Module 9/11 : cette soumission vient d'une réponse à un quiz/
            // épreuve assigné (questions ouvertes)  répercute la validation
            // sur la tentative de quiz, pour que son statut ne reste pas
            // indéfiniment "en attente" une fois la note humaine enregistrée.
            // Connu et documenté : les agrégats globaux du quiz (Quiz.TotalScore,
            // Quiz.PassingAttempts) gardent le score provisoire de la soumission
            // (QCM seuls), pas cette note finale  éviter de les recalculer ici
            // pour ne pas introduire une dérive sur les quiz catalogue partagés.
            if (s.QuizAttemptId.HasValue)
            {
                var attempt = await _context.QuizAttempts.FirstOrDefaultAsync(a => a.Id == s.QuizAttemptId.Value);
                if (attempt != null && attempt.Status == "PendingReview")
                {
                    attempt.Status = "Reviewed";
                }
            }
            await _context.SaveChangesAsync();

            // Module 10 : notifier l'élève. Une correction déjà validée puis
            // re-corrigée renotifie (la note peut avoir changé) ; ce n'est
            // qu'une rafale si le professeur valide plusieurs fois de suite la
            // même copie sans changement, cas marginal jugé acceptable plutôt
            // que de risquer de ne jamais renotifier une note corrigée après coup.
            try
            {
                await _ntfy.PublishAsync(
                    $"winplus-user-{s.StudentId}",
                    wasAlreadyCorrected ? "Correction mise à jour" : "Copie corrigée",
                    $"Ta copie pour « {s.Assignment!.Title} » a été corrigée : {s.Score}/{s.Assignment!.MaxScore}.",
                    userId: s.StudentId,
                    type: "Submission",
                    relatedEntityType: "Submission",
                    relatedEntityId: s.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Notification de correction {SubmissionId} échouée (note enregistrée malgré tout)", s.Id);
            }
            return await MapSubmissionToDtoAsync(s, s.Assignment!);
        }
        await _context.SaveChangesAsync();
        return await MapSubmissionToDtoAsync(s, s.Assignment!);
    }

    /// <summary>
    /// Similarité algorithmique (Jaccard sur tokens normalisés), pas de LLM :
    /// détection purement textuelle, déterministe, sans coût d'appel IA pour
    /// une opération potentiellement O(n²) par devoir. Ne compare que les
    /// copies avec du texte (Content)  les copies fichier-seul (scans PDF)
    /// ne sont pas comparables sans extraction de texte, non disponible.
    /// </summary>
    public async Task<List<SimilarityPairDto>> GetSimilarityAsync(int teacherId, int assignmentId)
    {
        var assignment = await _context.Assignments.FirstOrDefaultAsync(a => a.Id == assignmentId && a.TeacherId == teacherId)
            ?? throw new KeyNotFoundException("Devoir introuvable ou non autorisé.");

        var submissions = await _context.Submissions
            .AsNoTracking().Include(s => s.Student)
            .Where(s => s.AssignmentId == assignmentId && s.Content != null && s.Content != "")
            .ToListAsync();

        var dismissed = await _context.SubmissionSimilarityDismissals
            .Where(d => submissions.Select(s => s.Id).Contains(d.SubmissionAId))
            .ToListAsync();
        var dismissedSet = dismissed.Select(d => (d.SubmissionAId, d.SubmissionBId)).ToHashSet();

        var pairs = new List<SimilarityPairDto>();
        for (int i = 0; i < submissions.Count; i++)
        {
            for (int j = i + 1; j < submissions.Count; j++)
            {
                var a = submissions[i];
                var b = submissions[j];
                var (idA, idB) = a.Id < b.Id ? (a.Id, b.Id) : (b.Id, a.Id);
                if (dismissedSet.Contains((idA, idB))) continue;

                var similarity = JaccardSimilarity(a.Content!, b.Content!);
                if (similarity < 0.70) continue;

                pairs.Add(new SimilarityPairDto
                {
                    SubmissionAId = idA,
                    SubmissionBId = idB,
                    StudentAName = a.Id == idA ? FullName(a.Student) : FullName(b.Student),
                    StudentBName = a.Id == idA ? FullName(b.Student) : FullName(a.Student),
                    SimilarityPercent = (int)Math.Round(similarity * 100),
                });
            }
        }
        return pairs.OrderByDescending(p => p.SimilarityPercent).ToList();
    }

    public async Task DismissSimilarityAsync(int teacherId, int submissionAId, int submissionBId)
    {
        var (idA, idB) = submissionAId < submissionBId ? (submissionAId, submissionBId) : (submissionBId, submissionAId);
        var exists = await _context.SubmissionSimilarityDismissals.AnyAsync(d => d.SubmissionAId == idA && d.SubmissionBId == idB);
        if (!exists)
        {
            _context.SubmissionSimilarityDismissals.Add(new SubmissionSimilarityDismissal
            {
                SubmissionAId = idA,
                SubmissionBId = idB,
                DismissedByUserId = teacherId,
            });
            await _context.SaveChangesAsync();
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static double JaccardSimilarity(string a, string b)
    {
        static HashSet<string> Tokenize(string s) =>
            s.ToLowerInvariant()
             .Split(new[] { ' ', '\n', '\r', '\t', '.', ',', ';', ':', '!', '?', '"', '\'', '(', ')' }, StringSplitOptions.RemoveEmptyEntries)
             .Where(t => t.Length > 2)
             .ToHashSet();

        var tokensA = Tokenize(a);
        var tokensB = Tokenize(b);
        if (tokensA.Count == 0 || tokensB.Count == 0) return 0;

        var intersection = tokensA.Intersect(tokensB).Count();
        var union = tokensA.Union(tokensB).Count();
        return union == 0 ? 0 : (double)intersection / union;
    }

    private static string FullName(User? u) => u == null ? "Élève" : $"{u.FirstName} {u.LastName}".Trim();

    private async Task<AssignmentDto> MapToDtoAsync(Assignment a, int? teacherId = null, int? studentId = null)
    {
        var submissionCount = await _context.Submissions.CountAsync(s => s.AssignmentId == a.Id);
        var pendingCount = await _context.Submissions.CountAsync(s => s.AssignmentId == a.Id && (s.Status == "pending" || s.Status == "draft"));
        var alreadySubmitted = studentId.HasValue && await _context.Submissions.AnyAsync(s => s.AssignmentId == a.Id && s.StudentId == studentId.Value);

        return new AssignmentDto
        {
            Id = a.Id,
            Title = a.Title,
            StatementText = a.StatementText,
            RubricJson = a.RubricJson,
            ReferenceAnswerText = a.ReferenceAnswerText,
            ReferenceAnswerFileUrl = a.ReferenceAnswerFileUrl,
            QuizId = a.QuizId,
            MaxScore = a.MaxScore,
            DueDate = a.DueDate,
            TeacherClassId = a.TeacherClassId,
            TeacherClassName = a.TeacherClass?.Name,
            SubmissionCount = submissionCount,
            PendingCount = pendingCount,
            AlreadySubmitted = alreadySubmitted,
            CreatedAt = a.CreatedAt,
        };
    }

    private async Task<PendingCorrectionDto> MapSubmissionToDtoAsync(Submission s, Assignment assignment)
    {
        var student = s.Student ?? await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == s.StudentId);
        var days = (int)(DateTime.UtcNow - s.SubmittedAt).TotalDays;

        return new PendingCorrectionDto
        {
            Id = s.Id,
            AssignmentId = s.AssignmentId,
            Assignment = assignment.Title,
            StudentName = FullName(student),
            Subject = assignment.TeacherClass?.Level,
            SubmittedDate = s.SubmittedAt.ToString("O"),
            DaysWaiting = Math.Max(0, days),
            FileUrl = s.FileUrl,
            TextContent = s.Content,
            // "Urgent" à partir de 48h en attente (US-COR-01)  n'a de sens que
            // pour une copie pas encore corrigée.
            Priority = s.Status != "corrected" && days >= 2 ? "high" : "normal",
            Status = s.Status,
            Score = s.Score,
            Comment = s.Comment,
            MaxScore = assignment.MaxScore,
            ReferenceAnswerText = assignment.ReferenceAnswerText,
            ReferenceAnswerFileUrl = assignment.ReferenceAnswerFileUrl,
            IsQuizSubmission = s.QuizAttemptId.HasValue,
            // Brouillon non envoyé depuis 72h (US-COR-04).
            IsStaleDraft = s.Status == "draft" && s.DraftUpdatedAt.HasValue && (DateTime.UtcNow - s.DraftUpdatedAt.Value).TotalHours >= 72,
        };
    }
}
