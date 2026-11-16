using Npgsql;

namespace WoodFlow.Infrastructure;

public sealed class Database : IDisposable
{
    public NpgsqlDataSource? DataSource { get; }

    public Database(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return;
        try
        {
            var settings = new NpgsqlConnectionStringBuilder(connectionString)
            {
                Timeout = 3,
                CommandTimeout = 5,
                IncludeErrorDetail = false,
                LogParameters = false
            };
            DataSource = NpgsqlDataSource.Create(settings.ConnectionString);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("Database configuration is invalid.");
        }
    }

    public ValueTask<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken) =>
        (DataSource ?? throw new InvalidOperationException("Database configuration is required."))
        .OpenConnectionAsync(cancellationToken);

    public void Dispose() => DataSource?.Dispose();
}
