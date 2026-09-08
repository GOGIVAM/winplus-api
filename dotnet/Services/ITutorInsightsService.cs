using Backend.Models.Entities;

namespace Backend.Services;

/// <summary>WinAI côté répétiteur : fiche de révision élève (US-REP-11) et coaching mensuel (US-REP-12).</summary>
public interface ITutorInsightsService
{
    /// <summary>Élèves ayant eu au moins une séance effectuée avec ce répétiteur, pour choisir à qui générer une fiche.</summary>
    Task<List<object>> GetStudentsWithHistoryAsync(int tutorUserId);

    /// <summary>Comptes-rendus de séance disponibles pour ce couple répétiteur/élève, source de la fiche de révision.</summary>
    Task<(string StudentName, string? Subject, List<string> Summaries)> GetStudentHistoryAsync(int tutorUserId, int studentUserId);

    Task<TutorRevisionSheet?> GetRevisionSheetAsync(int tutorUserId, int studentUserId);
    Task<TutorRevisionSheet> SaveRevisionSheetAsync(int tutorUserId, int studentUserId, string? subject, string content);

    Task<TutorCoachingReport?> GetLatestCoachingReportAsync(int tutorUserId);
    Task<(string MonthLabel, List<string> Reviews, List<string> Subjects, int SessionsCount, double? AverageRating)> GetCoachingSourceDataAsync(int tutorUserId, DateTime monthStart, DateTime monthEnd);
    Task<TutorCoachingReport> SaveCoachingReportAsync(int tutorUserId, string monthLabel, string content);
}
