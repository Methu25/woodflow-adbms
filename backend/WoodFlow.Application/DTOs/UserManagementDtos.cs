using System.ComponentModel.DataAnnotations;

namespace WoodFlow.Application.DTOs;

public sealed class CreateUserRequest
{
    [Required, StringLength(100)] public string FullName { get; init; } = "";
    [Required, RegularExpression(@"[A-Za-z0-9_.-]{3,50}")] public string Username { get; init; } = "";
    [Required, StringLength(128, MinimumLength = 8)] public string Password { get; init; } = "";
    [Required, RegularExpression("OWNER|ASSISTANT")] public string Role { get; init; } = "";
    public override string ToString() => nameof(CreateUserRequest);
}

public sealed class ChangeUserRequest
{
    [Required] public string ExpectedStatus { get; init; } = "";
    [Required] public string ExpectedRole { get; init; } = "";
    [Required] public string Status { get; init; } = "";
    [Required] public string Role { get; init; } = "";
    [Range(0, int.MaxValue)] public int ExpectedVersion { get; init; }
    public bool Confirmed { get; init; }
}

// Safe records only: password hashes never cross the repository boundary here.
public sealed record ManagedUser(int UserId, string FullName, string Username, string Role,
    string Status, DateTimeOffset? CreatedAt, int Version);
public sealed record UserCounts(int ActiveOwners, int ActiveAssistants, int Suspended, int Deactivated);
public sealed record UserPage(IReadOnlyList<ManagedUser> Items, int Total, int Page, int PageSize);
public enum UserWriteError { None, DuplicateUsername, NotFound, Conflict, Forbidden, SelfChange, LastOwner, Deactivated, InvalidTransition }
public sealed record UserWriteResult(ManagedUser? User = null, UserWriteError Error = UserWriteError.None);
