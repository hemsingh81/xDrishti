using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.OptionQuality;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Options.Quality;

namespace XDrishti.Application.OptionQuality;

/// <summary>
/// <c>GET /api/options/coverage</c> (spec P14-04 criterion 8): the newest stored verdict of every check per session, the usable flags and the cumulative usable chain sessions. Reads only the result rows and the master list; returns aggregates, never a price,
/// IV or Greek. An unknown underlying, a range over 400 days or an inverted range is <c>options.invalid_query</c>; nothing recorded is an empty list.
/// </summary>
internal sealed class GetOptionCoverageHandler(IOptionReference reference, IOptionQualityStore store, TimeProvider clock) : IQueryHandler<GetOptionCoverageQuery, Result<OptionCoverageView>>
{
    public const int MaxRangeDays = 400;

    public const int DefaultDays = 30;

    public const int MaxRollingSessions = 5000;

    public async ValueTask<Result<OptionCoverageView>> HandleAsync(GetOptionCoverageQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var symbol = Normalize(query.Underlying);
        if (symbol is null)
        {
            return Result.Failure<OptionCoverageView>(OptionQualityErrors.InvalidQuery("underlying is required, a symbol such as NSE:NIFTY."));
        }

        var to = query.To ?? IndiaTime.Today(clock.GetUtcNow());
        var from = query.From ?? to.AddDays(1 - DefaultDays);
        if (from > to)
        {
            return Result.Failure<OptionCoverageView>(OptionQualityErrors.InvalidQuery("from must not be after to."));
        }

        if (to.DayNumber - from.DayNumber + 1 > MaxRangeDays)
        {
            return Result.Failure<OptionCoverageView>(OptionQualityErrors.InvalidQuery($"The range is at most {MaxRangeDays} days."));
        }

        var id = await reference.FindUnderlyingAsync(symbol, cancellationToken);
        if (id is null)
        {
            return Result.Failure<OptionCoverageView>(OptionQualityErrors.InvalidQuery("Unknown underlying."));
        }

        var chain = await store.ChainResultsAsync(id.Value, to, cancellationToken);
        var rolling = await store.RollingResultsAsync(id.Value, from, to, MaxRollingSessions + 1, cancellationToken);
        var chainSessions = chain.GroupBy(r => r.SessionDate).OrderBy(g => g.Key).Select(g => Session(g.Key, QualityCodes.Chain, null, [.. g])).ToList();
        var rollingGroups = rolling.GroupBy(r => (r.SessionDate, r.FetchKey)).OrderBy(g => g.Key.SessionDate).ThenBy(g => g.Key.FetchKey, StringComparer.Ordinal).ToList();
        var truncated = rollingGroups.Count > MaxRollingSessions;
        var rollingSessions = rollingGroups.Take(MaxRollingSessions).Select(g => Session(g.Key.SessionDate, QualityCodes.Rolling, g.Key.FetchKey, [.. g])).ToList();
        var listed = chainSessions.Where(s => s.Date >= from).Concat(rollingSessions).OrderBy(s => s.Date).ThenBy(s => s.Source, StringComparer.Ordinal).ThenBy(s => s.FetchKey, StringComparer.Ordinal).ToList();
        return Result.Success(new OptionCoverageView(symbol, listed, chainSessions.Count(s => s.Usable), truncated));
    }

    private static string? Normalize(string? underlying)
    {
        var text = underlying?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(text) || text.Length > 60)
        {
            return null;
        }

        return text.Contains(':', StringComparison.Ordinal) ? text : "NSE:" + text;
    }

    private static OptionCoverageSessionView Session(DateOnly date, string source, string? fetchKey, IReadOnlyList<OptionQualityRow> rows)
    {
        var checks = rows.Select(r => r.Check).OrderBy(c => c.Code, StringComparer.Ordinal).ToList();
        return new OptionCoverageSessionView(
            date,
            source,
            fetchKey,
            [.. checks.Select(c => new OptionCoverageCheckView(c.Code, c.Status.Code(), c.Value, c.Threshold, c.Reason))],
            SessionUsability.IsUsable(source, checks));
    }
}
