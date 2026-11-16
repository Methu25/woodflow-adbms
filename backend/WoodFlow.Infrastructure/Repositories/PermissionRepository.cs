using Npgsql;
using NpgsqlTypes;
using WoodFlow.Application.DTOs;
using WoodFlow.Application.Interfaces;

namespace WoodFlow.Infrastructure.Repositories;

public sealed partial class PermissionRepository(Database database, IPasswordService passwords) : IPermissionRepository
{
    private static async Task<PermissionSettings> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, bool locked, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT granted_keys, version FROM public.assistant_permission_settings WHERE id=1" + (locked ? " FOR UPDATE" : ""), connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Permission configuration is required.");
        return new(reader.GetFieldValue<string[]>(0), reader.GetInt64(1));
    }
    public async Task<PermissionSettings> GetAsync(CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        return await ReadAsync(connection, null, false, ct);
    }
    public async Task<PermissionSaveResult> SaveAsync(int actorId, string[] grantedKeys, long expectedVersion, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        // Match account-management lock order so a concurrently demoted/suspended OWNER cannot write.
        await using (var gate = new NpgsqlCommand("SELECT pg_advisory_xact_lock(815043271)", connection, transaction))
            await gate.ExecuteNonQueryAsync(ct);
        await using (var actor = new NpgsqlCommand("SELECT password_hash FROM public.users WHERE user_id=@id AND role='OWNER' AND account_status='ACTIVE' AND is_active IS TRUE FOR SHARE", connection, transaction))
        {
            actor.Parameters.AddWithValue("id", actorId);
            if (await actor.ExecuteScalarAsync(ct) is not string hash || !passwords.IsUsableHash(hash))
                return new(Error: PermissionSaveError.Forbidden);
        }
        var before = await ReadAsync(connection, transaction, true, ct);
        if (before.Version != expectedVersion) return new(Error: PermissionSaveError.Conflict);
        if (before.GrantedKeys.Order(StringComparer.Ordinal).SequenceEqual(grantedKeys))
        {
            await transaction.CommitAsync(ct); return new(before);
        }
        await using var update = new NpgsqlCommand("UPDATE public.assistant_permission_settings SET granted_keys=@keys, version=version+1 WHERE id=1 RETURNING version", connection, transaction);
        update.Parameters.AddWithValue("keys", NpgsqlDbType.Array | NpgsqlDbType.Text, grantedKeys);
        var version = (long)(await update.ExecuteScalarAsync(ct))!;
        // Part 3 integration point only: actorId, before, grantedKeys/version, before commit.
        // Audit logging is owned by another teammate and is intentionally not implemented here.
        await transaction.CommitAsync(ct);
        return new(new(grantedKeys, version));
    }
}
