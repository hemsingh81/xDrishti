using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Options;
using XDrishti.Application.Options.Import;
using XDrishti.Domain.Options;

namespace XDrishti.Application.Options;

/// <summary>
/// The option step of the master refresh (spec P14-01, ADR 0175). Before the download it reads the setting <c>options.underlyings</c> (with no row yet it builds the
/// default: the four indices and the 30 most traded F&amp;O shares, written once only when all 30 are ranked on fresh daily bars, never recomputed afterwards); after
/// the master list is saved it resolves the listed underlyings and hands the contracts and the observed lots to the store. It never fails the refresh. Its counts
/// go to the log; the run's warnings get a line only for a real problem (an unreadable setting or entry, an unknown underlying, unreadable or conflicting rows, a
/// reused security id, a file without the option columns, a failed save), so a normal refresh shows no warning.
/// </summary>
internal sealed class OptionContractImport(IOptionUnderlyingSetting setting, IOptionContractStore store, ILogger<OptionContractImport> logger)
{
    /// <summary>The actor recorded on the setting row when the default is fixed (never an owner act).</summary>
    public const string FirstImportActor = "system:options-first-import";

    /// <summary>The ranking is fresh when its newest daily bar is at most this many days old (about five sessions).</summary>
    public const int MaxBarAgeDays = 7;

    private const int MaxNamed = 5;

    public async ValueTask<(OptionUnderlyings Listed, IReadOnlyList<string> Problems)> ListingAsync(DateOnly today, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            var stored = await setting.ReadAsync(cancellationToken);
            if (stored is not null)
            {
                var listed = OptionUnderlyings.Parse(stored);
                return (listed, listed.Rejected.Count == 0 ? [] : [$"Option underlyings: {Count(listed.Rejected.Count)} entries of the setting options.underlyings are not a symbol and were ignored."]);
            }

            var ranked = await setting.MostTradedFnoSharesAsync(today, OptionUnderlyings.DefaultShares, cancellationToken);
            var list = OptionUnderlyings.From([.. OptionUnderlyings.DefaultIndices, .. ranked.Symbols]);
            if (ranked.Symbols.Count < OptionUnderlyings.DefaultShares)
            {
                OptionsLog.DefaultNotFixed(logger, ranked.Symbols.Count, OptionUnderlyings.DefaultShares);
                return (list, []);
            }

            if (ranked.NewestBar is not { } newest || newest < today.AddDays(-MaxBarAgeDays))
            {
                OptionsLog.DefaultNotFixed(logger, ranked.Symbols.Count, OptionUnderlyings.DefaultShares);
                return (list, [$"Option underlyings are not fixed: the newest daily bar of the ranking ({(ranked.NewestBar?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "none")}) is more than {MaxBarAgeDays} days old; this refresh uses the ranking as it is."]);
            }

            if (await setting.FixDefaultAsync(list.ToStored(), FirstImportActor, now, cancellationToken))
            {
                OptionsLog.DefaultFixed(logger, OptionUnderlyings.DefaultIndices.Count, ranked.Symbols.Count);
                return (list, []);
            }

            return (OptionUnderlyings.Parse(await setting.ReadAsync(cancellationToken)), []);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // the option archive is an extra: whatever goes wrong with it costs the option rows of this refresh, never the master list
        catch (Exception ex)
#pragma warning restore CA1031
        {
            OptionsLog.StepFailed(logger, "listing", ex);
            return (OptionUnderlyings.Empty, ["Option contracts were not updated: the underlying list could not be read."]);
        }
    }

    public async ValueTask<IReadOnlyList<string>> ApplyAsync(OptionParse? parse, OptionUnderlyings listed, DateOnly today, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(listed);
        if (parse is null || listed.IsEmpty)
        {
            return [];
        }

        if (parse.Unavailable is not null)
        {
            return [parse.Unavailable];
        }

        try
        {
            var problems = new List<string>();
            var ids = await store.ResolveUnderlyingsAsync(listed.Symbols, cancellationToken);
            var unknown = listed.Symbols.Where(s => !ids.ContainsKey(s)).ToList();
            var candidates = new List<OptionContractCandidate>(parse.Rows.Count);
            var unmapped = 0;
            foreach (var row in parse.Rows)
            {
                if (ids.TryGetValue(row.UnderlyingSymbol, out var id))
                {
                    candidates.Add(new OptionContractCandidate(row.SecurityId, id, row.Instrument, row.Expiry, row.StrikePx, row.Side, row.ExpiryKind, row.LotSize, row.TickPx));
                }
                else
                {
                    unmapped++;
                }
            }

            if (unknown.Count > 0)
            {
                OptionsLog.UnknownUnderlyings(logger, unknown.Count);
                problems.Add($"Option underlyings not in the master list as an active index or share ({Count(unknown.Count)}, unmapped_underlying {Count(unmapped)} rows): "
                    + $"{string.Join(", ", unknown.Take(MaxNamed))}{(unknown.Count > MaxNamed ? " …" : string.Empty)}.");
            }

            var (lots, mixed) = ObservedLots.Pick(candidates, today);
            var applied = await store.ApplyAsync(candidates, lots, LotSizeSeed.All, today, cancellationToken);
            OptionsLog.ContractsApplied(
                logger, candidates.Count, lots.Count, applied.Inserted, applied.Advanced, applied.Conflicts, applied.ReusedIds, applied.LotChanges, mixed, parse.SkippedNotListed, unmapped, parse.Invalid);
            if (parse.Invalid > 0)
            {
                problems.Add($"Option contracts: {Count(parse.Invalid)} rows of listed underlyings could not be read (invalid) and were not stored.");
            }

            if (applied.ReusedIds > 0)
            {
                problems.Add($"Option contracts: {Count(applied.ReusedIds)} Dhan security ids now name a different contract than the stored one (reused_id); a new expiry is stored as its own row, any other change was neither stored nor advanced.");
            }

            if (applied.Conflicts > 0)
            {
                problems.Add($"Option contracts: {Count(applied.Conflicts)} rows name a contract another security id already holds (conflicting) and were not stored.");
            }

            return problems;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // the option archive is an extra: whatever goes wrong with it costs the option rows of this refresh, never the master list
        catch (Exception ex)
#pragma warning restore CA1031
        {
            OptionsLog.StepFailed(logger, "apply", ex);
            return ["Option contracts were not updated: saving them failed (see the log); the instrument list is saved."];
        }
    }

    private static string Count(long value) => value.ToString(CultureInfo.InvariantCulture);
}
