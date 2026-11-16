using Npgsql;
using WoodFlow.Application.DTOs;
using WoodFlow.Infrastructure;
using WoodFlow.Infrastructure.Repositories;
using WoodFlow.Infrastructure.Security;
using WoodFlow.Domain;
using Xunit;

namespace WoodFlow.Tests;

public sealed class PostgresPermissionTests
{
    [PermissionsPostgresFact, Trait("Category", "PostgreSQL")]
    public async Task SettingsPersistMigrationIsRerunnableAndConcurrentWritesConflict()
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("WOODFLOW_PERMISSION_TEST_CONNECTION")
            ?? throw new InvalidOperationException("Dedicated local test connection required."));
        Assert.Equal("woodflow_permissions_tests", settings.Database);
        Assert.Contains(settings.Host, new[] { "localhost", "127.0.0.1", "::1" });
        using var database = new Database(settings.ConnectionString);
        await using var connection = await database.OpenAsync(default);
        async Task<object?> Scalar(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection); return await command.ExecuteScalarAsync();
        }
        async Task Execute(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection); await command.ExecuteNonQueryAsync();
        }
        Assert.Equal(DBNull.Value, await Scalar("SELECT to_regclass('public.users')"));
        Assert.Equal(DBNull.Value, await Scalar("SELECT to_regclass('public.assistant_permission_settings')"));
        Assert.Equal(DBNull.Value, await Scalar("SELECT to_regclass('public.assistant_user_permissions')"));
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "database/migrations/004_assistant_permissions.sql"))) root = root.Parent;
        var migration = await File.ReadAllTextAsync(Path.Combine(root!.FullName, "database/migrations/004_assistant_permissions.sql"));
        var individualMigration = await File.ReadAllTextAsync(Path.Combine(root.FullName, "database/migrations/005_assistant_user_permissions.sql"));
        await Execute("CREATE TABLE public.users (user_id INT PRIMARY KEY, password_hash VARCHAR(255) NOT NULL, role VARCHAR(30) NOT NULL, account_status VARCHAR(20) NOT NULL, is_active BOOLEAN NOT NULL, full_name VARCHAR(100) NOT NULL DEFAULT 'Fixture User', username VARCHAR(50) NOT NULL DEFAULT 'fixture', created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP, auth_version INT NOT NULL DEFAULT 0)");
        try
        {
            await Execute(migration); await Execute(migration);
            await Execute(individualMigration); await Execute(individualMigration);
            var passwords = new PasswordService();
            var owner = new User(1, "fixture", "", "Fixture Owner", Roles.Owner, true);
            await using (var insert = new NpgsqlCommand("INSERT INTO public.users (user_id,password_hash,role,account_status,is_active) VALUES (1,@hash,'OWNER','ACTIVE',true),(2,@hash,'ASSISTANT','ACTIVE',true),(3,@hash,'ASSISTANT','ACTIVE',true)", connection))
            {
                insert.Parameters.AddWithValue("hash", passwords.Hash(owner, "Dedicated fixture password 42!"));
                await insert.ExecuteNonQueryAsync();
            }
            var repo = new PermissionRepository(database, passwords);
            Assert.Equal(new[] { "dashboard.view" }, (await repo.GetAsync(default)).GrantedKeys);
            var writes = await Task.WhenAll(
                repo.SaveAsync(1, ["sales.view"], 0, default),
                repo.SaveAsync(1, ["reports.view"], 0, default));
            Assert.Single(writes, r => r.Error == PermissionSaveError.None);
            Assert.Single(writes, r => r.Error == PermissionSaveError.Conflict);
            var saved = writes.Single(r => r.Error == PermissionSaveError.None).Settings!;
            using var separateDatabase = new Database(settings.ConnectionString);
            var persisted = await new PermissionRepository(separateDatabase, passwords).GetAsync(default);
            Assert.Equal(saved.GrantedKeys, persisted.GrantedKeys); Assert.Equal(1, persisted.Version);
            await Execute(migration);
            Assert.Equal(saved.GrantedKeys, (await repo.GetAsync(default)).GrantedKeys);
            SaveUserPermissionsRequest Individual(long version, bool defaults = false, string key = "dashboard.view") => new()
            { GrantedKeys = defaults ? [] : [key], ExpectedVersion = version, ExpectedRoleVersion = 1, ExpectedAccountVersion = 0, UseRoleDefaults = defaults, Confirmed = true };
            Assert.True((await repo.GetUserAsync(2, default))!.InheritsRole);
            var individualWrites = await Task.WhenAll(repo.SaveUserAsync(1, 2, Individual(0), default), repo.SaveUserAsync(1, 2, Individual(0, key: "reports.view"), default));
            Assert.Single(individualWrites, r => r.Error == PermissionSaveError.None);
            Assert.Single(individualWrites, r => r.Error == PermissionSaveError.Conflict);
            Assert.Equal(individualWrites.Single(r => r.Error == PermissionSaveError.None).Settings!.GrantedKeys,
                (await repo.GetEffectiveAsync(2, default)).GrantedKeys);
            Assert.Equal(saved.GrantedKeys, (await repo.GetEffectiveAsync(3, default)).GrantedKeys);
            var reset = await repo.SaveUserAsync(1, 2, Individual(1, true), default);
            Assert.True(reset.Settings!.InheritsRole); Assert.Equal(2, reset.Settings.Version);
            await repo.SaveUserAsync(1, 2, Individual(2), default);
            await Execute(individualMigration); // Re-running never resets overrides.
            Assert.False((await repo.GetUserAsync(2, default))!.InheritsRole);
            await Execute("UPDATE public.users SET role='OWNER' WHERE user_id=2; UPDATE public.users SET role='ASSISTANT' WHERE user_id=2");
            var afterRoleCycle = (await repo.GetUserAsync(2, default))!;
            Assert.True(afterRoleCycle.InheritsRole); Assert.Equal(5, afterRoleCycle.Version);
            await Execute("UPDATE public.users SET role='ASSISTANT' WHERE user_id=1");
            Assert.Equal(PermissionSaveError.Forbidden, (await repo.SaveAsync(1, [], 1, default)).Error);
            Assert.Equal(saved.GrantedKeys, (await repo.GetAsync(default)).GrantedKeys);
        }
        finally { await Execute("DROP TABLE IF EXISTS public.assistant_user_permissions; DROP TABLE IF EXISTS public.assistant_permission_settings; DROP TABLE public.users; DROP FUNCTION IF EXISTS public.clear_assistant_permission_override()"); }
    }
}

public sealed class PermissionsPostgresFactAttribute : FactAttribute
{
    public PermissionsPostgresFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("WOODFLOW_RUN_PERMISSION_DB_TESTS") != "1")
            Skip = "Requires explicit opt-in and a dedicated empty local woodflow_permissions_tests database.";
    }
}
