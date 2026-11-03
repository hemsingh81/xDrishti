using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.OptionExpression;
using XDrishti.Domain.Options.Remark;

namespace XDrishti.Infrastructure.OptionExpression;

/// <summary>The distinct expiry dates and kinds of one underlying's rows of <c>options.contracts</c> in a date range (P14-08, ADR 0197): bounded by the underlying and the range, read only. A row with no kind is left out.</summary>
internal sealed class ContractExpiryReader(NpgsqlDataSource dataSource) : IContractExpiryReader
{
    public async ValueTask<IReadOnlyList<ContractExpiry>> ExpiriesAsync(Guid underlyingId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT DISTINCT expiry, expiry_kind FROM options.contracts WHERE underlying_id = @u AND expiry >= @from AND expiry <= @to AND expiry_kind IS NOT NULL ORDER BY expiry, expiry_kind");
        command.Parameters.AddWithValue("u", underlyingId);
        command.Parameters.AddWithValue("from", NpgsqlDbType.Date, from);
        command.Parameters.AddWithValue("to", NpgsqlDbType.Date, to);
        var list = new List<ContractExpiry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ContractExpiry(reader.GetFieldValue<DateOnly>(0), reader.GetString(1) == "week" ? 'W' : 'M'));
        }

        return list;
    }
}
