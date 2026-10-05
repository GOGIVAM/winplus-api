using Backend.Models.Entities;
using Backend.Models.DTOs;
using Backend.Repositories;

namespace Backend.Services;

public interface IEnrollmentService
{
    Task<Enrollment> EnrollUserAsync(int userId, int subjectId);
    Task<IEnumerable<Enrollment>> GetUserEnrollmentsAsync(int userId);
    Task<Enrollment?> GetEnrollmentAsync(int userId, int subjectId);

    /// <summary>
    /// Inscription retrouvée par son identifiant propre (Module 20).
    ///
    /// Le contrôleur appelait GetEnrollmentAsync(userId, enrollmentId), qui
    /// attend un identifiant de contenu en second argument : la vérification
    /// de propriété avant désinscription comparait donc un identifiant
    /// d'inscription à un identifiant de matière, et renvoyait « introuvable »
    /// ou la mauvaise ligne selon les identifiants en présence.
    /// </summary>
    Task<Enrollment?> GetEnrollmentByIdAsync(int enrollmentId);
    Task<Enrollment> UpdateProgressAsync(int enrollmentId, decimal progressPercentage);
    Task<Enrollment> CompleteEnrollmentAsync(int enrollmentId, string? certificateUrl = null);
    Task<bool> UnenrollAsync(int enrollmentId);
    Task<bool> IsUserEnrolledAsync(int userId, int subjectId);
    Task<int> GetEnrollmentCountBySubjectAsync(int subjectId);
    Task<decimal> GetAverageProgressAsync(int subjectId);
    Task<EnrollmentProgressDto> GetProgressAsync(int enrollmentId, int userId);
}

public class EnrollmentService : IEnrollmentService
{
    private readonly IUserRepository _userRepository;
    private readonly ISubjectRepository _subjectRepository;
    private readonly IEnrollmentRepository _enrollmentRepository;
    private readonly ICertificateService _certificateService;
    private readonly ILogger<EnrollmentService> _logger;

    public EnrollmentService(
        IUserRepository userRepository,
        ISubjectRepository subjectRepository,
        IEnrollmentRepository enrollmentRepository,
        ICertificateService certificateService,
        ILogger<EnrollmentService> logger)
    {
        _userRepository = userRepository;
        _subjectRepository = subjectRepository;
        _enrollmentRepository = enrollmentRepository;
        _certificateService = certificateService;
        _logger = logger;
    }

    public async Task<Enrollment> EnrollUserAsync(int userId, int subjectId)
    {
        try
        {
            // Verify user exists
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
                throw new InvalidOperationException($"User {userId} not found");

            // Verify subject exists
            var subject = await _subjectRepository.GetByIdAsync(subjectId);
            if (subject == null)
                throw new InvalidOperationException($"Subject {subjectId} not found");

            // Check if already enrolled
            var existing = user.Enrollments?.FirstOrDefault(e => e.SubjectId == subjectId);
            if (existing != null)
            {
                _logger.LogInformation("User {UserId} already enrolled in subject {SubjectId}", userId, subjectId);
                return existing;
            }

            // Module 21 : une inscription désinscrite (suppression logique)
            // existe peut-être déjà pour cette paire — l'index unique actif
            // interdit d'en créer une seconde, et ça n'aurait de toute façon
            // aucun sens (le certificat déjà obtenu reste attaché à la ligne
            // existante). Réactivation plutôt que doublon.
            var deletedExisting = await _enrollmentRepository.GetAnyByUserAndSubjectAsync(userId, subjectId);
            if (deletedExisting is { IsDeleted: true })
            {
                deletedExisting.IsDeleted = false;
                deletedExisting.UnenrolledAt = null;
                deletedExisting.UnenrollReason = null;
                deletedExisting.EnrolledAt = DateTime.UtcNow;
                await _enrollmentRepository.UpdateAsync(deletedExisting);

                subject.EnrollmentCount++;
                await _subjectRepository.UpdateAsync(subject);

                _logger.LogInformation("User {UserId} re-enrolled in subject {SubjectId} (reactivated)", userId, subjectId);
                return deletedExisting;
            }

            // Create enrollment
            var enrollment = new Enrollment
            {
                UserId = userId,
                SubjectId = subjectId,
                EnrolledAt = DateTime.UtcNow,
                ProgressPercentage = 0,
                User = user,
                Subject = subject
            };

            // Add to user's enrollments
            user.Enrollments ??= new List<Enrollment>();
            user.Enrollments.Add(enrollment);

            await _userRepository.UpdateAsync(user);

            // Increment subject enrollment count
            subject.EnrollmentCount++;
            await _subjectRepository.UpdateAsync(subject);

            _logger.LogInformation("User {UserId} enrolled in subject {SubjectId}", userId, subjectId);

            return enrollment;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enrolling user {UserId} in subject {SubjectId}", userId, subjectId);
            throw;
        }
    }

