using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public class TutorInsightsService : ITutorInsightsService
{
    private readonly ApplicationDbContext _context;

    public TutorInsightsService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<object>> GetStudentsWithHistoryAsync(int tutorUserId)
    {
        var bookings = await _context.TutorBookings
            .AsNoTracking()
            .Include(b => b.Student)
            .Where(b => b.TutorProfile!.UserId == tutorUserId && b.Status == "completed")
            .ToListAsync();

        return bookings
            .GroupBy(b => b.StudentUserId)
            .Select(g => new
            {
                studentUserId = g.Key,
                studentName = g.First().Student != null ? $"{g.First().Student!.FirstName} {g.First().Student!.LastName}".Trim() : "Élève",
                sessionsCount = g.Count(),
                lastSubject = g.OrderByDescending(b => b.SessionDate).First().Subject,
            })
            .Cast<object>()
            .ToList();
    }

    public async Task<(string StudentName, string? Subject, List<string> Summaries)> GetStudentHistoryAsync(int tutorUserId, int studentUserId)
    {
        var bookings = await _context.TutorBookings
            .AsNoTracking()
            .Include(b => b.Student)
            .Where(b => b.TutorProfile!.UserId == tutorUserId && b.StudentUserId == studentUserId && b.Status == "completed")
            .OrderByDescending(b => b.SessionDate)
            .ToListAsync();

        var studentName = bookings.FirstOrDefault()?.Student is { } s ? $"{s.FirstName} {s.LastName}".Trim() : "Élève";
        var subject = bookings.FirstOrDefault()?.Subject;
        var summaries = bookings.Where(b => !string.IsNullOrWhiteSpace(b.SummaryText)).Select(b => b.SummaryText!).ToList();
        return (studentName, subject, summaries);
    }

    public async Task<TutorRevisionSheet?> GetRevisionSheetAsync(int tutorUserId, int studentUserId) =>
        await _context.TutorRevisionSheets
            .FirstOrDefaultAsync(s => s.TutorUserId == tutorUserId && s.StudentUserId == studentUserId);

    public async Task<TutorRevisionSheet> SaveRevisionSheetAsync(int tutorUserId, int studentUserId, string? subject, string content)
    {
        var sheet = await _context.TutorRevisionSheets
            .FirstOrDefaultAsync(s => s.TutorUserId == tutorUserId && s.StudentUserId == studentUserId);

        if (sheet == null)
        {
            sheet = new TutorRevisionSheet { TutorUserId = tutorUserId, StudentUserId = studentUserId };
            _context.TutorRevisionSheets.Add(sheet);
        }
        sheet.Subject = subject;
        sheet.Content = content;
        sheet.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return sheet;
    }

    public async Task<TutorCoachingReport?> GetLatestCoachingReportAsync(int tutorUserId) =>
        await _context.TutorCoachingReports
            .Where(r => r.TutorUserId == tutorUserId)
            .OrderByDescending(r => r.GeneratedAt)
            .FirstOrDefaultAsync();

    public async Task<(string MonthLabel, List<string> Reviews, List<string> Subjects, int SessionsCount, double? AverageRating)> GetCoachingSourceDataAsync(
        int tutorUserId, DateTime monthStart, DateTime monthEnd)
    {
        var monthLabel = monthStart.ToString("MMMM yyyy", new System.Globalization.CultureInfo("fr-FR"));

        var reviews = await _context.TutorReviews
            .AsNoTracking()
            .Where(r => r.TutorProfile!.UserId == tutorUserId && r.CreatedAt >= monthStart && r.CreatedAt < monthEnd)
            .ToListAsync();

        var bookings = await _context.TutorBookings
            .AsNoTracking()
            .Where(b => b.TutorProfile!.UserId == tutorUserId && b.Status == "completed"
                && b.CompletedAt != null && b.CompletedAt >= monthStart && b.CompletedAt < monthEnd)
            .ToListAsync();

        var reviewTexts = reviews.Where(r => !string.IsNullOrWhiteSpace(r.Comment))
            .Select(r => $"{r.Rating}/5 — {r.Comment}").ToList();
        var subjects = bookings.Where(b => !string.IsNullOrWhiteSpace(b.Subject)).Select(b => b.Subject!).Distinct().ToList();
        var avgRating = reviews.Count > 0 ? reviews.Average(r => r.Rating) : (double?)null;

        return (monthLabel, reviewTexts, subjects, bookings.Count, avgRating);
    }

    public async Task<TutorCoachingReport> SaveCoachingReportAsync(int tutorUserId, string monthLabel, string content)
    {
        var report = new TutorCoachingReport { TutorUserId = tutorUserId, MonthLabel = monthLabel, Content = content };
        _context.TutorCoachingReports.Add(report);
        await _context.SaveChangesAsync();
        return report;
    }
}
