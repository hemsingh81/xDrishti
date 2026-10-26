using System.Text.Json;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Paper;

namespace XDrishti.Application.Paper;

/// <summary>
/// The epoch gate of the configuration governance (spec P10-05, ADR 0119). At each capture run it takes a snapshot of the configuration, compares it with the last full one and decides: equal or only risk-down differences keep the epoch; a frozen difference starts
/// a new epoch effective the next trading day and sets the sessions of the run aside (<c>straddle</c>); a snapshot that could not be read fully claims nothing. It detects, labels and records. It changes no setting, blocks nothing, applies no hypothesis and
/// writes only its own records.
/// </summary>
internal sealed class FingerprintEpochGate(
    IPaperStore paper,
    IGovernanceStore store,
    IConfigSnapshotSource source,
    IHypothesisLog hypotheses,
    ILearningChangelog changelog,
    IStrategyStore strategies,
    ITradingCalendar calendars,
    TimeProvider time,
    ILogger<FingerprintEpochGate> logger) : IEpochGate
{
    private const int ReadLimit = 200;

    private const string Process = "xd-api";

    public async ValueTask<EpochResolution> ResolveAsync(IReadOnlyList<DateOnly> sessions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        var distinct = sessions.Distinct().Order().ToList();
        var epoch = await paper.CurrentEpochAsync(cancellationToken);
        if (epoch is not null && distinct.Count == 0)
        {
            return new EpochResolution(epoch, new Dictionary<DateOnly, string>(), await HistoryAsync(cancellationToken));
        }

        var now = time.GetUtcNow();
        var reading = await source.ReadAsync(cancellationToken);
        var partial = reading.Unreadable.Count > 0;
        var fingerprint = ConfigFingerprint.Create(reading.Components);
        var calendar = await calendars.GetAsync(cancellationToken);
        var created = epoch is null;
        epoch ??= await store.CreateFirstEpochAsync(IndiaTime.Today(now), now, partial ? null : fingerprint.Hash, cancellationToken);
        var newest = distinct.Count == 0 ? calendar.LastClosedSession(now) : distinct[^1];

        // shortcut: a snapshot is taken only on a run that has sessions to capture (a run with none reads nothing), so a change that is made and reversed between two such runs is invisible. Upgrade: a daily snapshot job independent of the capture.
        var snapshot = new NewSnapshot(now, newest, fingerprint.Hash, fingerprint.CanonicalJson, partial, Process);
        if (partial)
        {
            GovernanceLog.Unreadable(logger, string.Join(',', reading.Unreadable));
        }

        var lastFull = await store.LatestSnapshotAsync(true, cancellationToken);
        var before = lastFull is null ? null : ConfigFingerprint.FromJson(lastFull.ComponentsJson);
        var changes = partial || before is null ? [] : ConfigDiff.Compare(before.Values, fingerprint.Values);
        var decision = EpochDecision.Decide(distinct, before is not null, partial, changes, s => lastFull is null ? null : calendar.TradingDaysBetween(lastFull.SessionDate, s));

        NewDetectedEpoch? started = null;
        var stored = new List<NewChange>();
        if (changes.Count > 0 && lastFull is not null)
        {
            var explained = await ExplainAsync(changes, lastFull.TakenAt, now, cancellationToken);
            stored = [.. changes.Select(c => new NewChange(c.Key, c.Class.Code(), c.Before, c.After, c.Class == ConfigClass.Frozen ? "new_epoch" : "noted", explained.GetValueOrDefault(c.Key)))];
            if (decision.Outcome == EpochOutcome.NewEpoch)
            {
                var frozen = changes.Count(c => c.Class == ConfigClass.Frozen);
                started = new NewDetectedEpoch(NextTradingDay(calendar, newest), now, fingerprint.Hash, $"the configuration changed ({frozen} frozen difference(s)); the count restarts");
            }
        }

        await store.CommitAsync(new GovernanceCommit(snapshot, lastFull?.Id, started, epoch.Id, stored), cancellationToken);
        GovernanceLog.Snapshot(logger, fingerprint.Hash[..12], changes.Count, created && decision.Outcome == EpochOutcome.FirstSnapshot ? "first" : decision.Outcome.ToString());
        foreach (var c in changes)
        {
            GovernanceLog.Change(logger, Group(c.Key), c.Class.Code(), c.Class == ConfigClass.Frozen ? "new_epoch" : "noted");
        }

        if (started is not null)
        {
            GovernanceLog.EpochStarted(logger, "detected_change", started.StartedOn, fingerprint.Hash[..12]);
        }

        return new EpochResolution(epoch, decision.MeasureStatus, await HistoryAsync(cancellationToken));
    }

    private async Task<IReadOnlyList<EpochRow>> HistoryAsync(CancellationToken cancellationToken) => [.. (await store.ListEpochsAsync(cancellationToken)).Select(e => e.Epoch)];

    /// <summary>The key without the part that can name an account or a cell.</summary>
    internal static string Group(string key)
    {
        var slash = key.IndexOf('/', StringComparison.Ordinal);
        return slash < 0 ? key : key[..(slash + 1)];
    }

    private static DateOnly NextTradingDay(TradingCalendar calendar, DateOnly session)
    {
        var day = session.AddDays(1);
        for (var i = 0; i < 15 && !calendar.IsTradingDay(day); i++)
        {
            day = day.AddDays(1);
        }

        return day;
    }

    private async Task<IReadOnlyDictionary<string, string?>> ExplainAsync(IReadOnlyList<ConfigChange> changes, DateTimeOffset previousAt, DateTimeOffset detectedAt, CancellationToken cancellationToken)
    {
        var accepted = (await hypotheses.ListAsync("accepted", ReadLimit, cancellationToken))
            .Select(h => new AcceptedHypothesis(h.HypothesisId, h.Row.CellKey, h.Row.KnobKind, h.Row.KnobName, h.Row.CandidateValue, h.Row.CreatedAt)).ToList();
        var log = (await changelog.ListAsync("cell", null, ReadLimit, cancellationToken)).Select(c => new ChangelogFact(c.Id, c.Entry.Subject, c.Entry.At)).ToList();
        var used = await store.UsedHypothesisIdsAsync(cancellationToken);
        var parameters = new Dictionary<string, IReadOnlyDictionary<string, string>?>(StringComparer.Ordinal);
        foreach (var hash in changes.Where(c => c.Key.StartsWith(PaperConfigKeys.CellsEligible, StringComparison.Ordinal) && c.After is not null).Select(c => c.After!).Distinct(StringComparer.Ordinal))
        {
            parameters[hash] = accepted.Count == 0 ? null : VersionParameters.Read((await strategies.GetByHashAsync(hash, cancellationToken))?.CanonicalJson);
        }

        return ChangeExplainer.Explain(changes, accepted, used, log, h => parameters.GetValueOrDefault(h), previousAt, detectedAt);
    }
}

/// <summary>The stored parameters of a strategy version, read from its canonical JSON. UNVERIFIED shape (kept as a marker until it is checked against real stored versions): the first <c>params</c> object, its scalar members as text. Anything else gives null.</summary>
internal static class VersionParameters
{
    public static IReadOnlyDictionary<string, string>? Read(string? canonicalJson)
    {
        if (string.IsNullOrWhiteSpace(canonicalJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(canonicalJson);
            return Find(doc.RootElement) is { } node
                ? node.EnumerateObject().Where(p => p.Value.ValueKind is JsonValueKind.Number or JsonValueKind.String or JsonValueKind.True or JsonValueKind.False)
                    .ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : p.Value.GetRawText(), StringComparer.Ordinal)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement? Find(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in element.EnumerateObject())
            {
                if (p.NameEquals("params") && p.Value.ValueKind == JsonValueKind.Object)
                {
                    return p.Value;
                }

                if (Find(p.Value) is { } inner)
                {
                    return inner;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (Find(item) is { } inner)
                {
                    return inner;
                }
            }
        }

        return null;
    }
}
