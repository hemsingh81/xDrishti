using Npgsql;
using XDrishti.Application.Abstractions.Market.Baskets;

namespace XDrishti.Infrastructure.Market.SystemBaskets;

/// <summary>A session-level advisory lock on its own connection: released when the handle is disposed or the connection drops (a crash frees it).</summary>
internal sealed class BasketRunLock(NpgsqlDataSource dataSource) : IBasketRunLock
{
    /// <summary>"xdrishti" + "baskets".</summary>
    internal const long Key = 0x7864_6261_736B_7473;

    public async ValueTask<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
            command.Parameters.AddWithValue("key", Key);
            if (await command.ExecuteScalarAsync(cancellationToken) is true)
            {
                return new Handle(connection);
            }
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        await connection.DisposeAsync();
        return null;
    }

    private sealed class Handle(NpgsqlConnection connection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
                command.Parameters.AddWithValue("key", Key);
                await command.ExecuteScalarAsync(CancellationToken.None);
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
