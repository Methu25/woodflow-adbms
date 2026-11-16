using System.Collections.Concurrent;
using WoodFlow.Application.Interfaces;
using WoodFlow.Domain;

namespace WoodFlow.Tests;

// Shared only by test hosts. Production always uses the PostgreSQL session repository.
internal sealed class TestAuthSessions(IUserRepository users, TimeProvider clock) : IAuthSessionRepository
{
    private readonly ConcurrentDictionary<string, Entry> _sessions = new();
    private sealed record Entry(int UserId, int Version, DateTimeOffset ExpiresAt, bool Revoked = false);
    public bool FailRevocation { get; set; }
    public async Task<DateTimeOffset?> CreateAsync(User user, byte[] sessionHash, CancellationToken ct)
    {
        var current = await users.FindByIdAsync(user.UserId, ct);
        if (current is not { IsActive: true } || current.AuthVersion != user.AuthVersion || current.PasswordHash != user.PasswordHash) return null;
        var expires = clock.GetUtcNow().AddHours(8);
        _sessions[Convert.ToHexString(sessionHash)] = new(user.UserId, user.AuthVersion, expires);
        return expires;
    }
    public async Task<bool> IsActiveAsync(int id, int version, byte[] hash, CancellationToken ct)
    {
        var current = await users.FindByIdAsync(id, ct);
        return _sessions.TryGetValue(Convert.ToHexString(hash), out var s) && !s.Revoked && s.UserId == id &&
            s.Version == version && s.ExpiresAt > clock.GetUtcNow() && current is { IsActive: true } && current.AuthVersion == version;
    }
    public Task RevokeAsync(int id, byte[] hash, CancellationToken ct)
    {
        if (FailRevocation) throw new InvalidOperationException("Simulated session-store outage.");
        var key = Convert.ToHexString(hash);
        if (_sessions.TryGetValue(key, out var s) && s.UserId == id) _sessions[key] = s with { Revoked = true };
        return Task.CompletedTask;
    }
}
