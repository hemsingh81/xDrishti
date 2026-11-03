using System.Globalization;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.OptionCosts;
using XDrishti.Application.Abstractions.OptionQuality;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Options;
using XDrishti.Domain.Options.Costs;
using XDrishti.Domain.Options.Quality;

namespace XDrishti.Application.OptionCosts;

/// <summary>
/// <c>xd options spread-model build</c> (spec P14-07 criterion 3, ADR 0194): for an underlying with at least <c>MinUsableSessions</c> usable chain sessions (coverage <c>pass</c>, parity <c>pass</c> or <c>warn</c>, ADR 0178), reads those sessions' quotes one session at a
/// time, takes the median half-spread per bucket and stores the model under the SHA-256 of its inputs. A session that is not usable is never read. The same inputs give the same id, which is looked up before any quote is read, so a rebuild reads and writes nothing.
/// Fewer usable sessions is not a failure: no model, with the reason <c>sessions n of m</c>. Nothing here is read by a decision, a plan or an order.
/// </summary>
internal sealed class BuildSpreadModelHandler(
    IOptionReference reference,
    IOptionArchiveReader archive,
    IOptionQualityStore quality,
    IChainSpreadReader quotes,
    ISpreadModelStore store,
    ISpreadModelSettings settings,
    TimeProvider time) : ICommandHandler<BuildSpreadModelCommand, SpreadModelBuildSummary>
{
    /// <summary>How far back the underlyings that were recorded are looked for when the verb names none.</summary>
    private const int RecentDays = 400;

    public const string UnknownUnderlyingCode = "options.unknown_underlying";

    public async ValueTask<Result<SpreadModelBuildSummary>> HandleAsync(BuildSpreadModelCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var today = IndiaTime.Today(time.GetUtcNow());
        var targets = new List<(Guid Id, string Symbol)>();
        if (command.Underlying is not null)
        {
            var symbol = UnderlyingSymbol.Normalize(command.Underlying);
            if (symbol is null || await reference.FindUnderlyingAsync(symbol, cancellationToken) is not { } id)
            {
                return Result.Failure<SpreadModelBuildSummary>(Error.NotFound(UnknownUnderlyingCode, "No instrument has that canonical symbol."));
            }

            targets.Add((id, symbol));
        }
        else
        {
            var ids = await archive.RecentChainUnderlyingsAsync(today.AddDays(-RecentDays), cancellationToken);
            var symbols = await reference.SymbolsAsync(ids, cancellationToken);
            targets.AddRange(ids.Where(symbols.ContainsKey).Select(i => (i, symbols[i])).OrderBy(t => t.Item2, StringComparer.Ordinal));
        }

        if (targets.Count == 0)
        {
            return Result.Success(new SpreadModelBuildSummary([], "no underlying has chain sessions recorded"));
        }

        var lines = new List<SpreadModelBuildLine>();
        foreach (var (id, name) in targets)
        {
            lines.Add(await BuildOneAsync(id, name, today, cancellationToken));
        }

        return Result.Success(new SpreadModelBuildSummary(lines, null));
    }

    private async ValueTask<SpreadModelBuildLine> BuildOneAsync(Guid underlyingId, string symbol, DateOnly today, CancellationToken cancellationToken)
    {
        var rows = await quality.ChainResultsAsync(underlyingId, today, cancellationToken);
        var usable = rows.Where(r => string.Equals(r.Source, QualityCodes.Chain, StringComparison.Ordinal))
            .GroupBy(r => r.SessionDate)
            .Where(g => SessionUsability.IsUsable(QualityCodes.Chain, [.. g.Select(r => r.Check)]))
            .Select(g => g.Key).Order().ToList();
        if (usable.Count < settings.MinUsableSessions)
        {
            return new SpreadModelBuildLine(symbol, "not_built", string.Create(CultureInfo.InvariantCulture, $"sessions {usable.Count} of {settings.MinUsableSessions}"), usable.Count, null, 0);
        }

        var modelId = SpreadModelId.Of(underlyingId, usable, settings.MinUsableSessions, settings.MinQuotesPerBucket);
        if (await store.ExistsAsync(modelId, cancellationToken))
        {
            return new SpreadModelBuildLine(symbol, "already_built", "the same sessions and settings were built already; nothing read, nothing written", usable.Count, modelId, 0);
        }

        var accumulator = new HalfSpreadAccumulator();
        foreach (var session in usable)
        {
            cancellationToken.ThrowIfCancellationRequested();
            accumulator.AddSession(session, await quotes.QuotesAsync(underlyingId, session, cancellationToken));
        }

        var buckets = accumulator.Rows(settings.MinQuotesPerBucket);
        var built = new BuiltSpreadModel(modelId, underlyingId, usable.Count, usable[0], usable[^1], SpreadModelId.SettingsJson(settings.MinUsableSessions, settings.MinQuotesPerBucket), buckets);
        var written = await store.AppendAsync(built, cancellationToken);
        var reason = string.Create(CultureInfo.InvariantCulture, $"{usable.Count} usable sessions from {usable[0]:yyyy-MM-dd} to {usable[^1]:yyyy-MM-dd}; {buckets.Count} buckets with enough quotes; {accumulator.Used} quotes used, {accumulator.Skipped} left out");
        return new SpreadModelBuildLine(symbol, written ? "built" : "already_built", reason, usable.Count, modelId, buckets.Count);
    }
}
