using Backend.Data;
using Backend.Extensions;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

/// <summary>
/// Moyenne scolaire réelle de l'élève (bulletin), par année. Saisie par
/// l'élève lui-même ou par un parent lié (ParentStudentLink) — le premier des
/// deux qui la renseigne, l'autre peut la corriger ensuite. Sert de signal
/// supplémentaire pour les recommandations WinAI (quiz, révisions, épreuves),
/// aux côtés du niveau scolaire, des téléchargements et des objectifs.
/// </summary>
[ApiController]
[Route("api/academic-records")]
[Authorize]
public class AcademicRecordsController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public AcademicRecordsController(ApplicationDbContext db)
    {
        _db = db;
    }

    private static AcademicRecordDto ToDto(AcademicRecord r) => new()
    {
        Id = r.Id,
        StudentId = r.StudentId,
        SchoolYear = r.SchoolYear,
        AverageGrade = r.AverageGrade,
        RecordedByUserId = r.RecordedByUserId,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
    };

    private async Task<bool> IsParentOf(int parentId, int studentId) =>
        await _db.ParentStudentLinks.AnyAsync(l => l.ParentId == parentId && l.StudentId == studentId);

    private static bool IsValidGrade(decimal grade) => grade is >= 0 and <= 20;

    /// <summary>Mes propres moyennes (élève).</summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(IEnumerable<AcademicRecordDto>), 200)]
    public async Task<IActionResult> GetMine()
    {
        var userId = User.GetUserId();
        var records = await _db.AcademicRecords
            .AsNoTracking()
            .Where(r => r.StudentId == userId)
            .OrderByDescending(r => r.SchoolYear)
            .ToListAsync();

        return Ok(records.Select(ToDto));
    }

    /// <summary>Crée ou corrige ma propre moyenne pour une année (élève).</summary>
    [HttpPut("me")]
    [ProducesResponseType(typeof(AcademicRecordDto), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> UpsertMine([FromBody] UpsertAcademicRecordRequestDto request)
    {
        var userId = User.GetUserId();
        return await Upsert(userId, userId, request);
    }

    /// <summary>Moyennes d'un enfant lié (parent).</summary>
    [HttpGet("student/{studentId:int}")]
    [ProducesResponseType(typeof(IEnumerable<AcademicRecordDto>), 200)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> GetForStudent(int studentId)
    {
        var parentId = User.GetUserId();
        if (!await IsParentOf(parentId, studentId))
            return Forbid();

        var records = await _db.AcademicRecords
            .AsNoTracking()
            .Where(r => r.StudentId == studentId)
            .OrderByDescending(r => r.SchoolYear)
            .ToListAsync();

        return Ok(records.Select(ToDto));
    }

    /// <summary>Crée ou corrige la moyenne d'un enfant lié pour une année (parent).</summary>
    [HttpPut("student/{studentId:int}")]
    [ProducesResponseType(typeof(AcademicRecordDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> UpsertForStudent(int studentId, [FromBody] UpsertAcademicRecordRequestDto request)
    {
        var parentId = User.GetUserId();
        if (!await IsParentOf(parentId, studentId))
            return Forbid();

        return await Upsert(studentId, parentId, request);
    }

    private async Task<IActionResult> Upsert(int studentId, int recordedByUserId, UpsertAcademicRecordRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.SchoolYear))
            return BadRequest(new { error = "Année scolaire requise (ex: 2025-2026)." });
        if (!IsValidGrade(request.AverageGrade))
            return BadRequest(new { error = "La moyenne doit être comprise entre 0 et 20." });

        var record = await _db.AcademicRecords
            .FirstOrDefaultAsync(r => r.StudentId == studentId && r.SchoolYear == request.SchoolYear);

        if (record == null)
        {
            record = new AcademicRecord
            {
                StudentId = studentId,
                SchoolYear = request.SchoolYear,
                AverageGrade = request.AverageGrade,
                RecordedByUserId = recordedByUserId,
                CreatedAt = DateTime.UtcNow,
            };
            _db.AcademicRecords.Add(record);
        }
        else
        {
            record.AverageGrade = request.AverageGrade;
            record.RecordedByUserId = recordedByUserId;
            record.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return Ok(ToDto(record));
    }
}
