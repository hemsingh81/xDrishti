using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Domain.Learning;

namespace XDrishti.Infrastructure.Paper;

/// <summary>The read of a model's graded shadow scores behind <see cref="IShadowScoreReader"/> (CF-10e part 10e-1): select only; candidates that count for edge and are graded.</summary>
internal sealed class ShadowScoreReader(NpgsqlDataSource dataSource) : IShadowScoreReader
{
    public async ValueTask<IReadOnlyList<ScoredOutcome>> OutcomesAsync(long modelId, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("""
            SELECT e.session_date, e.trade_key, s.p_win, g.r_sim
            FROM paper.shadow_scores s
            JOIN paper.shadow_entries e ON e.id = s.entry_id
            JOIN paper.shadow_grades g ON g.entry_id = e.id AND g.status = 'graded'
            WHERE s.model_id = @model AND e.counts_for_edge
            ORDER BY e.session_date, e.trade_key
            """);
        select.Parameters.AddWithValue("model", NpgsqlDbType.Bigint, modelId);
        var result = new List<ScoredOutcome>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ScoredOutcome(DateOnly.FromDateTime(reader.GetDateTime(0)), reader.GetString(1), reader.GetDouble(2), reader.GetDouble(3)));
        }

        return result;
    }
}
