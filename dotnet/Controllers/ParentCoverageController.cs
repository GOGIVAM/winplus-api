using Backend.Data;
using Backend.Extensions;
using Backend.Services.Entitlements;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

public sealed class CoveredChildrenRequest
{
    public List<int> StudentIds { get; set; } = new();
}

/// <summary>
/// Lot 9.7b (étude 1.4) : enfants couverts par le plan du parent.
///
/// GET /api/parent/covered-children
///   → { max (null si illimité), unlimited, selectionNeeded, customSelection,
///       canChange, lastChangedAt, nextChangeAt,
///       children: [{ studentId, name, avatarUrl, linkedAt, covered, chosen }] }
/// PUT /api/parent/covered-children  { studentIds: [..] }
///   → même forme ; 400 (choix invalide) ou 409 (changement déjà fait dans la
///     période) avec { error, code }.
/// Règles : voir <see cref="ParentCoverage"/>.
/// </summary>
[ApiController]
[Route("api/parent/covered-children")]
[Authorize(Policy = "ParentOnly")]
public class ParentCoverageController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IEntitlementService _entitlements;

    public ParentCoverageController(ApplicationDbContext db, IEntitlementService entitlements)
    {
        _db = db;
        _entitlements = entitlements;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var status = await ParentCoverage.GetAsync(_db, _entitlements, User.GetUserId(), DateTime.UtcNow, ct);
        return Ok(Shape(status));
    }

    [HttpPut]
    public async Task<IActionResult> Put([FromBody] CoveredChildrenRequest request, CancellationToken ct)
    {
        var outcome = await ParentCoverage.SetAsync(_db, _entitlements, User.GetUserId(),
            request?.StudentIds ?? new List<int>(), DateTime.UtcNow, ct);
        if (outcome.Ok) return Ok(Shape(outcome.Status!));
        var body = new { error = outcome.Error, code = outcome.Code };
        return outcome.Code == "change_limit" ? Conflict(body) : BadRequest(body);
    }

    private static object Shape(ParentCoverage.Status s) => new
    {
        max = s.Unlimited ? (long?)null : s.Max,
        unlimited = s.Unlimited,
        selectionNeeded = s.SelectionNeeded,
        customSelection = s.CustomSelection,
        canChange = s.CanChange,
        lastChangedAt = s.LastChangedAt,
        nextChangeAt = s.NextChangeAt,
        children = s.Children.Select(c => new
        {
            studentId = c.StudentId,
            name = c.Name,
            avatarUrl = c.AvatarUrl,
            linkedAt = c.LinkedAt,
            covered = c.Covered,
            chosen = c.Chosen,
        }),
    };
}
