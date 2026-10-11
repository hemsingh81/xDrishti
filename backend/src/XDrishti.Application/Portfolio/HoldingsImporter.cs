using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio;

/// <summary>
/// The holdings to import: the keys (from the preview) with an optional date each; <c>DefaultDate</c> dates the rows that have none (flagged then). Null: the first
/// import date of the account. With <c>All</c> every holding Dhan lists is imported and the rows only carry dates that override the default for their key.
/// </summary>
internal sealed record ImportInput(DateOnly? DefaultDate, IReadOnlyList<ImportRowInput>? Rows, bool All = false);

/// <summary>What an import did: holdings created (or brought back), keys that were imported already, keys Dhan does not list, and lots on the default date.</summary>
internal sealed record ImportResultView(int Imported, int AlreadyImported, int Unknown, int DefaultDateLots);

/// <summary>A finished import: the sync run it was recorded as and what it did.</summary>
internal sealed record ImportOutcome(Guid RunId, ImportResultView Result);

/// <summary>
/// Imports the holdings of a connected Dhan account with acquisition dates (ADR 0012, 0013). Not a use case of its own: the first import happens when the
/// Portfolio settings are saved (<see cref="Accounts.UpdatePortfolioHandler"/>) or at the first sync (<see cref="SyncHoldingsHandler"/>). Dhan's holdings are
/// fetched again (nothing from the preview is trusted), and the chosen keys are created, each with one lot dated by its row or the default date (flagged then).
/// Keys the account has imported already are skipped, keys Dhan does not list are counted, a closed holding that is listed again is brought back. All or
/// nothing; recorded as a sync run of trigger "import" (one run per account at a time: <c>portfolio.sync_running</c>).
/// </summary>
internal sealed class HoldingsImporter(
    HoldingsFetcher fetcher,
    SyncRuns runs,
    IHoldingStore holdings,
    IAccountEventLog events,
    TimeProvider clock,
    ILogger<HoldingsImporter> logger)
{
    /// <summary>More rows than any account has holdings; keeps a request bounded.</summary>
    public const int MaxRows = 5000;

    /// <summary>Imports the chosen holdings of an account (which must have the Portfolio role on and be connected).</summary>
    public async ValueTask<Result<ImportOutcome>> ImportAsync(Guid accountId, ImportInput input, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var account = await fetcher.AccountAsync(accountId, cancellationToken);
        if (account.IsFailure)
        {
            return Result.Failure<ImportOutcome>(account.Error);
        }

        var today = IndiaTime.Today(clock.GetUtcNow());
        var defaultDate = input.DefaultDate ?? account.Value.Portfolio.ImportDate;
        var errors = new FieldErrors();
        var requested = Check(input, defaultDate, today, errors, string.Empty);
        if (errors.HasErrors)
        {
            return Result.Failure<ImportOutcome>(PortfolioErrors.Invalid(errors));
        }

        var bucket = await fetcher.BucketAsync(account.Value, cancellationToken);
        if (bucket.IsFailure)
        {
            return Result.Failure<ImportOutcome>(bucket.Error);
        }

        var connected = await fetcher.ConnectedAsync(account.Value.Id, cancellationToken);
        if (connected.IsFailure)
        {
            return Result.Failure<ImportOutcome>(connected.Error);
        }

        return await RunAsync(account.Value, bucket.Value, defaultDate, input.All ? AllWithDates(requested) : _ => requested, actor, cancellationToken);
    }

    /// <summary>
    /// Imports every holding Dhan lists, each dated with the account's first import date (flagged as default). The caller has checked the account (role,
    /// bucket, connection); this is the first sync.
    /// </summary>
    public ValueTask<Result<ImportOutcome>> ImportAllAsync(BrokerAccount account, Guid bucket, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        return RunAsync(
            account,
            bucket,
            account.Portfolio.ImportDate,
            fetched => [.. fetched.Rows.Select(r => (r.Key, (DateOnly?)null))],
            actor,
            cancellationToken);
    }

    /// <summary>Every fetched holding; a key named in <paramref name="dates"/> takes its date, the others get the default date.</summary>
    private static Func<FetchedHoldings, IReadOnlyList<(string Key, DateOnly? AcquiredOn)>> AllWithDates(IReadOnlyList<(string Key, DateOnly? AcquiredOn)> dates)
    {
        var byKey = dates.ToDictionary(d => d.Key, d => d.AcquiredOn, StringComparer.OrdinalIgnoreCase);
        return fetched => [.. fetched.Rows.Select(r => (r.Key, byKey.GetValueOrDefault(r.Key)))];
    }

    /// <summary>
    /// Checks the dates and rows of an import request, adding field errors (under <paramref name="prefix"/>, for example <c>firstImport.</c>), and returns the
    /// requested keys with their dates; later duplicates of a key are dropped.
    /// </summary>
    public static IReadOnlyList<(string Key, DateOnly? AcquiredOn)> Check(ImportInput? input, DateOnly defaultDate, DateOnly today, FieldErrors errors, string prefix)
    {
        ArgumentNullException.ThrowIfNull(errors);
        HoldingRules.CheckDate(errors, prefix + "defaultDate", defaultDate, today);
        var rows = input?.Rows ?? [];
        if (rows.Count > MaxRows)
        {
            errors.Add(prefix + "rows", $"At most {MaxRows} rows.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keys = new List<(string Key, DateOnly? AcquiredOn)>(Math.Min(rows.Count, MaxRows));
        for (var i = 0; i < Math.Min(rows.Count, MaxRows); i++)
        {
            var row = rows[i];
            if (string.IsNullOrWhiteSpace(row?.Key))
            {
                errors.Add($"{prefix}rows[{i}].key", "Required.");
                continue;
            }

            if (row.AcquiredOn is { } date)
            {
                HoldingRules.CheckDate(errors, $"{prefix}rows[{i}].acquiredOn", date, today);
            }

            if (seen.Add(row.Key.Trim()))
            {
                keys.Add((row.Key.Trim(), row.AcquiredOn));
            }
        }

        return keys;
    }

    private async ValueTask<Result<ImportOutcome>> RunAsync(
        BrokerAccount account,
        Guid bucket,
        DateOnly defaultDate,
        Func<FetchedHoldings, IReadOnlyList<(string Key, DateOnly? AcquiredOn)>> select,
        string actor,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var outcome = await runs.RunAsync<ImportResultView>(
            account,
            SyncTrigger.Import,
            actor,
            token => fetcher.FetchAsync(account.Id, token),
            async (fetched, token) =>
            {
                var existing = await holdings.ListForAccountAsync(account.Id, token);
                var index = new HoldingIndex(existing);
                var byKey = fetched.Rows.ToDictionary(r => r.Key, StringComparer.OrdinalIgnoreCase);
                var imported = 0;
                var already = 0;
                var unknown = 0;
                var defaultLots = 0;
                foreach (var (key, acquiredOn) in select(fetched))
                {
                    if (!byKey.TryGetValue(key, out var row))
                    {
                        unknown++;
                        continue;
                    }

                    var match = index.Find(row);
                    if (match is { Status: HoldingStatus.Open })
                    {
                        already++;
                        continue;
                    }

                    var instrument = fetched.Instruments.GetValueOrDefault(row.Key);
                    var date = acquiredOn ?? defaultDate;
                    var usesDefault = acquiredOn is null;
                    if (match is null)
                    {
                        holdings.Add(Holding.Open(account.Id, bucket, instrument?.Id, instrument?.Name, row, date, usesDefault, LotSource.Import, now));
                    }
                    else
                    {
                        match.Reopen(row, instrument?.Id, instrument?.Name, date, usesDefault, LotSource.Import, now);
                    }

                    imported++;
                    defaultLots += usesDefault ? 1 : 0;
                }

                events.Record(AccountEvent.Record(
                    AccountEventType.HoldingsImported,
                    account.Id,
                    $"Holdings imported: {imported} imported, {already} already imported, {unknown} not listed by Dhan; {defaultLots} lots on the default date {defaultDate:yyyy-MM-dd}",
                    actor,
                    now));
                var counts = new SyncCounts(imported, 0, 0, 0, already);
                return Result.Success(new Applied<ImportResultView>(new ImportResultView(imported, already, unknown, defaultLots), counts));
            },
            cancellationToken);
        if (outcome.IsFailure)
        {
            return Result.Failure<ImportOutcome>(outcome.Error);
        }

        var result = outcome.Value.Value;
        PortfolioLog.Imported(logger, account.Slug, result.Imported, result.AlreadyImported, result.Unknown);
        return Result.Success(new ImportOutcome(outcome.Value.RunId, result));
    }
}
