using System.Data;
using Npgsql;
using WoodFlow.Application.DTOs;
using WoodFlow.Application.Interfaces;
using WoodFlow.Application.Services;
using WoodFlow.Domain;

namespace WoodFlow.Infrastructure.Repositories;

public sealed class UserManagementRepository(Database database, IPasswordService passwords) : IUserManagementRepository
{
    private const string Columns = "user_id, full_name, username, role, account_status, created_at, auth_version";
    private static ManagedUser Read(NpgsqlDataReader r) => new(r.GetInt32(0), r.GetString(1), r.GetString(2),
        r.GetString(3), r.GetString(4), r.IsDBNull(5) ? null : r.GetFieldValue<DateTimeOffset>(5), r.GetInt32(6));

    public async Task<UserCounts> CountsAsync(CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            SELECT count(*) FILTER (WHERE account_status='ACTIVE' AND role='OWNER')::int,
                count(*) FILTER (WHERE account_status='ACTIVE' AND role='ASSISTANT')::int,
                count(*) FILTER (WHERE account_status='SUSPENDED')::int,
                count(*) FILTER (WHERE account_status='DEACTIVATED')::int FROM public.users
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3));
    }

    public async Task<UserPage> ListAsync(string category, string search, int page, int pageSize, CancellationToken ct)
    {
        var (status, role) = category switch
        {
            "owners" => (AccountStatuses.Active, Roles.Owner),
            "assistants" => (AccountStatuses.Active, Roles.Assistant),
            "suspended" => (AccountStatuses.Suspended, ""),
            "deactivated" => (AccountStatuses.Deactivated, ""),
            _ => throw new ArgumentException("Invalid category.")
        };
        const string where = " WHERE account_status=@status AND (@role='' OR role=@role) AND " +
            "(@search='' OR strpos(lower(full_name),lower(@search))>0 OR strpos(lower(username),lower(@search))>0)";
        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        NpgsqlCommand Command(string sql)
        {
            var c = new NpgsqlCommand(sql, connection, transaction);
            c.Parameters.AddWithValue("status", status);
            c.Parameters.AddWithValue("role", role);
            c.Parameters.AddWithValue("search", search);
            return c;
        }
        await using var count = Command("SELECT count(*)::int FROM public.users" + where);
        var total = (int)(await count.ExecuteScalarAsync(ct))!;
        page = Math.Min(page, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
        await using var command = Command("SELECT " + Columns + " FROM public.users" + where +
            " ORDER BY created_at DESC NULLS LAST, user_id DESC LIMIT @limit OFFSET @offset");
        command.Parameters.AddWithValue("limit", pageSize);
        command.Parameters.AddWithValue("offset", (page - 1) * pageSize);
        var users = new List<ManagedUser>();
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) users.Add(Read(reader));
        await transaction.CommitAsync(ct);
        return new(users, total, page, pageSize);
    }

    public async Task<ManagedUser?> FindAsync(int id, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        return await FindAsync(connection, null, id, ct);
    }

    private static async Task<ManagedUser?> FindAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, int id, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT " + Columns + " FROM public.users WHERE user_id=@id", connection, transaction);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    private async Task<bool> LockAndAuthorizeAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int actorId, CancellationToken ct)
    {
        // Same lock as first-owner setup. Serializes management writes across API instances.
        await using (var gate = new NpgsqlCommand("SELECT pg_advisory_xact_lock(815043271)", connection, transaction))
            await gate.ExecuteNonQueryAsync(ct);
        // Also exclude concurrent direct/legacy users writes while checking the owner invariant.
        await using (var tableLock = new NpgsqlCommand("LOCK TABLE public.users IN SHARE ROW EXCLUSIVE MODE", connection, transaction))
            await tableLock.ExecuteNonQueryAsync(ct);
        await using var actor = new NpgsqlCommand("""
            SELECT password_hash FROM public.users
            WHERE user_id=@id AND role='OWNER' AND account_status='ACTIVE' AND is_active IS TRUE
            """, connection, transaction);
        actor.Parameters.AddWithValue("id", actorId);
        return await actor.ExecuteScalarAsync(ct) is string hash && passwords.IsUsableHash(hash);
    }

    public async Task<UserWriteResult> CreateAsync(int actorId, string name, string username, string hash, string role, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        if (!await LockAndAuthorizeAsync(connection, transaction, actorId, ct)) return new(Error: UserWriteError.Forbidden);
        await using (var duplicate = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM public.users WHERE lower(username)=lower(@username))", connection, transaction))
        {
            duplicate.Parameters.AddWithValue("username", username);
            if ((bool)(await duplicate.ExecuteScalarAsync(ct))!) return new(Error: UserWriteError.DuplicateUsername);
        }
        await using var insert = new NpgsqlCommand("""
            INSERT INTO public.users (full_name, username, password_hash, role, account_status, is_active)
            VALUES (@name,@username,@hash,@role,'ACTIVE',true) RETURNING user_id
            """, connection, transaction);
        insert.Parameters.AddWithValue("name", name);
        insert.Parameters.AddWithValue("username", username);
        insert.Parameters.AddWithValue("hash", hash);
        insert.Parameters.AddWithValue("role", role);
        try
        {
            var id = (int)(await insert.ExecuteScalarAsync(ct))!;
            var result = await FindAsync(connection, transaction, id, ct);
            // Future audit integration belongs inside this transaction, before commit.
            // Do not emit credentials/password hashes. Part 3 owns that implementation.
            await transaction.CommitAsync(ct);
            return new(result);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        { return new(Error: UserWriteError.DuplicateUsername); }
    }

    public async Task<UserWriteResult> ChangeAsync(int actorId, int id, ChangeUserRequest request, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        if (!await LockAndAuthorizeAsync(connection, transaction, actorId, ct)) return new(Error: UserWriteError.Forbidden);
        var target = await FindAsync(connection, transaction, id, ct);
        if (target is null) return new(Error: UserWriteError.NotFound);
        var owners = 0;
        var targetIsUsableOwner = false;
        await using (var command = new NpgsqlCommand("SELECT user_id, password_hash FROM public.users WHERE role='OWNER' AND account_status='ACTIVE' AND is_active IS TRUE", connection, transaction))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                if (passwords.IsUsableHash(reader.GetString(1)))
                {
                    owners++;
                    if (reader.GetInt32(0) == target.UserId) targetIsUsableOwner = true;
                }
        // Only removing a usable OWNER can reduce the usable-owner count.
        var error = UserManagementRules.Check(actorId, target, request, owners, targetIsUsableOwner);
        if (error != UserWriteError.None) return new(Error: error);
        await using var update = new NpgsqlCommand("""
            UPDATE public.users SET role=@role, account_status=@status, is_active=(@status='ACTIVE')
            WHERE user_id=@id
            """, connection, transaction);
        update.Parameters.AddWithValue("role", request.Role);
        update.Parameters.AddWithValue("status", request.Status);
        update.Parameters.AddWithValue("id", id);
        await update.ExecuteNonQueryAsync(ct);
        var result = await FindAsync(connection, transaction, id, ct);
        // Future audit integration: actorId, target (before), result (after), same transaction.
        await transaction.CommitAsync(ct);
        return new(result);
    }
}
