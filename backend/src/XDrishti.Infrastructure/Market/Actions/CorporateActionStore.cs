using Microsoft.EntityFrameworkCore;
using Npgsql;
using XDrishti.Application.Abstractions.Market.Actions;
using XDrishti.Domain.Market.Actions;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Market.Actions;

/// <summary>The corporate action rows (<c>market.corporate_actions</c>, ADR 0017 §5). The runtime role may insert and update them, never delete.</summary>
internal sealed class CorporateActionStore(XDrishtiDbContext db, NpgsqlDataSource dataSource) : ICorporateActionStore
{
    private const string InsertSuggestion =
        """
        INSERT INTO market.corporate_actions (id, instrument_id, ex_date, kind, numerator, denominator, factor, status, source, note, created_at, detected_ex_date)
        VALUES (@id, @instrument, @exDate, @kind, @numerator, @denominator, @factor, 'suggested', 'detected', @note, @createdAt, @exDate)
        ON CONFLICT (instrument_id, ex_date, kind) DO NOTHING
        """;

    public async ValueTask<CorporateAction?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await db.CorporateActions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async ValueTask<bool> ExistsAsync(Guid instrumentId, DateOnly exDate, CorporateActionKind kind, Guid? except, CancellationToken cancellationToken) =>
        await db.CorporateActions.AsNoTracking().AnyAsync(x => x.InstrumentId == instrumentId && x.ExDate == exDate && x.Kind == kind && (except == null || x.Id != except), cancellationToken);

    public async ValueTask<IReadOnlyList<CorporateAction>> ListNotIgnoredAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);
        var ids = instrumentIds.ToArray();
        return await db.CorporateActions.Where(x => ids.Contains(x.InstrumentId) && x.Status != CorporateActionStatus.Ignored).ToListAsync(cancellationToken);
    }

    public void Add(CorporateAction action) => db.CorporateActions.Add(action);

    public async ValueTask<int> AddSuggestionsAsync(IReadOnlyCollection<CorporateAction> suggestions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(suggestions);
        var stored = 0;
        foreach (var suggestion in suggestions)
        {
            await using var command = dataSource.CreateCommand(InsertSuggestion);
            command.Parameters.AddWithValue("id", suggestion.Id);
            command.Parameters.AddWithValue("instrument", suggestion.InstrumentId);
            command.Parameters.AddWithValue("exDate", suggestion.ExDate);
            command.Parameters.AddWithValue("kind", suggestion.Kind.ToCode());
            command.Parameters.AddWithValue("numerator", suggestion.Numerator);
            command.Parameters.AddWithValue("denominator", suggestion.Denominator);
            command.Parameters.AddWithValue("factor", suggestion.Factor);
            command.Parameters.AddWithValue("note", NpgsqlTypes.NpgsqlDbType.Text, (object?)suggestion.Note ?? DBNull.Value);
            command.Parameters.AddWithValue("createdAt", suggestion.CreatedAt);
            stored += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return stored;
    }
}
