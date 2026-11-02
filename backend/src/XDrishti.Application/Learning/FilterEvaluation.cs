using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Selection;
using XDrishti.Domain.Common;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Application.Learning;

/// <summary>
/// Protocol F for the three windows N = 1, 2, 3 (P15-10): reads the population of the unfiltered stored runs, appends the three trials to the ledger <b>before</b> any number is computed, judges the development seasons per N, stores one record per N and, for each new record, runs the
/// auto rule of the event-risk filter once (inert unless the owner set <c>approval = auto</c>). The reserved seasons are read only by the hold-out act, after a stored KEEP, once per N.
/// </summary>
internal sealed class FilterEvaluation(
    IFilterPopulationSource population,
    EvaluationWriter writer,
    IExtensionEvaluationStore store,
    ICommandHandler<ApplyEventFilterAutoCommand, EventFilterAutoResult> autoRule,
    ILogger<FilterEvaluation> logger)
{
    private const string Method = "filter-compare";

    public async ValueTask<Result<ExtensionEvaluationOutcome>> RunAsync(bool holdOut, CancellationToken cancellationToken)
    {
        var options = new ProtocolFOptions();
        var read = await population.ReadAsync(cancellationToken);
        if (read.IsFailure)
        {
            return Result.Failure<ExtensionEvaluationOutcome>(read.Error);
        }

        var data = read.Value;
        // A spent hold-out refuses before anything is counted or written, whatever the data: a refused --holdout must not raise N.
        // shortcut: check-then-store is not atomic across two concurrent runs of the verb; the CLI has one owner and the table's index per window is the backstop. Add a per-family advisory lock if a second caller ever exists.
        if (holdOut)
        {
            foreach (var window in ProtocolF.Variants)
            {
                if (await store.HoldOutUsedAsync(ExtensionFamilies.Filter, VersionKey(window), cancellationToken))
                {
                    ExtensionEvaluationLog.Refused(logger, ExtensionFamilies.Filter, ExtensionEvaluationErrors.HoldOutUsed);
                    return Result.Failure<ExtensionEvaluationOutcome>(Error.Conflict(ExtensionEvaluationErrors.HoldOutUsed, "The hold-out of " + ExtensionFamilies.Filter + " has been read already; it is read once for every window."));
                }
            }
        }

        var byN = ProtocolF.Variants.ToDictionary(n => n, n => (IReadOnlyList<FilterTrade>)[.. data.Observations.Select(o => o.ToTrade(n))]);

        // Ledger first: the three trials are counted before any metric of any of them is computed.
        var trials = ProtocolF.Variants.ToDictionary(n => n, n => ExtensionFamilies.TrialOf(ExtensionFamilies.Filter, TrialText(n, options), data.Observations.Count == 0 ? default : data.Observations.Min(o => o.Date), data.Observations.Count == 0 ? default : data.Observations.Max(o => o.Date)));
        var variants = await writer.CountAsync(ExtensionFamilies.Filter, cancellationToken);
        if (data.Observations.Count > 0)
        {
            var from = data.Observations.Min(o => o.Date);
            var to = data.Observations.Max(o => o.Date);
            var population256 = ExtensionFamilies.Sha256Hex(string.Join(',', data.RunKeys));
            foreach (var n in ProtocolF.Variants)
            {
                variants = await writer.AppendTrialAsync(trials[n], population256, from, to, Method, cancellationToken);
            }
        }

        // Everything is computed before anything is written, so a refusal leaves no record and no switch change.
        var development = new List<(int N, ProtocolFResult Result, ExtensionEvaluationRecord Record)>();
        foreach (var n in ProtocolF.Variants)
        {
            var (trades, _, seasons) = ProtocolF.Split(byN[n]);
            var evaluated = ProtocolF.Evaluate(n, variants, seasons, trades, options);
            if (evaluated.IsFailure)
            {
                return Result.Failure<ExtensionEvaluationOutcome>(evaluated.Error);
            }

            development.Add((n, evaluated.Value, Identity(n, trials[n], variants, data, options, false, null).ToRecord(evaluated.Value.Outcome)));
        }

        var kept = development.Where(d => d.Result.Outcome.Verdict == ExtensionVerdict.Keep).ToList();
        var holdOutRecords = new List<ExtensionEvaluationRecord>();
        string? note = null;
        if (holdOut && kept.Count == 0)
        {
            note = "The hold-out was not read: no window has a development KEEP.";
            ExtensionEvaluationLog.HoldOutNotRead(logger, ExtensionFamilies.Filter, "no_development_keep");
        }
        else if (holdOut)
        {
            foreach (var (n, result, record) in kept)
            {
                var hold = ProtocolF.HoldOut(n, ProtocolF.Split(byN[n]).HoldOut, result.Basis, options);
                if (hold.IsFailure)
                {
                    return Result.Failure<ExtensionEvaluationOutcome>(hold.Error);
                }

                holdOutRecords.Add(Identity(n, trials[n], variants, data, options, true, record.EvaluationId).ToRecord(hold.Value));
            }
        }

        var records = new List<EvaluatedRecord>();
        foreach (var record in development.Select(d => d.Record).Concat(holdOutRecords))
        {
            var stored = await writer.StoreAsync(record, cancellationToken);
            if (stored.IsFailure)
            {
                return Result.Failure<ExtensionEvaluationOutcome>(stored.Error);
            }

            ExtensionEvaluationLog.Stored(logger, record.Family, record.Protocol, record.VersionKey, record.Verdict, record.Rows, stored.Value.Stored);
            records.Add(stored.Value);
        }

        // One run of the owner's auto option after all the new verdicts (a development KEEP and its hold-out reading are one event), never between them: no ON-then-OFF flapping. It acts only on a hold-out confirmed KEEP.
        // Run at the end of every successful run, not only when something new was stored: the rule is idempotent (already_on, already_off, keep_not_confirmed) and a run that stores nothing is the one that retries an earlier act that failed.
        EventFilterAutoResult? auto = null;
        var applied = await autoRule.HandleAsync(new ApplyEventFilterAutoCommand(), cancellationToken);
        if (applied.IsSuccess)
        {
            auto = applied.Value;
            ExtensionEvaluationLog.AutoRan(logger, auto.Action, auto.Reason);
        }

        return Result.Success(new ExtensionEvaluationOutcome(ExtensionFamilies.Filter, records, note, auto));
    }

    private static ProtocolIdentity Identity(int n, ExtensionTrialId trial, int variants, FilterPopulation data, ProtocolFOptions options, bool holdOut, string? developmentRecord) => new(
        ExtensionFamilies.Filter,
        ProtocolF.Protocol,
        null,
        VersionKey(n),
        "none",
        ExtensionFamilies.Sha256Hex(options.Canonical()),
        null,
        data.Observations.Count == 0 ? null : trial.Hash,
        variants,
        "quarter",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["rule_version"] = EventCalendarSnapshot.RuleVersion,
            ["results_within_days"] = n.ToString(CultureInfo.InvariantCulture),
            ["calendar_sha256"] = data.CalendarSha256,
            ["lag_minutes"] = data.LagMinutes.ToString(CultureInfo.InvariantCulture),
            ["engines"] = string.Join(',', data.Engines),
            ["run_keys_sha256"] = ExtensionFamilies.Sha256Hex(string.Join(',', data.RunKeys)),
            ["runs_used"] = data.RunKeys.Count.ToString(CultureInfo.InvariantCulture),
            ["runs_with_event_filter_left_out"] = data.RunsSkipped.ToString(CultureInfo.InvariantCulture),
            ["population"] = "closed trades of stored runs without not_event_day; gross R from the filled prices",
            ["development_record"] = developmentRecord ?? "none",
        },
        holdOut);

    /// <summary>The version key of a window: the rule version and N; the verdict port answers for the N of the effective switch.</summary>
    internal static string VersionKey(int n) => string.Create(CultureInfo.InvariantCulture, $"{EventCalendarSnapshot.RuleVersion}:N={n}");

    private static string TrialText(int n, ProtocolFOptions options) => string.Join(
        '\n',
        "filter-trial/1",
        "rule:" + EventCalendarSnapshot.RuleVersion,
        "results_within_days:" + n.ToString(CultureInfo.InvariantCulture),
        "population:closed trades of stored runs without not_event_day, gross R (filled)",
        "classes:skipped,taken,unavailable(no_entry,only_cancelled,no_upcoming_entry); unavailable counts as removed",
        "folds:season=calendar quarter; hold-out=last 4 seasons",
        "bootstrap:day-block",
        "options:" + options.Canonical());
}
