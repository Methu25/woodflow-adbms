using Npgsql;
using NpgsqlTypes;
using WoodFlow.Application.DTOs;

namespace WoodFlow.Infrastructure.Repositories;

public sealed partial class PermissionRepository
{
    private static async Task<UserPermissionSettings?> ReadUserAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, int id, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            SELECT u.user_id,u.full_name,u.username,u.role,u.account_status,u.created_at,u.auth_version,
                COALESCE(p.granted_keys,r.granted_keys), p.granted_keys IS NULL, COALESCE(p.version,0),r.version
            FROM public.users u CROSS JOIN public.assistant_permission_settings r
            LEFT JOIN public.assistant_user_permissions p ON p.user_id=u.user_id
            WHERE u.user_id=@id AND u.role='ASSISTANT' AND r.id=1
            """, connection, transaction);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var user = new ManagedUser(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5), reader.GetInt32(6));
        return new(user, reader.GetFieldValue<string[]>(7), reader.GetBoolean(8), reader.GetInt64(9), reader.GetInt64(10));
    }
    public async Task<UserPermissionSettings?> GetUserAsync(int userId, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        return await ReadUserAsync(connection, null, userId, ct);
    }
    public async Task<PermissionSettings> GetEffectiveAsync(int userId, CancellationToken ct)
    {
        var settings = await GetUserAsync(userId, ct) ?? throw new InvalidOperationException("Assistant permission configuration is required.");
        return new(settings.GrantedKeys, settings.Version);
    }
    public async Task<UserPermissionSaveResult> SaveUserAsync(int actorId, int userId, SaveUserPermissionsRequest request, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var gate = new NpgsqlCommand("SELECT pg_advisory_xact_lock(815043271)", connection, transaction))
            await gate.ExecuteNonQueryAsync(ct);
        // Same order as other account writes, also excluding concurrent legacy/direct updates.
        await using (var gate = new NpgsqlCommand("LOCK TABLE public.users IN SHARE ROW EXCLUSIVE MODE", connection, transaction))
            await gate.ExecuteNonQueryAsync(ct);
        await using (var actor = new NpgsqlCommand("SELECT password_hash FROM public.users WHERE user_id=@id AND role='OWNER' AND account_status='ACTIVE' AND is_active IS TRUE", connection, transaction))
        {
            actor.Parameters.AddWithValue("id", actorId);
            if (await actor.ExecuteScalarAsync(ct) is not string hash || !passwords.IsUsableHash(hash))
                return new(Error: PermissionSaveError.Forbidden);
        }
        // Serialize role-default writes and compare both versions in the same transaction.
        await ReadAsync(connection, transaction, true, ct);
        var before = await ReadUserAsync(connection, transaction, userId, ct);
        if (before is null) return new(Error: PermissionSaveError.NotFound);
        if (before.User.Status == "DEACTIVATED") return new(Error: PermissionSaveError.Deactivated);
        if (before.Version != request.ExpectedVersion || before.RoleVersion != request.ExpectedRoleVersion ||
            before.User.Version != request.ExpectedAccountVersion) return new(Error: PermissionSaveError.Conflict);
        await using var update = new NpgsqlCommand("""
            INSERT INTO public.assistant_user_permissions (user_id,granted_keys,version) VALUES (@id,@keys,1)
            ON CONFLICT (user_id) DO UPDATE SET granted_keys=EXCLUDED.granted_keys,
                version=assistant_user_permissions.version+1
            """, connection, transaction);
        update.Parameters.AddWithValue("id", userId);
        update.Parameters.Add("keys", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = request.UseRoleDefaults
            ? DBNull.Value : request.GrantedKeys.Order(StringComparer.Ordinal).ToArray();
        await update.ExecuteNonQueryAsync(ct);
        var result = await ReadUserAsync(connection, transaction, userId, ct);
        // Future audit integration point: actorId, before, result, before commit. No audit implementation here.
        await transaction.CommitAsync(ct);
        return new(result);
    }
}
