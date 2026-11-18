using Npgsql;
using WoodFlow.Application.DTOs;
using WoodFlow.Domain;
using WoodFlow.Infrastructure;
using WoodFlow.Infrastructure.Repositories;
using WoodFlow.Infrastructure.Security;
using Xunit;

namespace WoodFlow.Tests;

public sealed class PostgresUserManagementTests
{
    // Explicitly opt-in only: destructive fixture is never allowed to use woodflow_db.
    [UserManagementPostgresFact, Trait("Category", "PostgreSQL")]
    public async Task MigrationPersistenceRevocationAndConcurrentOwnerProtection()
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("WOODFLOW_USER_TEST_CONNECTION")
            ?? throw new InvalidOperationException("Dedicated test connection required."));
        Assert.Equal("woodflow_users_tests", settings.Database);
        Assert.Contains(settings.Host, new[] { "localhost", "127.0.0.1", "::1" });
        using var database = new Database(settings.ConnectionString);
        await using var connection = await database.OpenAsync(default);
        async Task Execute(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
        async Task<object?> Scalar(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            return await command.ExecuteScalarAsync();
        }
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "database/migrations/003_user_account_status.sql"))) root = root.Parent;
        var migration = await File.ReadAllTextAsync(Path.Combine(root!.FullName, "database/migrations/003_user_account_status.sql"));
        // Require an empty, dedicated fixture rather than dropping any pre-existing tables.
        Assert.Equal(DBNull.Value, await Scalar("SELECT to_regclass('public.users')"));
        await Execute("""
            CREATE TABLE public.users (
                user_id SERIAL PRIMARY KEY, username VARCHAR(50) UNIQUE NOT NULL,
                password_hash VARCHAR(255) NOT NULL, full_name VARCHAR(100) NOT NULL,
                role VARCHAR(30) NOT NULL CHECK (role IN ('OWNER','ASSISTANT')),
                is_active BOOLEAN DEFAULT true, created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
            );
            """);
        try
        {
            await Execute(migration); await Execute(migration);
            var passwords = new PasswordService();
            var owner = new User(0, "first", "", "First Owner", Roles.Owner, true);
            await using (var seed = new NpgsqlCommand("INSERT INTO public.users (username,password_hash,full_name,role) VALUES ('first',@hash,'First Owner','OWNER')", connection))
            {
                seed.Parameters.AddWithValue("hash", passwords.Hash(owner, "Dedicated fixture password 42!"));
                await seed.ExecuteNonQueryAsync();
            }
            var repo = new UserManagementRepository(database, passwords);
            var created = await repo.CreateAsync(1, "Assistant", "assistant", passwords.Hash(owner, "Dedicated fixture password 42!"), Roles.Assistant, default);
            Assert.Equal(UserWriteError.None, created.Error);
            Assert.Equal(UserWriteError.DuplicateUsername, (await repo.CreateAsync(1, "Duplicate", "ASSISTANT", "unused", Roles.Assistant, default)).Error);
            ChangeUserRequest Change(ManagedUser u, string status, string? role = null) => new()
            { ExpectedRole = u.Role, ExpectedStatus = u.Status, ExpectedVersion = u.Version, Role = role ?? u.Role, Status = status, Confirmed = true };
            var suspended = await repo.ChangeAsync(1, created.User!.UserId, Change(created.User, "SUSPENDED"), default);
            Assert.Equal("SUSPENDED", suspended.User!.Status);
            var authRepo = new UserRepository(database);
            Assert.False((await authRepo.FindByIdAsync(created.User.UserId, default))!.IsActive);
            var restored = await repo.ChangeAsync(1, created.User.UserId, Change(suspended.User, "ACTIVE"), default);
            Assert.True(restored.User!.Version > suspended.User.Version);
            Assert.Equal(UserWriteError.Conflict, (await repo.ChangeAsync(1, created.User.UserId, Change(created.User, "DEACTIVATED"), default)).Error);
            var deactivated = await repo.ChangeAsync(1, created.User.UserId, Change(restored.User, "DEACTIVATED"), default);
            Assert.Equal(UserWriteError.Deactivated, (await repo.ChangeAsync(1, created.User.UserId, Change(deactivated.User!, "ACTIVE"), default)).Error);
            await Execute("INSERT INTO public.users (username,password_hash,full_name,role) VALUES ('seed-owner','placeholder','Seed Owner','OWNER')");
            var seedId = (int)(await Scalar("SELECT user_id FROM public.users WHERE username='seed-owner'"))!;
            var seedAccount = (await repo.FindAsync(seedId, default))!;
            Assert.Equal(UserWriteError.None, (await repo.ChangeAsync(1, seedId, Change(seedAccount, "ACTIVE", Roles.Assistant), default)).Error);
            Assert.Equal(Roles.Assistant, (await repo.FindAsync(seedId, default))!.Role);
            var second = await repo.CreateAsync(1, "Second Owner", "second", passwords.Hash(owner, "Dedicated fixture password 42!"), Roles.Owner, default);
            var first = (await repo.FindAsync(1, default))!;
            var attempts = await Task.WhenAll(
                repo.ChangeAsync(1, second.User!.UserId, Change(second.User, "ACTIVE", Roles.Assistant), default),
                repo.ChangeAsync(second.User.UserId, 1, Change(first, "ACTIVE", Roles.Assistant), default));
            Assert.Single(attempts, r => r.Error == UserWriteError.None);
            Assert.Single(attempts, r => r.Error == UserWriteError.Forbidden);
            Assert.Equal(1L, await Scalar("SELECT count(*) FROM public.users WHERE role='OWNER' AND account_status='ACTIVE'"));
        }
        finally
        {
            await Execute("DROP TABLE public.users; DROP FUNCTION public.sync_user_account_status()");
        }
    }
}

public sealed class UserManagementPostgresFactAttribute : FactAttribute
{
    public UserManagementPostgresFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("WOODFLOW_RUN_USER_DB_TESTS") != "1")
            Skip = "Requires explicit opt-in and a disposable local woodflow_users_tests database.";
    }
}
