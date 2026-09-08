using Backend.Models.DTOs;

namespace Backend.Services;

public interface IAssignmentService
{
    Task<AssignmentDto> CreateAssignmentAsync(int teacherId, CreateAssignmentRequestDto request);
    Task<List<AssignmentDto>> GetTeacherAssignmentsAsync(int teacherId);
    Task<AssignmentDto?> GetAssignmentAsync(int teacherId, int assignmentId);
    Task SetRubricAsync(int assignmentId, string rubricJson);

    Task<List<AssignmentDto>> GetStudentAssignmentsAsync(int studentId);
    Task<PendingCorrectionDto> StudentSubmitAsync(int studentId, int assignmentId, StudentSubmitRequestDto request);
    Task<PendingCorrectionDto> TeacherUploadSubmissionAsync(int teacherId, int assignmentId, TeacherUploadSubmissionRequestDto request);

    /// <summary>US-COR-01 : file de correction, filtrable "all"/"pending"/"corrected".</summary>
    Task<List<PendingCorrectionDto>> GetPendingCorrectionsAsync(int teacherId, string filter = "pending");
    Task<PendingCorrectionDto?> GetSubmissionAsync(int teacherId, int submissionId);
    Task<PendingCorrectionDto> GradeSubmissionAsync(int teacherId, int submissionId, GradeSubmissionRequestDto request);

    Task<List<SimilarityPairDto>> GetSimilarityAsync(int teacherId, int assignmentId);
    Task DismissSimilarityAsync(int teacherId, int submissionAId, int submissionBId);
}
