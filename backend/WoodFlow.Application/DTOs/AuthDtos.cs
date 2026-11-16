using System.ComponentModel.DataAnnotations;

namespace WoodFlow.Application.DTOs;

public sealed class LoginRequest
{
    [Required, StringLength(50)]
    public string Username { get; init; } = "";
    [Required, StringLength(128)]
    public string Password { get; init; } = "";
}

public sealed class InitializeOwnerRequest
{
    [Required, StringLength(100)]
    public string FullName { get; init; } = "";
    [Required, StringLength(50), RegularExpression(@"[A-Za-z0-9_.-]{3,50}")]
    public string Username { get; init; } = "";
    [Required, StringLength(128, MinimumLength = 12)]
    public string Password { get; init; } = "";
    [Required, Compare(nameof(Password))]
    public string ConfirmPassword { get; init; } = "";
    [Required, StringLength(64, MinimumLength = 64)]
    public string SetupCode { get; init; } = "";
}

public sealed record AccountResponse(int UserId, string Username, string FullName, string Role);
