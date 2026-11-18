using Npgsql;
using WoodFlow.Application.DTOs;
using WoodFlow.Application.Interfaces;
using WoodFlow.Domain;

namespace WoodFlow.Infrastructure.Repositories;

public sealed class RecoveryRepository(Database database, IPasswordService passwords) : IRecoveryRepository
{
    private static NpgsqlCommand Command(NpgsqlConnection c, NpgsqlTransaction? t, string sql,
        params (string Name, object Value)[] args)
    {
        var command = new NpgsqlCommand(sql, c, t);
        foreach (var (name, value) in args) command.Parameters.AddWithValue(name, value);
        return command;
    }

    private static async Task<User?> UserAsync(NpgsqlConnection c, NpgsqlTransaction? t, int id, CancellationToken ct)
    {
        await using var command = Command(c, t, """
            SELECT user_id, username, password_hash, full_name, role,
                account_status='ACTIVE' AND is_active IS TRUE, auth_version
            FROM public.users WHERE user_id=@id
            """, ("id", id));
        await using var r = await command.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? new(r.GetInt32(0), r.GetString(1), r.GetString(2),
            r.GetString(3), r.GetString(4), r.GetBoolean(5), r.GetInt32(6)) : null;
    }
    private bool Eligible(User? user) => user is { IsActive: true } &&
        user.Role is Roles.Owner or Roles.Assistant && passwords.IsUsableHash(user.PasswordHash);
    private bool Owner(User? user) => Eligible(user) && user!.Role == Roles.Owner;

