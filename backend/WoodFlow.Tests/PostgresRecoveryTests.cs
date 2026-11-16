using Npgsql;
using WoodFlow.Api.Security;
using WoodFlow.Application.DTOs;
using WoodFlow.Application.Services;
using WoodFlow.Domain;
using WoodFlow.Infrastructure;
using WoodFlow.Infrastructure.Repositories;
using WoodFlow.Infrastructure.Security;
using Xunit;

namespace WoodFlow.Tests;

public sealed class PostgresRecoveryTests
{
    // Opt-in integration test. NEVER points at woodflow_db or creates a database.
    [RecoveryPostgresFact, Trait("Category", "PostgreSQL")]
    public async Task PersistenceEligibilityExpirySingleUseAndConcurrentProcessing()
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("WOODFLOW_RECOVERY_TEST_CONNECTION")!);
        Assert.Equal("woodflow_recovery_tests", settings.Database);
        Assert.Contains(settings.Host, new[] { "localhost", "127.0.0.1", "::1" });
        using var database = new Database(settings.ConnectionString);
        await using var connection = await database.OpenAsync(default);
        async Task<object?> Sql(string sql, params (string, object)[] parameters)
        {
            await using var cmd = new NpgsqlCommand(sql, connection);
            foreach (var (key, value) in parameters) cmd.Parameters.AddWithValue(key, value);
            return await cmd.ExecuteScalarAsync();
        }
        Assert.InRange(int.Parse((string)(await Sql("SHOW server_version_num"))!), 160000, 169999);
        Assert.Equal(DBNull.Value, await Sql("SELECT to_regclass('public.users')"));
        Assert.Equal(DBNull.Value, await Sql("SELECT to_regclass('public.password_recovery_requests')"));
        Assert.Equal(DBNull.Value, await Sql("SELECT to_regclass('public.auth_sessions')"));
        await Sql("""
            CREATE TABLE public.users (user_id SERIAL PRIMARY KEY,username VARCHAR(50) UNIQUE NOT NULL,
                password_hash VARCHAR(255) NOT NULL,full_name VARCHAR(100) NOT NULL,
                role VARCHAR(30) NOT NULL CHECK(role IN ('OWNER','ASSISTANT')),
                is_active BOOLEAN DEFAULT true,created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP)
            """);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "database/migrations"))) root = root.Parent;
        try
        {
            foreach (var file in new[] { "003_user_account_status.sql", "006_password_recovery.sql", "006_password_recovery.sql",
                "007_session_revocation_and_recovery_resolution.sql", "007_session_revocation_and_recovery_resolution.sql" })
                await Sql(await File.ReadAllTextAsync(Path.Combine(root!.FullName, "database/migrations", file)));
            var passwords = new PasswordService();
            var hash = passwords.Hash(new(0, "", "", "", Roles.Assistant, true), "Initial fixture password 42!");
            await Sql("""
                INSERT INTO public.users(username,full_name,password_hash,role,account_status) VALUES
                ('owner1','Owner One',@hash,'OWNER','ACTIVE'),('owner2','Owner Two',@hash,'OWNER','ACTIVE'),
                ('assistant','Assistant',@hash,'ASSISTANT','ACTIVE'),('suspended','Suspended',@hash,'ASSISTANT','SUSPENDED'),
                ('deactivated','Deactivated',@hash,'ASSISTANT','DEACTIVATED'),('seed','Seed','placeholder','OWNER','ACTIVE')
                """, ("hash", hash));
            var repo = new RecoveryRepository(database, passwords);
            var service = new RecoveryService(repo, passwords);
            var authRepository = new UserRepository(database);
            var sessions = new AccountSessionService(new AuthSessionRepository(database));
            var owner = (await authRepository.FindByIdAsync(1, default))!;
            var session1 = (await sessions.StartAsync(owner, default))!;
            var session2 = (await sessions.StartAsync(owner, default))!;
            Assert.True(await sessions.IsActiveAsync(owner, session1.Handle, default));
            await sessions.RevokeAsync(owner.UserId, session1.Handle, default);
            Assert.False(await sessions.IsActiveAsync(owner, session1.Handle, default));
            Assert.True(await sessions.IsActiveAsync(owner, session2.Handle, default));
            Assert.True(await new AccountSessionService(new AuthSessionRepository(database)).IsActiveAsync(owner, session2.Handle, default));
            Assert.Equal(32, await Sql("SELECT max(octet_length(session_hash)) FROM public.auth_sessions"));
            await Sql("UPDATE public.auth_sessions SET created_at=clock_timestamp()-interval '1 hour',expires_at=clock_timestamp()-interval '1 second' WHERE session_hash=@hash",
                ("hash", RecoveryService.HashCode(session2.Handle)));
            Assert.False(await sessions.IsActiveAsync(owner, session2.Handle, default));

            // A verified OWNER can rescue a pending request pinned to another OWNER.
            await repo.SubmitAsync("assistant", 1, default);
            var pinned = Assert.Single((await repo.InboxAsync(1, "PENDING", 1, 10, default)).Value!.Items);
            await repo.SubmitAsync("assistant", 2, default); // Anonymous resubmission cannot redirect it.
            Assert.Equal(0, (await repo.PendingCountAsync(2, default)).Value);
            Assert.Equal(RecoveryError.Forbidden, await repo.ResolvePendingAsync(3, "assistant", false, default));
            Assert.Equal(RecoveryError.None, await repo.ResolvePendingAsync(2, "assistant", false, default));
            Assert.Equal(RecoveryError.NotFound, (await repo.DetailsAsync(1, pinned.RequestId, default)).Error);
            Assert.Equal(pinned.RequestId, (await repo.DetailsAsync(2, pinned.RequestId, default)).Value!.RequestId);
            Assert.Equal(1, await Sql("SELECT previous_owner_id FROM public.password_recovery_requests"));
            var rescueCode = (await service.ApproveAsync(2, pinned.RequestId, true, default)).Value!;
            Assert.Equal(RecoveryError.Conflict, await repo.ResolvePendingAsync(1, "assistant", true, default));
            Assert.Equal(RecoveryError.Conflict, await repo.ResolvePendingAsync(1, "assistant", false, default));
            await repo.SubmitAsync("assistant", 1, default);
            Assert.Equal("APPROVED", await Sql("SELECT status FROM public.password_recovery_requests"));
            Assert.Equal(2, await Sql("SELECT selected_owner_id FROM public.password_recovery_requests"));
            Assert.Equal(RecoveryService.HashCode(rescueCode.Code), (byte[])(await Sql("SELECT code_hash FROM public.password_recovery_requests"))!);
            await Sql("DELETE FROM public.password_recovery_requests");
            await repo.SubmitAsync("assistant", 1, default);
            Assert.Equal(RecoveryError.None, await repo.ResolvePendingAsync(2, "assistant", true, default));
            Assert.Equal("DENIED", await Sql("SELECT status FROM public.password_recovery_requests"));
            await Sql("DELETE FROM public.password_recovery_requests");
            await repo.SubmitAsync("assistant", 1, default);
            var racingRequest = Assert.Single((await repo.InboxAsync(1, "PENDING", 1, 10, default)).Value!.Items);
            var race = await Task.WhenAll(repo.ResolvePendingAsync(2, "assistant", false, default),
                Task.Run(async () => (await service.ApproveAsync(1, racingRequest.RequestId, true, default)).Error));
            Assert.Single(race, result => result == RecoveryError.None);
            await Sql("DELETE FROM public.password_recovery_requests");
            await repo.SubmitAsync("owner1", 2, default);
            Assert.Equal(RecoveryError.Conflict, await repo.ResolvePendingAsync(1, "owner1", false, default));
            await Sql("DELETE FROM public.password_recovery_requests");
            Assert.Equal(2, (await repo.OwnersAsync(default)).Count);
            foreach (var name in new[] { "missing", "suspended", "deactivated", "seed", "owner1" })
                await repo.SubmitAsync(name, 1, default); // owner1 cannot choose themselves.
            await repo.SubmitAsync("assistant", 3, default); // ASSISTANT cannot be selected.
            Assert.Equal(0L, await Sql("SELECT count(*) FROM public.password_recovery_requests"));
            await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => repo.SubmitAsync("assistant", 1, default)));
            Assert.Equal(1L, await Sql("SELECT count(*) FROM public.password_recovery_requests"));
            Assert.Equal(1, (await repo.PendingCountAsync(1, default)).Value);
            Assert.Equal(0, (await repo.PendingCountAsync(2, default)).Value);
            var item = Assert.Single((await repo.InboxAsync(1, null, 1, 10, default)).Value!.Items);
            Assert.Equal(RecoveryError.Forbidden, (await repo.DetailsAsync(3, item.RequestId, default)).Error);
            Assert.Equal(RecoveryError.NotFound, (await repo.DetailsAsync(2, item.RequestId, default)).Error);
            Assert.Equal(RecoveryError.NotFound, (await service.ApproveAsync(2, item.RequestId, true, default)).Error);
            var approvals = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => service.ApproveAsync(1, item.RequestId, true, default)));
            var code = Assert.Single(approvals, r => r.Error == RecoveryError.None).Value!;
            Assert.Equal(3, approvals.Count(r => r.Error == RecoveryError.Conflict));
            Assert.Equal(RecoveryService.HashCode(code.Code), (byte[])(await Sql("SELECT code_hash FROM public.password_recovery_requests"))!);
            Assert.InRange(code.ExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(9), TimeSpan.FromMinutes(11));
            CompleteRecoveryRequest Reset(string token) => new() { Code = token, NewPassword = "New fixture password 73!", ConfirmPassword = "New fixture password 73!" };
            var auth = new UserRepository(database);
            var before = (await auth.FindByIdAsync(3, default))!;
            var oldSession = (await sessions.StartAsync(before, default))!;
            Assert.False(await service.CompleteAsync(Reset(new string('0', 64)), default));
            var resets = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => service.CompleteAsync(Reset(code.Code), default)));
            Assert.Single(resets, r => r);
            Assert.False(await service.CompleteAsync(Reset(code.Code), default));
            var after = (await auth.FindByIdAsync(3, default))!;
            Assert.True(passwords.Verify(after, "New fixture password 73!", out _));
            Assert.Equal(before.AuthVersion + 1, after.AuthVersion);
            Assert.NotEqual(AccountPrincipal.Stamp(before), AccountPrincipal.Stamp(after));
            Assert.False(await sessions.IsActiveAsync(after, oldSession.Handle, default));
            Assert.Equal("COMPLETED", await Sql("SELECT status FROM public.password_recovery_requests"));
            Assert.Equal(DBNull.Value, await Sql("SELECT code_hash FROM public.password_recovery_requests"));
            Assert.Equal(true, await Sql("SELECT consumed_at IS NOT NULL FROM public.password_recovery_requests"));

            // OWNER-to-OWNER; own request remains inaccessible even though requester has OWNER role.
            await repo.SubmitAsync("owner1", 2, default);
            item = Assert.Single((await repo.InboxAsync(2, "PENDING", 1, 10, default)).Value!.Items);
            Assert.Equal(RecoveryError.NotFound, (await service.ApproveAsync(1, item.RequestId, true, default)).Error);
            var ownerCode = (await service.ApproveAsync(2, item.RequestId, true, default)).Value!;
            await Sql("UPDATE public.password_recovery_requests SET expires_at=clock_timestamp()-interval '1 second' WHERE request_id=@id", ("id", item.RequestId));
            Assert.False(await service.CompleteAsync(Reset(ownerCode.Code), default));
            Assert.Equal("EXPIRED", (await repo.DetailsAsync(2, item.RequestId, default)).Value!.Status);

            // Denial and approval cannot both win; denial produces no reset code.
            await Sql("DELETE FROM public.password_recovery_requests");
            await repo.SubmitAsync("assistant", 1, default);
            item = Assert.Single((await repo.InboxAsync(1, null, 1, 10, default)).Value!.Items);
            var decisions = await Task.WhenAll(repo.ProcessAsync(1, item.RequestId, null, default),
                repo.ProcessAsync(1, item.RequestId, RecoveryService.HashCode(new string('B', 64)), default));
            Assert.Single(decisions, r => r.Error == RecoveryError.None);

            foreach (var change in new[] { "suspend-requester", "deactivate-requester", "demote-approver", "expire-pending" })
            {
                await Sql("DELETE FROM public.password_recovery_requests");
                // Use a fresh active assistant for each scenario; never restore deactivated accounts.
                var id = (int)(await Sql("INSERT INTO public.users(username,full_name,password_hash,role) VALUES (@name,'Fixture',@hash,'ASSISTANT') RETURNING user_id",
                    ("name", change), ("hash", hash)))!;
                await repo.SubmitAsync(change, 1, default);
                item = Assert.Single((await repo.InboxAsync(1, "PENDING", 1, 10, default)).Value!.Items);
                if (change == "expire-pending")
                {
                    await Sql("UPDATE public.password_recovery_requests SET expires_at=clock_timestamp()-interval '1 second'");
                    Assert.Equal(RecoveryError.Conflict, (await service.ApproveAsync(1, item.RequestId, true, default)).Error);
                    continue;
                }
                code = (await service.ApproveAsync(1, item.RequestId, true, default)).Value!;
                if (change == "demote-approver") await Sql("UPDATE public.users SET role='ASSISTANT' WHERE user_id=1");
                else await Sql("UPDATE public.users SET account_status=@status WHERE user_id=@id",
                    ("id", id), ("status", change == "suspend-requester" ? "SUSPENDED" : "DEACTIVATED"));
                Assert.False(await service.CompleteAsync(Reset(code.Code), default));
                if (change == "demote-approver") await Sql("UPDATE public.users SET role='OWNER' WHERE user_id=1");
            }
        }
        finally
        {
            await Sql("DROP TABLE IF EXISTS public.auth_sessions; DROP TABLE IF EXISTS public.password_recovery_requests; DROP TABLE public.users; DROP FUNCTION IF EXISTS public.sync_user_account_status()");
        }
    }
}

public sealed class RecoveryPostgresFactAttribute : FactAttribute
{
    public RecoveryPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WOODFLOW_RECOVERY_TEST_CONNECTION")))
            Skip = "Requires explicit local woodflow_recovery_tests connection; never run against woodflow_db.";
    }
}
