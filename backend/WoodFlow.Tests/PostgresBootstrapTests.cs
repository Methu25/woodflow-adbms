using Npgsql;
using WoodFlow.Application.Services;
using WoodFlow.Domain;
using WoodFlow.Infrastructure;
using WoodFlow.Infrastructure.Repositories;
using WoodFlow.Infrastructure.Security;
using Xunit;

namespace WoodFlow.Tests;

// Never uses ConnectionStrings__WoodFlowDb. Explicit opt-in AND a dedicated local
// disposable database are required. These tests are excluded from routine verification.
public sealed class PostgresBootstrapTests
{
    [PostgresFact]
    [Trait("Category", "PostgreSQL")]
    public async Task MigrationsExpiryRotationSingleUseAndConcurrentInitialization()
    {
        var connectionString = Environment.GetEnvironmentVariable("WOODFLOW_TEST_CONNECTION")
            ?? throw new InvalidOperationException("Dedicated test connection required.");
        var settings = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Equal("woodflow_auth_tests", settings.Database);
        Assert.Contains(settings.Host, new[] { "localhost", "127.0.0.1", "::1" });

        using var database = new Database(connectionString);
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
        var root = FindRoot();
        var roleMigration = await File.ReadAllTextAsync(Path.Combine(root, "database/migrations/001_application_roles.sql"));
        var setupMigration = await File.ReadAllTextAsync(Path.Combine(root, "database/migrations/002_owner_setup.sql"));

        // Destructive fixture is confined to the explicitly named disposable test database.
        await Execute("DROP TABLE IF EXISTS public.owner_setup_state; DROP TABLE IF EXISTS public.users");
        try
        {
            await Execute("""
                CREATE TABLE public.users (
                    user_id SERIAL PRIMARY KEY, username VARCHAR(50) UNIQUE NOT NULL,
                    password_hash VARCHAR(255) NOT NULL, full_name VARCHAR(100) NOT NULL,
                    role VARCHAR(30) NOT NULL CHECK (role IN ('OWNER','CASHIER','WORKSHOP_SUPERVISOR')),
                    is_active BOOLEAN DEFAULT true
                );
                INSERT INTO public.users (username,password_hash,full_name,role) VALUES
                    ('seed-owner','hash_owner_123','Seed Owner','OWNER'),
                    ('seed-cashier','hash_cashier_123','Seed Cashier','CASHIER'),
                    ('seed-foreman','hash_foreman_123','Seed Foreman','WORKSHOP_SUPERVISOR');
                """);
            await Execute(roleMigration);
            await Execute(roleMigration);
            await Execute(setupMigration);
            await Execute(setupMigration);
            Assert.Equal(2L, await Scalar("SELECT count(*) FROM public.users WHERE role='ASSISTANT' AND user_id IN (2,3)"));
            Assert.Equal("hash_owner_123", await Scalar("SELECT password_hash FROM public.users WHERE user_id=1"));

            var passwords = new PasswordService();
            var repository = new SetupRepository(database, passwords);
            var service = new SetupService(repository, passwords);
            var first = await service.GenerateCodeAsync(default);
            Assert.NotNull(first);
            Assert.Equal(32, Assert.IsType<byte[]>(await Scalar("SELECT code_hash FROM public.owner_setup_state")).Length);
            var user = new User(0, "new-owner", "", "New Owner", Roles.Owner, true);
            var passwordHash = passwords.Hash(user, "Example secure password");
            var firstHash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(first));
            Assert.False(await repository.InitializeOwnerAsync("New Owner", "new-owner", passwordHash, new byte[32], default));
            await Execute("UPDATE public.owner_setup_state SET expires_at = clock_timestamp() - interval '1 second'");
            Assert.False(await repository.InitializeOwnerAsync("New Owner", "new-owner", passwordHash, firstHash, default));
            var second = await service.GenerateCodeAsync(default);
            Assert.NotNull(second);
            Assert.False(await repository.InitializeOwnerAsync("New Owner", "new-owner", passwordHash, firstHash, default));
            var secondHash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(second));
            Assert.False(await repository.InitializeOwnerAsync("New Owner", "seed-owner", passwordHash, secondHash, default));
            var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
                repository.InitializeOwnerAsync("New Owner", "new-owner-" + i, passwordHash, secondHash, default)));
            Assert.Equal(1, attempts.Count(success => success));
            Assert.Equal(4L, await Scalar("SELECT count(*) FROM public.users"));
            Assert.False(await repository.InitializeOwnerAsync("New Owner", "replay", passwordHash, secondHash, default));
            Assert.Null(await service.GenerateCodeAsync(default));
            await Execute("UPDATE public.users SET is_active=false WHERE username LIKE 'new-owner-%'");
            Assert.Null(await service.GenerateCodeAsync(default));
            await Execute("UPDATE public.users SET role='ASSISTANT' WHERE username LIKE 'new-owner-%'");
            Assert.Null(await service.GenerateCodeAsync(default)); // Durable latch, even with no real OWNER.
            Assert.Equal(DBNull.Value, await Scalar("SELECT code_hash FROM public.owner_setup_state"));
        }
        finally
        {
            await Execute("DROP TABLE IF EXISTS public.owner_setup_state; DROP TABLE IF EXISTS public.users");
        }
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "database/schema.sql")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root required.");
    }
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("WOODFLOW_RUN_DB_TESTS") != "1")
            Skip = "Requires explicit opt-in and a disposable local woodflow_auth_tests database.";
    }
}
