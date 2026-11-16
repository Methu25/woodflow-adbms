using Npgsql;
using WoodFlow.Application.Interfaces;

namespace WoodFlow.Infrastructure.Repositories;

public sealed class PostgresHealthRepository(Database database) : IHealthRepository
{
    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken)
    {
        if (database.DataSource is null)
            return false;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await using var connection = await database.OpenAsync(timeout.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (NpgsqlException)
        {
            return false;
        }
    }
}
