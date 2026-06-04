using Backend.Models.Entities;
using Backend.Models.DTOs;
using Backend.Repositories;

namespace Backend.Services;

public interface IEnrollmentService
{
    Task<Enrollment> EnrollUserAsync(int userId, int subjectId);
    Task<IEnumerable<Enrollment>> GetUserEnrollmentsAsync(int userId);
    Task<Enrollment?> GetEnrollmentAsync(int userId, int subjectId);
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
    private readonly ILogger<EnrollmentService> _logger;

    public EnrollmentService(
        IUserRepository userRepository,
        ISubjectRepository subjectRepository,
        ILogger<EnrollmentService> logger)
    {
        _userRepository = userRepository;
        _subjectRepository = subjectRepository;
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

    public async Task<Enrollment> UpdateProgressAsync(int enrollmentId, decimal progressPercentage)
    {
        try
        {
            if (progressPercentage < 0 || progressPercentage > 100)
                throw new ArgumentException("Progress percentage must be between 0 and 100");

            // This would require an IEnrollmentRepository to be fully implemented
            // For now, we'll throw a not implemented exception
            throw new NotImplementedException("EnrollmentRepository not yet implemented");
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
            // This would require an IEnrollmentRepository to be fully implemented
            throw new NotImplementedException("EnrollmentRepository not yet implemented");
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
            // Note: Unenroll logic would need IEnrollmentRepository
            // For now, returning false since repository is not available
            _logger.LogWarning("Unenroll not fully implemented - missing enrollment repository");
            return false;
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
