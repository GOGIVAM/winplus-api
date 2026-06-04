using Backend.Models.DTOs;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ExamsController : ControllerBase
{
    private readonly IExamService _examService;
    private readonly ILogger<ExamsController> _logger;

    public ExamsController(IExamService examService, ILogger<ExamsController> logger)
    {
        _examService = examService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        try
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);
            var (data, total) = await _examService.GetAllExamsAsync(page, pageSize);
            return Ok(new { data, pagination = Paginate(page, pageSize, total) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur GET /exams");
            return Problem(ex.Message);
        }
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        try
        {
            var exam = await _examService.GetExamByIdAsync(id);
            return exam == null ? NotFound() : Ok(exam);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur GET /exams/{Id}", id);
            return Problem(ex.Message);
        }
    }

    [HttpGet("by-type/{examType}")]
    public async Task<IActionResult> GetByType(string examType, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        try
        {
            var (data, total) = await _examService.GetExamsByTypeAsync(examType, page, pageSize);
            return Ok(new { data, pagination = Paginate(page, pageSize, total) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur GET /exams/by-type/{Type}", examType);
            return Problem(ex.Message);
        }
    }

    [HttpGet("by-subject/{category}")]
    public async Task<IActionResult> GetBySubject(string category, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        try
        {
            var (data, total) = await _examService.GetExamsBySubjectAsync(category, page, pageSize);
            return Ok(new { data, pagination = Paginate(page, pageSize, total) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur GET /exams/by-subject/{Cat}", category);
            return Problem(ex.Message);
        }
    }

    [HttpGet("by-year/{year:int}")]
    public async Task<IActionResult> GetByYear(int year, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        try
        {
            var (data, total) = await _examService.GetExamsByYearAsync(year, page, pageSize);
            return Ok(new { data, pagination = Paginate(page, pageSize, total) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur GET /exams/by-year/{Year}", year);
            return Problem(ex.Message);
        }
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] ExamSearchFilterDto filter)
    {
        try
        {
            filter.Page = Math.Max(1, filter.Page);
            filter.PageSize = Math.Clamp(filter.PageSize, 1, 200);
            var (data, total) = await _examService.SearchExamsAsync(filter);
            return Ok(new { data, pagination = Paginate(filter.Page, filter.PageSize, total) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur GET /exams/search");
            return Problem(ex.Message);
        }
    }

    [HttpPost]
    [Authorize(Roles = "admin,teacher")]
    public async Task<IActionResult> Create([FromBody] CreateExamRequestDto dto)
    {
        try
        {
            var exam = await _examService.CreateExamAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = exam.Id }, exam);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur POST /exams");
            return Problem(ex.Message);
        }
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "admin,teacher")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateExamRequestDto dto)
    {
        try
        {
            var exam = await _examService.UpdateExamAsync(id, dto);
            return exam == null ? NotFound() : Ok(exam);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur PUT /exams/{Id}", id);
            return Problem(ex.Message);
        }
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "admin,teacher")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            return await _examService.DeleteExamAsync(id) ? NoContent() : NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur DELETE /exams/{Id}", id);
            return Problem(ex.Message);
        }
    }

    private static object Paginate(int page, int pageSize, int total) => new
    {
        page, pageSize, totalCount = total,
        totalPages = (int)Math.Ceiling((double)total / pageSize),
        hasNext = page * pageSize < total,
        hasPrevious = page > 1
    };
}
