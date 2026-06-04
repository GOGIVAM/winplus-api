namespace Backend.Models.DTOs;

/// <summary>
/// Wrapper de réponse paginée générique
/// </summary>
public class PaginatedResponseDto<T>
{
    public List<T> Data { get; set; } = new();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int Total { get; set; } = 0;
    public int TotalPages => Total > 0 ? (int)Math.Ceiling(Total / (double)PageSize) : 0;
    public bool HasMore => Page < TotalPages;
    public bool Success { get; set; } = true;
    public string? Message { get; set; }
}

/// <summary>
/// Wrapper de réponse générique (simple)
/// </summary>
public class ApiResponseDto<T>
{
    public T? Data { get; set; }
    public bool Success { get; set; } = true;
    public string? Message { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Wrapper de réponse pour les opérations sans contenu
/// </summary>
public class ApiResponseDto
{
    public bool Success { get; set; } = true;
    public string? Message { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Wrapper de réponse d'erreur
/// </summary>
public class ApiErrorResponseDto
{
    public bool Success { get; set; } = false;
    public string? Message { get; set; }
    public List<string>? Errors { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}
