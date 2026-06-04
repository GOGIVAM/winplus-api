using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public class ExamService : IExamService
{
    private readonly ApplicationDbContext _context;

    public ExamService(ApplicationDbContext context)
    {
        _context = context;
    }

    private IQueryable<Exam> BaseQuery()
    {
        return _context.Exams
            .AsNoTracking()
            .Include(e => e.SubjectReference)
            .Where(e => !e.IsDeleted && e.IsPublished);
    }

    private static ExamDto ToDto(Exam e) => new()
    {
        Id = e.Id,
        Title = e.Title,
        Description = e.Description,
        ExamType = e.ExamType,
        Category = e.Category,
        Year = e.Year,
        Session = e.Session,
        Level = e.Level,
        Difficulty = e.Difficulty,
        DurationMinutes = e.DurationMinutes,
        DocumentUrl = e.DocumentUrl,
        CorrectionUrl = e.CorrectionUrl,
        DownloadCount = e.DownloadCount,
        IsPublished = e.IsPublished,
        SubjectId = e.SubjectId,
        Price = e.SubjectReference?.Price ?? 0,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
    };

    public async Task<(List<ExamDto> Data, int TotalCount)> GetAllExamsAsync(int page, int pageSize)
    {
        var query = BaseQuery();
        var total = await query.CountAsync();

        var exams = await query
            .OrderByDescending(e => e.Year)
            .ThenByDescending(e => e.DownloadCount)
            .ThenBy(e => e.Title)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (exams.Select(ToDto).ToList(), total);
    }

    public async Task<ExamDto?> GetExamByIdAsync(int id)
    {
        var exam = await _context.Exams
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted);

        return exam == null ? null : ToDto(exam);
    }

    public async Task<(List<ExamDto> Data, int TotalCount)> GetExamsByTypeAsync(string examType, int page, int pageSize)
    {
        var query = BaseQuery().Where(e => e.ExamType.ToLower() == examType.ToLower());
        var total = await query.CountAsync();

        var exams = await query
            .OrderByDescending(e => e.Year)
            .ThenBy(e => e.Category)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (exams.Select(ToDto).ToList(), total);
    }

    public async Task<(List<ExamDto> Data, int TotalCount)> GetExamsBySubjectAsync(string category, int page, int pageSize)
    {
        var query = BaseQuery().Where(e => e.Category.ToLower() == category.ToLower());
        var total = await query.CountAsync();

        var exams = await query
            .OrderByDescending(e => e.Year)
            .ThenBy(e => e.ExamType)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (exams.Select(ToDto).ToList(), total);
    }

    public async Task<(List<ExamDto> Data, int TotalCount)> GetExamsByYearAsync(int year, int page, int pageSize)
    {
        var query = BaseQuery().Where(e => e.Year == year);
        var total = await query.CountAsync();

        var exams = await query
            .OrderBy(e => e.ExamType)
            .ThenBy(e => e.Category)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (exams.Select(ToDto).ToList(), total);
    }

    public async Task<(List<ExamDto> Data, int TotalCount)> SearchExamsAsync(ExamSearchFilterDto filter)
    {
        var query = BaseQuery();

        if (!string.IsNullOrWhiteSpace(filter.ExamType))
            query = query.Where(e => e.ExamType.ToLower() == filter.ExamType.ToLower());

        if (!string.IsNullOrWhiteSpace(filter.Category))
            query = query.Where(e => e.Category.ToLower() == filter.Category.ToLower());

        if (filter.Year.HasValue)
            query = query.Where(e => e.Year == filter.Year.Value);

        if (filter.MinYear.HasValue)
            query = query.Where(e => e.Year >= filter.MinYear.Value);

        if (filter.MaxYear.HasValue)
            query = query.Where(e => e.Year <= filter.MaxYear.Value);

        if (!string.IsNullOrWhiteSpace(filter.Level))
            query = query.Where(e => e.Level != null && e.Level.ToLower() == filter.Level.ToLower());

        if (!string.IsNullOrWhiteSpace(filter.Difficulty))
            query = query.Where(e => e.Difficulty != null && e.Difficulty.ToLower() == filter.Difficulty.ToLower());

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var term = filter.SearchTerm.ToLower();
            query = query.Where(e =>
                e.Title.ToLower().Contains(term) ||
                (e.Description != null && e.Description.ToLower().Contains(term)) ||
                e.Category.ToLower().Contains(term) ||
                e.ExamType.ToLower().Contains(term));
        }

        var total = await query.CountAsync();

        query = (filter.SortBy?.ToLower(), filter.SortOrder?.ToLower()) switch
        {
            ("title", "asc") => query.OrderBy(e => e.Title),
            ("title", _) => query.OrderByDescending(e => e.Title),
            ("popular", _) => query.OrderByDescending(e => e.DownloadCount),
            ("downloads", "asc") => query.OrderBy(e => e.DownloadCount),
            ("downloads", _) => query.OrderByDescending(e => e.DownloadCount),
            ("year", "asc") => query.OrderBy(e => e.Year),
            _ => query.OrderByDescending(e => e.Year).ThenByDescending(e => e.DownloadCount),
        };

        var exams = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        return (exams.Select(ToDto).ToList(), total);
    }

    public async Task<ExamDto> CreateExamAsync(CreateExamRequestDto dto)
    {
        var exam = new Exam
        {
            Title = dto.Title,
            Description = dto.Description,
            ExamType = dto.ExamType,
            Category = dto.Category,
            Year = dto.Year,
            Session = dto.Session,
            Level = dto.Level,
            Difficulty = dto.Difficulty,
            DurationMinutes = dto.DurationMinutes,
            DocumentUrl = dto.DocumentUrl,
            CorrectionUrl = dto.CorrectionUrl,
            SubjectId = dto.SubjectId,
            IsPublished = true,
            IsDeleted = false,
            DownloadCount = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _context.Exams.Add(exam);
        await _context.SaveChangesAsync();
        return ToDto(exam);
    }

    public async Task<ExamDto?> UpdateExamAsync(int id, UpdateExamRequestDto dto)
    {
        var exam = await _context.Exams.FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted);
        if (exam == null) return null;

        if (dto.Title != null) exam.Title = dto.Title;
        if (dto.Description != null) exam.Description = dto.Description;
        if (dto.ExamType != null) exam.ExamType = dto.ExamType;
        if (dto.Category != null) exam.Category = dto.Category;
        if (dto.Year.HasValue) exam.Year = dto.Year.Value;
        if (dto.Session != null) exam.Session = dto.Session;
        if (dto.Level != null) exam.Level = dto.Level;
        if (dto.Difficulty != null) exam.Difficulty = dto.Difficulty;
        if (dto.DurationMinutes.HasValue) exam.DurationMinutes = dto.DurationMinutes;
        if (dto.DocumentUrl != null) exam.DocumentUrl = dto.DocumentUrl;
        if (dto.CorrectionUrl != null) exam.CorrectionUrl = dto.CorrectionUrl;
        if (dto.IsPublished.HasValue) exam.IsPublished = dto.IsPublished.Value;
        if (dto.SubjectId.HasValue) exam.SubjectId = dto.SubjectId;
        exam.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return ToDto(exam);
    }

    public async Task<bool> DeleteExamAsync(int id)
    {
        var exam = await _context.Exams.FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted);
        if (exam == null) return false;

        exam.IsDeleted = true;
        exam.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }
}