    public async Task<IEnumerable<Enrollment>> GetUserEnrollmentsAsync(int userId)
    {
        try
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
                return Enumerable.Empty<Enrollment>();

            return user.Enrollments ?? Enumerable.Empty<Enrollment>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting enrollments for user {UserId}", userId);
            return Enumerable.Empty<Enrollment>();
        }
    }

    public async Task<Enrollment?> GetEnrollmentAsync(int userId, int subjectId)
    {
        try
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
                return null;

            return user.Enrollments?.FirstOrDefault(e => e.SubjectId == subjectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting enrollment for user {UserId} and subject {SubjectId}", userId, subjectId);
            return null;
        }
    }

    public async Task<Enrollment?> GetEnrollmentByIdAsync(int enrollmentId)
    {
        try
        {
            return await _enrollmentRepository.GetByIdAsync(enrollmentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting enrollment {EnrollmentId}", enrollmentId);
            return null;
        }
    }

    public async Task<Enrollment> UpdateProgressAsync(int enrollmentId, decimal progressPercentage)
    {
        try
        {
            if (progressPercentage < 0 || progressPercentage > 100)
                throw new ArgumentException("Progress percentage must be between 0 and 100");

            var enrollment = await _enrollmentRepository.GetByIdAsync(enrollmentId);
            if (enrollment == null)
                throw new KeyNotFoundException($"Enrollment {enrollmentId} not found");

            enrollment.ProgressPercentage = progressPercentage;

            // Auto-complete if progress reaches 100%
            if (progressPercentage >= 100 && !enrollment.IsCompleted)
            {
                enrollment.IsCompleted = true;
                enrollment.CompletedAt = DateTime.UtcNow;
                _logger.LogInformation("Enrollment {EnrollmentId} auto-completed at 100% progress", enrollmentId);
            }

            await _enrollmentRepository.UpdateAsync(enrollment);

            _logger.LogInformation("Progress updated to {Progress}% for enrollment {EnrollmentId}", progressPercentage, enrollmentId);
            return enrollment;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating progress for enrollment {EnrollmentId}", enrollmentId);
            throw;
        }
    }

    public async Task<Enrollment> CompleteEnrollmentAsync(int enrollmentId, string? certificateUrl = null)
    {
        try
        {
            var enrollment = await _enrollmentRepository.GetByIdAsync(enrollmentId);
            if (enrollment == null)
                throw new KeyNotFoundException($"Enrollment {enrollmentId} not found");

            enrollment.IsCompleted = true;
            enrollment.CompletedAt = DateTime.UtcNow;
            enrollment.ProgressPercentage = 100;

            if (certificateUrl != null)
                enrollment.CertificateUrl = certificateUrl;

            await _enrollmentRepository.UpdateAsync(enrollment);

            _logger.LogInformation("Enrollment {EnrollmentId} marked as completed for user {UserId}", enrollmentId, enrollment.UserId);

            // Trigger certificate generation if no certificate yet
            if (enrollment.Certificate == null)
            {
                try
                {
                    await _certificateService.GenerateCertificateAsync(enrollment.UserId, enrollmentId);
                    _logger.LogInformation("Certificate generated for enrollment {EnrollmentId}", enrollmentId);
                }
                catch (Exception certEx)
                {
                    // Certificate generation failure must not roll back the completion
                    _logger.LogWarning(certEx, "Certificate generation failed for enrollment {EnrollmentId}  enrollment is still marked complete", enrollmentId);
                }
            }

            return enrollment;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing enrollment {EnrollmentId}", enrollmentId);
            throw;
        }
    }

    public async Task<bool> IsUserEnrolledAsync(int userId, int subjectId)
    {
        try
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
                return false;

            return user.Enrollments?.Any(e => e.SubjectId == subjectId) ?? false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking enrollment status");
            return false;
        }
    }

    public async Task<int> GetEnrollmentCountBySubjectAsync(int subjectId)
    {
        try
        {
            var subject = await _subjectRepository.GetByIdAsync(subjectId);
            if (subject == null)
                return 0;

            return subject.EnrollmentCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting enrollment count for subject {SubjectId}", subjectId);
            return 0;
        }
    }

    public async Task<decimal> GetAverageProgressAsync(int subjectId)
    {
        try
        {
            var subject = await _subjectRepository.GetByIdAsync(subjectId);
            if (subject == null || subject.EnrollmentCount == 0)
                return 0;

            // Calculate average progress from enrollments
            var averageProgress = subject.Enrollments?.Average(e => e.ProgressPercentage) ?? 0;
            return (decimal)averageProgress;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating average progress for subject {SubjectId}", subjectId);
            return 0;
        }
    }

    public async Task<bool> UnenrollAsync(int enrollmentId)
    {
        try
        {
            var enrollment = await _enrollmentRepository.GetByIdAsync(enrollmentId);
            if (enrollment == null)
            {
                _logger.LogWarning("Enrollment {EnrollmentId} not found for unenrollment", enrollmentId);
                return false;
            }

            // Module 21 (décision §4.G) : suppression logique, plus physique.
            // Un Remove() physique entraînait, via la relation en cascade
            // alors déclarée sur Certificate, la perte d'un certificat déjà
            // obtenu. La relation est désormais SetNull et la suppression
            // elle-même n'est plus physique : les deux protections se
            // recoupent, aucune ne dépend plus de l'autre pour être sûre.
            enrollment.IsDeleted = true;
            enrollment.UnenrolledAt = DateTime.UtcNow;
            await _enrollmentRepository.UpdateAsync(enrollment);
            var deleted = true;

            if (deleted)
            {
                // Decrement subject enrollment count
                var subject = await _subjectRepository.GetByIdAsync(enrollment.SubjectId);
                if (subject != null && subject.EnrollmentCount > 0)
                {
                    subject.EnrollmentCount--;
                    await _subjectRepository.UpdateAsync(subject);
                }

                _logger.LogInformation("User {UserId} unenrolled from subject {SubjectId}", enrollment.UserId, enrollment.SubjectId);
            }

            return deleted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unenrolling from enrollment {EnrollmentId}", enrollmentId);
            return false;
        }
    }

    public async Task<EnrollmentProgressDto> GetProgressAsync(int enrollmentId, int userId)
    {
        try
        {
            var subject = await _subjectRepository.GetByIdAsync(enrollmentId);
            if (subject == null)
            {
                throw new KeyNotFoundException("Enrollment not found");
            }

            var enrollment = subject.Enrollments?.FirstOrDefault(e => e.Id == enrollmentId && e.UserId == userId);
            if (enrollment == null)
            {
                throw new KeyNotFoundException("Enrollment not found for this user");
            }

            // Get course contents for completion calculation
            var totalContents = subject.Contents?.Count ?? 0;
            
            // Calculate progress based on contents and learning history
            // This is a simplified calculation - in real scenario would track actual completion
            var completedContents = (int)((enrollment.ProgressPercentage / 100m) * totalContents);

            var lastActivity = subject.LearningHistories?
                .Where(l => l.UserId == userId && l.SubjectId == subject.Id)
                .OrderByDescending(l => l.CreatedAt)
                .FirstOrDefault();

            return new EnrollmentProgressDto
            {
                EnrollmentId = enrollment.Id,
                UserId = userId,
                SubjectId = subject.Id,
                SubjectTitle = subject.Title,
                ProgressPercentage = enrollment.ProgressPercentage,
                IsCompleted = enrollment.IsCompleted,
                EnrolledAt = enrollment.EnrolledAt,
                CompletedAt = enrollment.CompletedAt,
                TotalContents = totalContents,
                CompletedContents = completedContents,
                LastAccessedAt = lastActivity?.CreatedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting progress for enrollment {EnrollmentId}", enrollmentId);
            throw;
        }
    }
}
