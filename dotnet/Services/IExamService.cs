using Backend.Models.DTOs;

namespace Backend.Services;

public interface IExamService
{
    Task<(List<ExamDto> Data, int TotalCount)> GetAllExamsAsync(int page, int pageSize);
    Task<ExamDto?> GetExamByIdAsync(int id);
    Task<(List<ExamDto> Data, int TotalCount)> GetExamsByTypeAsync(string examType, int page, int pageSize);
    Task<(List<ExamDto> Data, int TotalCount)> GetExamsBySubjectAsync(string category, int page, int pageSize);
    Task<(List<ExamDto> Data, int TotalCount)> GetExamsByYearAsync(int year, int page, int pageSize);
    Task<(List<ExamDto> Data, int TotalCount)> SearchExamsAsync(ExamSearchFilterDto filter);
    Task<ExamDto> CreateExamAsync(CreateExamRequestDto dto);
    Task<ExamDto?> UpdateExamAsync(int id, UpdateExamRequestDto dto);
    Task<bool> DeleteExamAsync(int id);
}
