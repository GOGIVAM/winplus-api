using System.ComponentModel.DataAnnotations;

namespace Backend.Models.DTOs;

public class CreateThreadRequest
{
    [Required][MaxLength(255)] public string Title { get; set; } = string.Empty;
    [Required] public string Content { get; set; } = string.Empty;
    [Required][MaxLength(100)] public string Category { get; set; } = string.Empty;
    [MaxLength(100)] public string? Tag { get; set; }
}

public class CreatePostRequest
{
    [Required] public string Content { get; set; } = string.Empty;
}

public class VoteRequest
{
    [Required][MaxLength(10)] public string Type { get; set; } = "up";
}

public class ForumThreadResponse
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string? AuthorName { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Tag { get; set; }
    public bool IsPinned { get; set; }
    public bool IsSolved { get; set; }
    public int ViewsCount { get; set; }
    public int RepliesCount { get; set; }
    public int Upvotes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ForumThreadListResponse
{
    public List<ForumThreadResponse> Threads { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

public class ForumPostResponse
{
    public int Id { get; set; }
    public int ThreadId { get; set; }
    public int UserId { get; set; }
    public string? AuthorName { get; set; }
    public string Content { get; set; } = string.Empty;
    public int Upvotes { get; set; }
    public bool IsAccepted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ForumPostListResponse
{
    public List<ForumPostResponse> Posts { get; set; } = new();
    public int Total { get; set; }
}