    private static async Task LockAsync(NpgsqlConnection c, NpgsqlTransaction t, CancellationToken ct)
    {
        // Same lock/order as user management and bootstrap; safe across backend instances.
        await using var command = Command(c, t, """
            SELECT pg_advisory_xact_lock(815043271);
            LOCK TABLE public.users IN SHARE ROW EXCLUSIVE MODE;
            UPDATE public.password_recovery_requests r
            SET status='EXPIRED', code_hash=NULL, updated_at=clock_timestamp()
            FROM public.users u, public.users o
            WHERE u.user_id=r.requester_user_id AND o.user_id=r.selected_owner_id
                AND r.status IN ('PENDING','APPROVED') AND (
                    r.expires_at <= clock_timestamp() OR u.account_status<>'ACTIVE' OR u.is_active IS NOT TRUE
                    OR u.auth_version<>r.requester_auth_version OR o.account_status<>'ACTIVE'
                    OR o.is_active IS NOT TRUE OR o.role<>'OWNER'
                    OR (r.status='APPROVED' AND o.auth_version<>r.approving_owner_auth_version));
            """);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<RecoveryOwnerContact>> OwnersAsync(CancellationToken ct)
    {
        await using var c = await database.OpenAsync(ct);
        await using var command = Command(c, null, """
            SELECT user_id, full_name, password_hash FROM public.users
            WHERE role='OWNER' AND account_status='ACTIVE' AND is_active IS TRUE ORDER BY full_name, user_id
            """);
        var contacts = new List<RecoveryOwnerContact>();
        await using var r = await command.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            if (passwords.IsUsableHash(r.GetString(2))) contacts.Add(new(r.GetInt32(0), r.GetString(1)));
        return contacts;
    }

    public async Task SubmitAsync(string username, int ownerId, CancellationToken ct)
    {
        await using var c = await database.OpenAsync(ct);
        await using var t = await c.BeginTransactionAsync(ct);
        await LockAsync(c, t, ct);
        int? userId;
        await using (var lookup = Command(c, t,
            "SELECT min(user_id) FROM public.users WHERE lower(username)=lower(@name) HAVING count(*)=1", ("name", username)))
            userId = await lookup.ExecuteScalarAsync(ct) as int?;
        var requester = await UserAsync(c, t, userId ?? 0, ct);
        var owner = await UserAsync(c, t, ownerId, ct);
        if (Eligible(requester) && Owner(owner) && requester!.UserId != ownerId)
        {
            await using var insert = Command(c, t, """
                INSERT INTO public.password_recovery_requests
                    (request_id,requester_user_id,selected_owner_id,expires_at,requester_auth_version)
                SELECT @id,@user,@owner,clock_timestamp()+interval '1 hour',@version
                WHERE NOT EXISTS (SELECT 1 FROM public.password_recovery_requests
                    WHERE requester_user_id=@user AND (status IN ('PENDING','APPROVED')
                        OR created_at > clock_timestamp()-interval '5 minutes'))
                ON CONFLICT DO NOTHING
                """, ("id", Guid.NewGuid()), ("user", requester.UserId), ("owner", ownerId),
                ("version", requester.AuthVersion));
            await insert.ExecuteNonQueryAsync(ct);
        }
        await t.CommitAsync(ct); // No existence-dependent public result or request identifier.
    }

    public async Task<RecoveryError> ResolvePendingAsync(int actorId, string username, bool deny, CancellationToken ct)
    {
        await using var c = await database.OpenAsync(ct);
        await using var t = await c.BeginTransactionAsync(ct);
        await LockAsync(c, t, ct);
        if (!Owner(await UserAsync(c, t, actorId, ct))) return RecoveryError.Forbidden;
        int? userId;
        await using (var lookup = Command(c, t,
            "SELECT min(user_id) FROM public.users WHERE lower(username)=lower(@name) HAVING count(*)=1", ("name", username)))
            userId = await lookup.ExecuteScalarAsync(ct) as int?;
        if (userId is null || userId == actorId || !Eligible(await UserAsync(c, t, userId.Value, ct)))
            return RecoveryError.Conflict;
        // Any active OWNER may resolve a verified person's misdirected pending request.
        // Never claim/deny APPROVED requests, disclose a code, or extend the request's lifetime.
        await using var update = Command(c, t, """
            UPDATE public.password_recovery_requests
            SET previous_owner_id=selected_owner_id,selected_owner_id=@actor,
                resolved_by_owner_id=@actor,resolved_at=clock_timestamp(),updated_at=clock_timestamp(),
                status=CASE WHEN @deny THEN 'DENIED' ELSE 'PENDING' END,
                processed_by_owner_id=CASE WHEN @deny THEN @actor ELSE NULL END,
                processed_at=CASE WHEN @deny THEN clock_timestamp() ELSE NULL END
            WHERE requester_user_id=@user AND status='PENDING' AND expires_at>clock_timestamp()
            """, ("user", userId.Value), ("actor", actorId), ("deny", deny));
        var changed = await update.ExecuteNonQueryAsync(ct) == 1;
        // Future audit integration point only. No audit module is implemented here.
        await t.CommitAsync(ct);
        return changed ? RecoveryError.None : RecoveryError.Conflict;
    }

    private const string Projection = """
        r.request_id,r.requester_user_id,u.full_name,u.username,u.role,r.status,
        r.created_at,r.updated_at,r.expires_at,r.processed_by_owner_id,r.processed_at,r.consumed_at
        """;
    private static RecoveryItem Read(NpgsqlDataReader r) => new(r.GetGuid(0), r.GetInt32(1), r.GetString(2),
        r.GetString(3), r.GetString(4), r.GetString(5), r.GetFieldValue<DateTimeOffset>(6),
        r.GetFieldValue<DateTimeOffset>(7), r.GetFieldValue<DateTimeOffset>(8),
        r.IsDBNull(9) ? null : r.GetInt32(9), r.IsDBNull(10) ? null : r.GetFieldValue<DateTimeOffset>(10),
        r.IsDBNull(11) ? null : r.GetFieldValue<DateTimeOffset>(11));

    public async Task<RecoveryResult<RecoveryPage>> InboxAsync(int actorId, string? status, int page, int pageSize, CancellationToken ct)
    {
        await using var c = await database.OpenAsync(ct);
        await using var t = await c.BeginTransactionAsync(ct);
        await LockAsync(c, t, ct);
        if (!Owner(await UserAsync(c, t, actorId, ct))) return new(Error: RecoveryError.Forbidden);
        const string where = " WHERE r.selected_owner_id=@actor AND (@status='' OR r.status=ANY(string_to_array(@status,',')))";
        await using var count = Command(c, t, "SELECT count(*)::int FROM public.password_recovery_requests r" + where,
            ("actor", actorId), ("status", status ?? ""));
        var total = (int)(await count.ExecuteScalarAsync(ct))!;
        var items = new List<RecoveryItem>();
        await using (var query = Command(c, t, "SELECT " + Projection +
            " FROM public.password_recovery_requests r JOIN public.users u ON u.user_id=r.requester_user_id" + where +
            " ORDER BY r.created_at DESC,r.request_id LIMIT @limit OFFSET @offset",
            ("actor", actorId), ("status", status ?? ""), ("limit", pageSize), ("offset", (page-1)*pageSize)))
        await using (var reader = await query.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) items.Add(Read(reader));
        await t.CommitAsync(ct);
        return new(new(items, total, page, pageSize));
    }

    public async Task<RecoveryResult<int>> PendingCountAsync(int actorId, CancellationToken ct)
    {
        var result = await InboxAsync(actorId, "PENDING", 1, 1, ct);
        return result.Error == RecoveryError.None ? new(result.Value!.Total) : new(Error: result.Error);
    }

    public async Task<RecoveryResult<RecoveryItem>> DetailsAsync(int actorId, Guid id, CancellationToken ct)
    {
        await using var c = await database.OpenAsync(ct);
        await using var t = await c.BeginTransactionAsync(ct);
        await LockAsync(c, t, ct);
        if (!Owner(await UserAsync(c, t, actorId, ct))) return new(Error: RecoveryError.Forbidden);
        RecoveryItem? item;
        await using (var query = Command(c, t, "SELECT " + Projection + """
             FROM public.password_recovery_requests r JOIN public.users u ON u.user_id=r.requester_user_id
             WHERE r.request_id=@id AND r.selected_owner_id=@actor AND r.requester_user_id<>@actor
            """, ("id", id), ("actor", actorId)))
        await using (var reader = await query.ExecuteReaderAsync(ct))
            item = await reader.ReadAsync(ct) ? Read(reader) : null;
        await t.CommitAsync(ct);
        return item is null ? new(Error: RecoveryError.NotFound) : new(item);
    }

    public async Task<RecoveryResult<DateTimeOffset>> ProcessAsync(int actorId, Guid id, byte[]? codeHash, CancellationToken ct)
    {
        await using var c = await database.OpenAsync(ct);
        await using var t = await c.BeginTransactionAsync(ct);
        await LockAsync(c, t, ct);
        var actor = await UserAsync(c, t, actorId, ct);
        if (!Owner(actor)) return new(Error: RecoveryError.Forbidden);
        int targetId;
        string status;
        await using (var query = Command(c, t, """
            SELECT requester_user_id,status FROM public.password_recovery_requests
            WHERE request_id=@id AND selected_owner_id=@actor AND requester_user_id<>@actor FOR UPDATE
            """, ("id", id), ("actor", actorId)))
        await using (var reader = await query.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) return new(Error: RecoveryError.NotFound);
            targetId = reader.GetInt32(0); status = reader.GetString(1);
        }
        if (status != "PENDING" || !Eligible(await UserAsync(c, t, targetId, ct)))
        {
            await t.CommitAsync(ct);
            return new(Error: RecoveryError.Conflict);
        }
        await using var update = Command(c, t, """
            UPDATE public.password_recovery_requests SET status=@status,processed_by_owner_id=@actor,
                processed_at=clock_timestamp(),updated_at=clock_timestamp(),
                code_hash=@hash,approving_owner_auth_version=@version,
                expires_at=CASE WHEN @status='APPROVED' THEN clock_timestamp()+interval '10 minutes' ELSE expires_at END
            WHERE request_id=@id AND status='PENDING' AND expires_at>clock_timestamp() RETURNING expires_at
            """, ("id", id), ("actor", actorId), ("status", codeHash is null ? "DENIED" : "APPROVED"),
            ("version", actor!.AuthVersion));
        update.Parameters.Add("hash", NpgsqlTypes.NpgsqlDbType.Bytea).Value = (object?)codeHash ?? DBNull.Value;
        if (await update.ExecuteScalarAsync(ct) is not DateTime expires) return new(Error: RecoveryError.Conflict);
        // Future audit integration point: actorId, id, decision; never include code/hash/password.
        await t.CommitAsync(ct);
        return new(new DateTimeOffset(expires));
    }

