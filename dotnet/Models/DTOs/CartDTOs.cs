namespace Backend.Models.DTOs;

/// <summary>
/// DTO for adding item to cart
/// </summary>
public class AddToCartRequestDto
{
    public int SubjectId { get; set; }
    public decimal Price { get; set; }
    /// <summary>
    /// Device ID for anonymous users. If null, userId from token is used
    /// </summary>
    public string? DeviceId { get; set; }
}

/// <summary>
/// DTO for applying promo code
/// </summary>
public class ApplyPromoCodeDto
{
    public string Code { get; set; } = string.Empty;
}

/// <summary>
/// DTO for a single cart item
/// </summary>
public class CartItemDto
{
    public int Id { get; set; }
    public int SubjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string? Image { get; set; }
    public int Quantity { get; set; }
    public DateTime AddedAt { get; set; }
}

/// <summary>
/// DTO for complete cart response
/// </summary>
public class CartResponseDto
{
    public List<CartItemDto> Items { get; set; } = new();
    public int ItemsCount { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "XAF";
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// DTO for updating user profile
/// </summary>
public class UpdateUserProfileDto
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Phone { get; set; }
    public string? Avatar { get; set; }
}