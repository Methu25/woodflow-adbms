using System.ComponentModel.DataAnnotations;

namespace WoodFlow.Application.DTOs;

public sealed class SubmitRecoveryRequest
{
    [Required, StringLength(50)] public string Username { get; init; } = "";
    [Range(1, int.MaxValue)] public int OwnerId { get; init; }
    public override string ToString() => nameof(SubmitRecoveryRequest);
}
public sealed class ApproveRecoveryRequest
{
    public bool IdentityVerified { get; init; }
}
public sealed class ResolvePendingRecoveryRequest
{
    [Required, StringLength(50)] public string Username { get; init; } = "";
    [Required, RegularExpression("REASSIGN_TO_ME|DENY")] public string Resolution { get; init; } = "";
    public bool IdentityVerified { get; init; }
    public bool Confirmed { get; init; }
    public override string ToString() => nameof(ResolvePendingRecoveryRequest);
}
public sealed class CompleteRecoveryRequest
{
    [Required, RegularExpression("[A-Fa-f0-9]{64}")] public string Code { get; init; } = "";
    [Required, StringLength(128, MinimumLength = 8)] public string NewPassword { get; init; } = "";
    [Required, Compare(nameof(NewPassword))] public string ConfirmPassword { get; init; } = "";
    public override string ToString() => nameof(CompleteRecoveryRequest);
}
// Public contact list deliberately excludes username, email, and account/security details.
public sealed record RecoveryOwnerContact(int OwnerId, string FullName);
public sealed record RecoveryItem(Guid RequestId, int RequesterUserId, string FullName, string Username,
    string Role, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt, int? ProcessedByOwnerId, DateTimeOffset? ProcessedAt,
    DateTimeOffset? ConsumedAt);
public sealed record RecoveryPage(IReadOnlyList<RecoveryItem> Items, int Total, int Page, int PageSize);
public enum RecoveryError { None, Forbidden, NotFound, Conflict }
public sealed record RecoveryResult<T>(T? Value = default, RecoveryError Error = RecoveryError.None);
public sealed record IssuedResetCode(string Code, DateTimeOffset ExpiresAt)
{
    public override string ToString() => nameof(IssuedResetCode);
}
