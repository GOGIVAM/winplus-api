using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;
using QRCoder;

namespace Backend.Services;

public record CourseCertificateVerificationResult(bool IsValid, string? StudentName, string? CourseTitle, decimal? Grade, DateTime? IssuedAt);

public interface ICourseCertificateService
{
    /// <summary>
    /// Génère le certificat si l'élève a atteint 100% (US-3C) — idempotent :
    /// renvoie le certificat existant s'il y en a déjà un pour ce couple.
    /// </summary>
    Task<CourseCertificate> GenerateForCompletedCourseAsync(int userId, int courseId);
    Task<CourseCertificate?> GetMineAsync(int userId, int courseId);
    Task<CourseCertificateVerificationResult> VerifyAsync(string code);
}

public class CourseCertificateService : ICourseCertificateService
{
    private readonly ApplicationDbContext _db;
    private readonly IPdfService _pdf;
    private readonly IStorageService _storage;
    private readonly IConfiguration _config;
    private readonly ILogger<CourseCertificateService> _logger;

    public CourseCertificateService(ApplicationDbContext db, IPdfService pdf, IStorageService storage, IConfiguration config, ILogger<CourseCertificateService> logger)
    {
        _db = db;
        _pdf = pdf;
        _storage = storage;
        _config = config;
        _logger = logger;
    }

    public async Task<CourseCertificate> GenerateForCompletedCourseAsync(int userId, int courseId)
    {
        var existing = await _db.CourseCertificates.FirstOrDefaultAsync(c => c.CourseId == courseId && c.UserId == userId);
        if (existing != null) return existing;

        var course = await _db.Courses.FirstOrDefaultAsync(c => c.Id == courseId)
            ?? throw new KeyNotFoundException("Formation introuvable.");
        if (!course.CertificateEnabled)
            throw new InvalidOperationException("Le certificat n'est pas activé pour cette formation.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new KeyNotFoundException("Utilisateur introuvable.");

        var enrollment = await _db.CourseEnrollments.FirstOrDefaultAsync(e => e.UserId == userId && e.CourseId == courseId);
        if (enrollment == null || enrollment.ProgressPercent < 100)
            throw new InvalidOperationException("La formation n'est pas terminée à 100%.");

        var grade = await _db.QuizAttempts.AsNoTracking()
            .Where(a => a.UserId == userId && _db.CourseLessons.Any(l => l.CourseId == courseId && l.QuizId == a.QuizId))
            .Select(a => (decimal?)a.Score)
            .AverageAsync();

        var code = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var cert = new CourseCertificate
        {
            CourseId = courseId,
            UserId = userId,
            VerificationCode = code,
            Grade = grade,
            IssuedAt = DateTime.UtcNow,
        };
        _db.CourseCertificates.Add(cert);
        await _db.SaveChangesAsync();

        try
        {
            var frontendUrl = _config["App:FrontendUrl"]?.TrimEnd('/') ?? "https://winplus.cm";
            var verifyUrl = $"{frontendUrl}/verify-formation/{code}";

            using var qrGenerator = new QRCodeGenerator();
            using var qrData = qrGenerator.CreateQrCode(verifyUrl, QRCodeGenerator.ECCLevel.M);
            var qrPng = new PngByteQRCode(qrData).GetGraphic(10);

            var pdfBytes = _pdf.GenerateCourseCertificate(cert, user, course, qrPng);
            var key = $"course-certificates/{code}.pdf";
            using var stream = new MemoryStream(pdfBytes);
            cert.FileUrl = await _storage.PutAsync(stream, key, "application/pdf");
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec génération PDF certificat {Code}", code);
        }

        // Reflète l'existant sur CourseEnrollment (déjà consommé par le
        // curriculum côté player) sans dupliquer la logique de progression.
        enrollment.CertificateUrl = cert.FileUrl;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Certificat formation {Code} généré pour user {UserId} / course {CourseId}", code, userId, courseId);
        return cert;
    }

    public async Task<CourseCertificate?> GetMineAsync(int userId, int courseId) =>
        await _db.CourseCertificates.AsNoTracking().FirstOrDefaultAsync(c => c.CourseId == courseId && c.UserId == userId);

    public async Task<CourseCertificateVerificationResult> VerifyAsync(string code)
    {
        var cert = await _db.CourseCertificates.AsNoTracking()
            .Include(c => c.User).Include(c => c.Course)
            .FirstOrDefaultAsync(c => c.VerificationCode == code);

        if (cert == null) return new CourseCertificateVerificationResult(false, null, null, null, null);

        return new CourseCertificateVerificationResult(
            true,
            $"{cert.User!.FirstName} {cert.User.LastName}".Trim(),
            cert.Course!.Title,
            cert.Grade,
            cert.IssuedAt);
    }
}
