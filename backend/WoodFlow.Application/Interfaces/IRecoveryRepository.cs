using WoodFlow.Application.DTOs;

namespace WoodFlow.Application.Interfaces;

public interface IRecoveryRepository
{
    Task<IReadOnlyList<RecoveryOwnerContact>> OwnersAsync(CancellationToken ct);
    Task SubmitAsync(string username, int ownerId, CancellationToken ct);
    Task<RecoveryResult<RecoveryPage>> InboxAsync(int actorId, string? status, int page, int pageSize, CancellationToken ct);
    Task<RecoveryResult<int>> PendingCountAsync(int actorId, CancellationToken ct);
    Task<RecoveryResult<RecoveryItem>> DetailsAsync(int actorId, Guid id, CancellationToken ct);
    Task<RecoveryResult<DateTimeOffset>> ProcessAsync(int actorId, Guid id, byte[]? codeHash, CancellationToken ct);
    Task<bool> CompleteAsync(byte[] codeHash, string passwordHash, CancellationToken ct);
    Task<RecoveryError> ResolvePendingAsync(int actorId, string username, bool deny, CancellationToken ct);
}
