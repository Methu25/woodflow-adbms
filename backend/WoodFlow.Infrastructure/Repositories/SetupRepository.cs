using System.Security.Cryptography;
using Npgsql;
using WoodFlow.Application.Interfaces;
using WoodFlow.Domain;

namespace WoodFlow.Infrastructure.Repositories;

public sealed class SetupRepository(Database database, IPasswordService passwords) : ISetupRepository
{
    // All bootstrap operations acquire the same transaction-scoped lock before reading.
    private static async Task LockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(815043271); " +
            "INSERT INTO owner_setup_state (id) VALUES (1) ON CONFLICT (id) DO NOTHING",
            connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<bool> SealExistingOwnerAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        int? ownerId = null;
        await using (var command = new NpgsqlCommand(
            "SELECT user_id, password_hash FROM users WHERE role = @role", connection, transaction))
        {
            command.Parameters.AddWithValue("role", Roles.Owner);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                if (passwords.IsUsableHash(reader.GetString(1)))
                {
                    ownerId = reader.GetInt32(0);
                    break; // Deactivation must never reopen first-owner setup.
                }
        }
        if (ownerId is null)
            return false;
        await using var seal = new NpgsqlCommand("""
            UPDATE owner_setup_state SET code_hash = NULL, expires_at = NULL,
                consumed_at = clock_timestamp(), initialized_at = clock_timestamp(),
                initialized_owner_id = @owner
            WHERE id = 1 AND initialized_at IS NULL
            """, connection, transaction);
        seal.Parameters.AddWithValue("owner", ownerId.Value);
        await seal.ExecuteNonQueryAsync(cancellationToken);
        return true;
    }

    public async Task<bool> IssueCodeAsync(byte[] codeHash, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockAsync(connection, transaction, cancellationToken);
        if (await SealExistingOwnerAsync(connection, transaction, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        await using var command = new NpgsqlCommand("""
            UPDATE owner_setup_state
            SET code_hash = @hash, expires_at = clock_timestamp() + interval '10 minutes', consumed_at = NULL
            WHERE id = 1 AND initialized_at IS NULL
            """, connection, transaction);
        command.Parameters.AddWithValue("hash", codeHash);
        var issued = await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        await transaction.CommitAsync(cancellationToken);
        return issued;
    }

    public async Task<bool> InitializeOwnerAsync(string fullName, string username, string passwordHash,
        byte[] codeHash, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockAsync(connection, transaction, cancellationToken);
        if (await SealExistingOwnerAsync(connection, transaction, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        await using (var check = new NpgsqlCommand("""
            SELECT code_hash FROM owner_setup_state
            WHERE id = 1 AND initialized_at IS NULL AND consumed_at IS NULL
                AND expires_at > clock_timestamp() FOR UPDATE
            """, connection, transaction))
        {
            var storedHash = await check.ExecuteScalarAsync(cancellationToken) as byte[];
            if (storedHash is null || !CryptographicOperations.FixedTimeEquals(storedHash, codeHash))
                return false;
        }

        await using var insert = new NpgsqlCommand("""
            INSERT INTO users (username, password_hash, full_name, role, is_active)
            VALUES (@username, @password, @name, @role, true)
            ON CONFLICT (username) DO NOTHING RETURNING user_id
            """, connection, transaction);
        insert.Parameters.AddWithValue("username", username);
        insert.Parameters.AddWithValue("password", passwordHash);
        insert.Parameters.AddWithValue("name", fullName);
        insert.Parameters.AddWithValue("role", Roles.Owner);
        var userId = await insert.ExecuteScalarAsync(cancellationToken);
        if (userId is null)
            return false; // Never overwrite an existing seed or real account.

        await using var consume = new NpgsqlCommand("""
            UPDATE owner_setup_state
            SET code_hash = NULL, expires_at = NULL, consumed_at = clock_timestamp(),
                initialized_at = clock_timestamp(), initialized_owner_id = @id
            WHERE id = 1
            """, connection, transaction);
        consume.Parameters.AddWithValue("id", userId);
        await consume.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
