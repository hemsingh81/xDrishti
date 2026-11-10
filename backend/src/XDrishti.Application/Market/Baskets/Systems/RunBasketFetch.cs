using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Baskets;
using XDrishti.Domain.Market.Baskets.Lists;

namespace XDrishti.Application.Market.Baskets;

/// <summary>
/// Fetches the index lists of the enabled sources (BK-01): <paramref name="BasketIds"/> null = every enabled source that is due; with ids (the owner's "fetch now") those of them that the owner has turned on, ignoring the schedule and a rest. A fetch never turns a source on.
/// <paramref name="Force"/> overrides only the tripwire. Fails with <c>baskets.fetch_disabled</c> and makes no request at all while <c>Baskets:System:Fetch:Enabled</c> is off.
/// </summary>
public sealed record RunBasketFetchCommand(IReadOnlyList<Guid>? BasketIds, bool Force, string Actor) : ICommand;

internal sealed class RunBasketFetchHandler(
    IBasketFetchSettings settings,
    IBasketSourceStore sources,
    IBasketSourceFetcher fetcher,
    IBasketStore baskets,
    IBasketMembershipApplier applier,
    IBasketRunLock runLock,
    IListSettings listSettings,
    ListFileProcessor lists,
    IFoLotSizeStore lotStore,
    TimeProvider clock,
    ILogger<RunBasketFetchHandler> logger) : ICommandHandler<RunBasketFetchCommand, BasketFetchRun>
{
    public const string NoUserAgent = "no_user_agent";

    public async ValueTask<Result<BasketFetchRun>> HandleAsync(RunBasketFetchCommand command, CancellationToken cancellationToken)
    {
        if (!settings.Enabled && !listSettings.FetchEnabled)
        {
            return Result.Failure<BasketFetchRun>(BasketErrors.FetchDisabled);
        }

        if (string.IsNullOrWhiteSpace(settings.UserAgent))
        {
            return Result.Failure<BasketFetchRun>(BasketErrors.Invalid("userAgent", "Set Baskets:System:Fetch:UserAgent (a descriptive name and a contact) before fetching."));
        }

        // One run at a time (fetch-now and the schedule, across processes): a second one is refused, not queued.
        await using var held = await runLock.TryAcquireAsync(cancellationToken);
        if (held is null)
        {
            return Result.Failure<BasketFetchRun>(BasketErrors.FetchRunning);
        }

        var manual = command.BasketIds is { Count: > 0 };
        var list = await sources.ListAsync(manual ? command.BasketIds : null, cancellationToken);
        var outcomes = new List<FetchOutcomeView>();
        foreach (var source in list.Where(s => s.Enabled && Allowed(s)))
        {
            if (!manual && !Due(source, clock.GetUtcNow()))
            {
                continue; // checked today already, resting after a failure, or before the run time
            }

            outcomes.Add(await FetchOneAsync(source, command, manual, cancellationToken));
        }

        return Result.Success(new BasketFetchRun(outcomes));
    }

    private async ValueTask<FetchOutcomeView> FetchOneAsync(BasketSourceInfo source, RunBasketFetchCommand command, bool manual, CancellationToken cancellationToken)
    {
        var started = clock.GetTimestamp();
        var now = clock.GetUtcNow();
        var enabled = source.Enabled;
        var response = await fetcher.GetAsync(settings.PreferredHost == "B" && source.HostAlt is { Length: > 0 } alt ? source with { SourceUrl = alt, HostAlt = source.SourceUrl } : source, cancellationToken);
        var fetch = new FetchRecord(
            Guid.CreateVersion7(now), source.BasketId, now, response.Status == 0 ? null : response.Status, response.Etag, response.LastModified, null, 0, FetchOutcomes.Failed, 0, 0, 0, "[]", null,
            command.Force, command.Actor, command.Force ? settings.Snapshot() : null);

        FetchOutcomeView Done(FetchRecord record)
        {
            var milliseconds = (long)clock.GetElapsedTime(started).TotalMilliseconds;
            BasketLog.Fetched(logger, source.BasketId, record.Outcome, record.Rows, record.Added, record.Removed, record.Unmatched, milliseconds);
            return ToView(record);
        }

        // Nothing came back, or the host refuses us: failed; the validators and the members stay as they were.
        if (response.Status is 0 or 403 or 429 or >= 500)
        {
            // 403/429 rest the source for the backoff hours; any other failure is tried again after 5, 30 and 120 minutes (the settings), then not before the next day.
            var tries = TriesToday(source, now) + 1;
            var rest = response.Status is 403 or 429
                ? now.AddHours(settings.BackoffHours)
                : tries <= settings.BackoffMinutes.Count ? now.AddMinutes(settings.BackoffMinutes[tries - 1]) : (DateTimeOffset?)null;
            var failed = fetch with { Outcome = FetchOutcomes.Failed, Error = response.Error ?? $"http_{response.Status}" };
            await sources.RecordAsync(Updated(source, enabled, now, ok: false, null, null, null, rest, tries), failed, cancellationToken);
            return Done(failed);
        }

        if (response.Status == 304)
        {
            var unchanged = fetch with { Outcome = FetchOutcomes.Unchanged, Sha256 = source.Sha256, Etag = response.Etag ?? source.Etag, LastModified = response.LastModified ?? source.LastModified };
            await sources.RecordAsync(Updated(source, enabled, now, ok: true, unchanged.Etag, unchanged.LastModified, source.Sha256, null, 0), unchanged, cancellationToken);
            return Done(unchanged);
        }

        var listFile = ListBaskets.IsListFile(source.Format);
        if (response.Status != 200 || response.Body is null || !(listFile ? IsNotHtml(response.ContentType) : IsCsv(response.ContentType)))
        {
            return Done(await RejectAsync(source, enabled, fetch, response.Status == 200 ? "not_csv" : $"http_{response.Status}", now, cancellationToken));
        }

        var sha = Convert.ToHexStringLower(SHA256.HashData(response.Body));
        fetch = fetch with { Sha256 = sha };
        if (sha == source.Sha256)
        {
            var same = fetch with { Outcome = FetchOutcomes.Unchanged };
            await sources.RecordAsync(Updated(source, enabled, now, ok: true, response.Etag, response.LastModified, sha, null, 0), same, cancellationToken);
            return Done(same);
        }

        if (listFile)
        {
            return Done(await ApplyListFileAsync(source, response, fetch, sha, now, cancellationToken));
        }

        var file = IndexCsv.Parse(System.Text.Encoding.UTF8.GetString(response.Body));
        fetch = fetch with { Rows = file.Rows.Count };
        if (!file.HeaderOk)
        {
            return Done(await RejectAsync(source, enabled, fetch, FetchGuard.BadHeader, now, cancellationToken));
        }

        var matching = await MatchAsync(file, cancellationToken);
        var current = (await baskets.ListActiveMembersAsync(source.BasketId, cancellationToken)).Select(m => m.InstrumentId).ToList();
        var change = MembershipDiff.Compute(current, matching.Ids);
        fetch = fetch with { Unmatched = matching.Unmatched, WarningsJson = JsonSerializer.Serialize(matching.Warnings), Added = change.Added.Count, Removed = change.Removed.Count };

        var today = IndiaTime.Today(now);
        var verdict = FetchGuard.Check(
            new FetchFacts(true, file.Rows.Count, source.ExpectedMin, source.ExpectedMax, matching.Unmatched, current.Count, change.Added.Count, change.Removed.Count,
                ReviewWindows.Contains(today, settings.ReviewMonths, settings.ReviewFromDay, settings.ReviewToDay), command.Force),
            settings.Limits);
        if (!verdict.Ok)
        {
            return Done(await RejectAsync(source, enabled, fetch with { Added = 0, Removed = 0 }, verdict.Reason!, now, cancellationToken));
        }

        if (change.Added.Count == 0 && change.Removed.Count == 0)
        {
            var same = fetch with { Outcome = FetchOutcomes.Unchanged };
            await sources.RecordAsync(Updated(source, enabled, now, ok: true, response.Etag, response.LastModified, sha, null, 0), same, cancellationToken);
            return Done(same);
        }

        var applied = fetch with { Outcome = FetchOutcomes.Applied };
        var effective = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).ToUniversalTime(); // the start of the India date, as the database takes it (UTC)
        var done = await applier.ApplyAsync(
            new MembershipApplication(source.BasketId, source.Name, change, matching.Ids, effective, now, Updated(source, enabled, now, ok: true, response.Etag, response.LastModified, sha, null, 0), applied),
            cancellationToken);
        return Done(done ? applied : applied with { Outcome = FetchOutcomes.Failed, Error = "concurrent_change", Added = 0, Removed = 0 });
    }

    // The index lists need Baskets:System:Fetch:Enabled, the two NSE F&O files Baskets:System:Lists:FetchEnabled; the lists read from the master are never fetched.
    private bool Allowed(BasketSourceInfo source) =>
        source.Format == ListBaskets.IndexFormat ? settings.Enabled : ListBaskets.IsListFile(source.Format) && listSettings.FetchEnabled;

    private async ValueTask<FetchRecord> ApplyListFileAsync(BasketSourceInfo source, SourceResponse response, FetchRecord fetch, string sha, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var plan = await lists.PlanAsync(source, response.Body!, fetch, now, cancellationToken);
        if (plan.Reject is { } reason)
        {
            return await RejectAsync(source, source.Enabled, plan.Fetch, reason, now, cancellationToken);
        }

        var update = Updated(source, source.Enabled, now, ok: true, response.Etag, response.LastModified, sha, null, 0);
        if (plan.Lots is { } rows)
        {
            if (rows.Count == 0)
            {
                var same = plan.Fetch with { Outcome = FetchOutcomes.Unchanged, Added = 0 };
                await sources.RecordAsync(update, same, cancellationToken);
                return same;
            }

            var stored = plan.Fetch with { Outcome = FetchOutcomes.Applied };
            await lotStore.ApplyAsync(update, stored, plan.LotDate, rows, cancellationToken);
            return stored;
        }

        if (plan.Change!.Added.Count == 0 && plan.Change.Removed.Count == 0)
        {
            var same = plan.Fetch with { Outcome = FetchOutcomes.Unchanged };
            await sources.RecordAsync(update, same, cancellationToken);
            return same;
        }

        var applied = plan.Fetch with { Outcome = FetchOutcomes.Applied };
        var done = await applier.ApplyAsync(new MembershipApplication(source.BasketId, source.Name, plan.Change, plan.Order!, plan.Effective, now, update, applied, plan.Origin), cancellationToken);
        return done ? applied : applied with { Outcome = FetchOutcomes.Failed, Error = "concurrent_change", Added = 0, Removed = 0 };
    }

    /// <summary>Whether the schedule asks this source now: after its retry time when one is set, otherwise once a day from the run time (twice a day in a review window); the NSE F&amp;O files at their own India times.</summary>
    private bool Due(BasketSourceInfo source, DateTimeOffset now)
    {
        if (source.NextAttemptAt is { } next)
        {
            return next <= now;
        }

        if (source.Format == ListBaskets.BanFormat)
        {
            return ListSchedule.IsDue(now, source.LastCheckedAt, listSettings.BanRunTimes);
        }

        if (source.Format == ListBaskets.LotsFormat)
        {
            return ListSchedule.IsDue(now, source.LastCheckedAt, [listSettings.LotSizeRunAt]);
        }

        if (TimeOnly.FromDateTime(IndiaTime.ToIst(now).DateTime) < settings.RunAt)
        {
            return false;
        }

        if (source.LastCheckedAt is not { } last || IndiaTime.Today(last) < IndiaTime.Today(now))
        {
            return true;
        }

        return ReviewWindows.Contains(IndiaTime.Today(now), settings.ReviewMonths, settings.ReviewFromDay, settings.ReviewToDay) && now - last >= TimeSpan.FromHours(12);
    }

    // The failures of the retry sequence count for one India day.
    private static int TriesToday(BasketSourceInfo source, DateTimeOffset now) =>
        source.LastCheckedAt is { } last && IndiaTime.Today(last) == IndiaTime.Today(now) ? source.FailCount : 0;

    private async ValueTask<FetchRecord> RejectAsync(BasketSourceInfo source, bool enabled, FetchRecord fetch, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rejected = fetch with { Outcome = FetchOutcomes.Rejected, Error = reason };
        await sources.RecordAsync(Updated(source, enabled, now, ok: false, null, null, null, null, TriesToday(source, now) + 1, BasketSourceStates.Rejected), rejected, cancellationToken);
        return rejected;
    }

    // On success the validators and hash are kept for the next conditional GET; on failure the old ones stay, so the same file is looked at again.
    private SourceUpdate Updated(BasketSourceInfo source, bool enabled, DateTimeOffset now, bool ok, string? etag, string? lastModified, string? sha, DateTimeOffset? rest, int failCount, string? state = null)
    {
        var lastOk = ok ? now : source.LastOkAt;
        var stale = !ok && lastOk is { } at && now - at > TimeSpan.FromDays(settings.StaleAfterDays);
        return new SourceUpdate(
            source.BasketId,
            enabled,
            ok ? BasketSourceStates.Ok : stale ? BasketSourceStates.Stale : state ?? BasketSourceStates.Failed,
            now,
            lastOk,
            ok ? etag : source.Etag,
            ok ? lastModified : source.LastModified,
            ok ? sha : source.Sha256,
            rest,
            failCount);
    }

    private async ValueTask<Matching> MatchAsync(IndexFile file, CancellationToken cancellationToken)
    {
        var found = await sources.FindInstrumentsAsync(
            [.. file.Rows.Select(r => r.Isin).Where(i => i.Length > 0)], [.. file.Rows.Select(r => r.Symbol)], cancellationToken);
        var byIsin = found.Where(i => i.Isin is not null).GroupBy(i => i.Isin!, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => Prefer(g), StringComparer.OrdinalIgnoreCase);
        var bySymbol = found.GroupBy(i => i.Symbol, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => Prefer(g), StringComparer.OrdinalIgnoreCase);

        var ids = new List<Guid>();
        var warnings = new List<string>();
        var unmatched = 0;
        foreach (var row in file.Rows)
        {
            MasterInstrument? hit;
            if (row.Isin.Length > 0 && byIsin.TryGetValue(row.Isin, out hit))
            {
                // matched by ISIN: a renamed symbol is fine
            }
            else if (bySymbol.TryGetValue(row.Symbol, out hit))
            {
                warnings.Add($"matched_by_symbol:{row.Symbol}");
            }
            else
            {
                unmatched++;
                warnings.Add($"unmatched:{row.Symbol}");
                continue;
            }

            if (hit.RetiredAt is { } retired)
            {
                warnings.Add($"retired:{row.Symbol}:{IndiaTime.Today(retired):yyyy-MM-dd}");
            }

            if (!ids.Contains(hit.Id))
            {
                ids.Add(hit.Id);
            }
        }

        return new Matching(ids, unmatched, warnings);
    }

    private static MasterInstrument Prefer(IEnumerable<MasterInstrument> candidates) =>
        candidates.OrderBy(c => c.RetiredAt is not null).ThenBy(c => c.Symbol, StringComparer.Ordinal).First();

    private static bool IsNotHtml(string? contentType) =>
        contentType is null || !contentType.Split(';')[0].Trim().Equals("text/html", StringComparison.OrdinalIgnoreCase);

    private static bool IsCsv(string? contentType) =>
        contentType is not null && contentType.Split(';')[0].Trim().Equals("text/csv", StringComparison.OrdinalIgnoreCase);

    private static FetchOutcomeView ToView(FetchRecord r) => new(r.BasketId, r.FetchedAt, r.Outcome, r.HttpStatus, r.Rows, r.Added, r.Removed, r.Unmatched, r.Error, r.Forced);

    private sealed record Matching(List<Guid> Ids, int Unmatched, List<string> Warnings);
}
