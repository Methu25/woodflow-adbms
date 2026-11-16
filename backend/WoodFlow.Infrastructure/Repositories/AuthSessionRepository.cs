using Npgsql;
using WoodFlow.Application.Interfaces;
using WoodFlow.Domain;

namespace WoodFlow.Infrastructure.Repositories;

public sealed class AuthSessionRepository(Database database) : IAuthSessionRepository
{
    public async Task<DateTimeOffset?> CreateAsync(User user, byte[] sessionHash, CancellationToken ct)
    {
        await using var c = await database.OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            INSERT INTO public.auth_sessions(session_hash,user_id,auth_version)
            SELECT @hash,user_id,auth_version FROM public.users
            WHERE user_id=@id AND auth_version=@version AND password_hash=@password AND role=@role
                AND role IN ('OWNER','ASSISTANT') AND account_status='ACTIVE' AND is_active IS TRUE
            RETURNING expires_at
            """, c);
        command.Parameters.AddWithValue("hash", sessionHash);
        command.Parameters.AddWithValue("id", user.UserId);
        command.Parameters.AddWithValue("version", user.AuthVersion);
        command.Parameters.AddWithValue("password", user.PasswordHash);
        command.Parameters.AddWithValue("role", user.Role);
        return await command.ExecuteScalarAsync(ct) is DateTime expires ? new DateTimeOffset(expires) : null;
    }
    public async Task<bool> IsActiveAsync(int userId, int authVersion, byte[] sessionHash, CancellationToken ct)
    {
        await using var c = await database.OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS(SELECT 1 FROM public.auth_sessions s JOIN public.users u ON u.user_id=s.user_id
                WHERE s.session_hash=@hash AND s.user_id=@id AND s.auth_version=@version
                    AND s.revoked_at IS NULL AND s.expires_at>clock_timestamp()
                    AND u.auth_version=s.auth_version AND u.account_status='ACTIVE' AND u.is_active IS TRUE
                    AND u.role IN ('OWNER','ASSISTANT'))
            """, c);
        command.Parameters.AddWithValue("hash", sessionHash);
        command.Parameters.AddWithValue("id", userId);
        command.Parameters.AddWithValue("version", authVersion);
        return (bool)(await command.ExecuteScalarAsync(ct))!;
    }
    public async Task RevokeAsync(int userId, byte[] sessionHash, CancellationToken ct)
    {
        await using var c = await database.OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            UPDATE public.auth_sessions SET revoked_at=COALESCE(revoked_at,clock_timestamp())
            WHERE session_hash=@hash AND user_id=@id
            """, c);
        command.Parameters.AddWithValue("hash", sessionHash);
        command.Parameters.AddWithValue("id", userId);
        await command.ExecuteNonQueryAsync(ct);
    }
}
