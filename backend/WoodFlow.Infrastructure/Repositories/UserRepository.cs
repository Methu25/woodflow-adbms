using Npgsql;
using WoodFlow.Application.Interfaces;
using WoodFlow.Domain;

namespace WoodFlow.Infrastructure.Repositories;

public sealed class UserRepository(Database database) : IUserRepository
{
    private async Task<User?> FindAsync(string predicate, object value, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT user_id, username, password_hash, full_name, role, (COALESCE(is_active, false) AND account_status = 'ACTIVE'), auth_version FROM users WHERE "
            + predicate, connection);
        command.Parameters.AddWithValue("value", value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new User(reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetBoolean(5), reader.GetInt32(6))
            : null;
    }

    public Task<User?> FindByUsernameAsync(string username, CancellationToken cancellationToken) =>
        FindAsync("username = @value", username, cancellationToken);

    public Task<User?> FindByIdAsync(int userId, CancellationToken cancellationToken) =>
        FindAsync("user_id = @value", userId, cancellationToken);

    public async Task UpdateHashAsync(int userId, string oldHash, string newHash, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "UPDATE users SET password_hash = @new WHERE user_id = @id AND password_hash = @old", connection);
        command.Parameters.AddWithValue("id", userId);
        command.Parameters.AddWithValue("old", oldHash);
        command.Parameters.AddWithValue("new", newHash);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