    public async Task<bool> CompleteAsync(byte[] codeHash, string passwordHash, CancellationToken ct)
    {
        await using var c = await database.OpenAsync(ct);
        await using var t = await c.BeginTransactionAsync(ct);
        await LockAsync(c, t, ct);
        Guid id;
        int userId, ownerId;
        await using (var query = Command(c, t, """
            SELECT request_id,requester_user_id,selected_owner_id FROM public.password_recovery_requests
            WHERE code_hash=@hash AND status='APPROVED' AND consumed_at IS NULL
                AND expires_at>clock_timestamp() FOR UPDATE
            """, ("hash", codeHash)))
        await using (var r = await query.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct)) { await r.DisposeAsync(); await t.CommitAsync(ct); return false; }
            id = r.GetGuid(0); userId = r.GetInt32(1); ownerId = r.GetInt32(2);
        }
        if (!Eligible(await UserAsync(c, t, userId, ct)) || !Owner(await UserAsync(c, t, ownerId, ct))) return false;
        await using var consume = Command(c, t, """
            UPDATE public.password_recovery_requests SET status='COMPLETED',code_hash=NULL,
                consumed_at=clock_timestamp(),updated_at=clock_timestamp()
            WHERE request_id=@id AND status='APPROVED' AND expires_at>clock_timestamp()
            """, ("id", id));
        if (await consume.ExecuteNonQueryAsync(ct) != 1) return false;
        await using var update = Command(c, t,
            "UPDATE public.users SET password_hash=@password,auth_version=auth_version+1 WHERE user_id=@user",
            ("password", passwordHash), ("user", userId));
        await update.ExecuteNonQueryAsync(ct);
        // Future audit integration point inside the transaction. No automatic sign-in or bootstrap changes.
        await t.CommitAsync(ct);
        return true;
    }
}
