using System.Globalization;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning.Jobs;

/// <summary>The knob of a job.</summary>
public sealed record ExperimentKnobView(string Kind, string Name, string BaselineValue, string CandidateValue);

/// <summary>The replay time of a finished job, as it is printed (spec P8-18 criterion 10): measured on this machine, with the instrument count and whether other heavy jobs ran.</summary>
/// <param name="Text">The sentence.</param>
/// <param name="TargetStatus"><c>met</c> or <c>not_met</c> only at exactly 214 instruments, else <c>not_comparable</c>.</param>
public sealed record ExperimentTimingView(string Text, string TargetStatus);

/// <summary>A replay job as the screen shows it (spec P8-18 Part B).</summary>
public sealed record ExperimentJobView(
    Guid Id,
    string State,
    string? Stage,
    int Done,
    int Total,
    string BaselineRunKey,
    ExperimentKnobView Knob,
    string? CandidateRunKey,
    long? ExperimentId,
    string? ErrorCode,
    string? ErrorText,
    DateTimeOffset QueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    long? ReplayMs,
    long? CompareMs,
    int Instruments,
    bool OtherHeavyJobsSeen,
    bool CancelRequested,
    ExperimentTimingView? Timing,
    IReadOnlyList<string> Limits);

/// <summary>The job started, and whether a new one was written.</summary>
/// <param name="Created">True when a new job was queued; false when the identical input already has a job.</param>
/// <param name="Job">The job.</param>
public sealed record StartedExperimentJob(bool Created, ExperimentJobView Job);

/// <summary>The jobs, newest first.</summary>
public sealed record ExperimentJobList(IReadOnlyList<ExperimentJobView> Items);

internal static class ExperimentJobViews
{
    /// <summary>The target of the P6 plan: a replay and comparison under ten minutes for 214 instruments.</summary>
    public const int TargetInstruments = 214;

    public static readonly TimeSpan Target = TimeSpan.FromMinutes(10);

    /// <summary>The last word of every result card.</summary>
    public const string Nothing = "Nothing was changed. Applying a change is your own separate act.";

    public static ExperimentJobView Of(ExperimentJob job, StoredRunHeader? baseline)
    {
        ArgumentNullException.ThrowIfNull(job);
        var limits = new List<string> { Nothing };
        if (job.State is ExperimentJobState.Failed or ExperimentJobState.Cancelled)
        {
            limits.Add("Judged nothing; not counted as a trial.");
        }
        else if (job.State == ExperimentJobState.Interrupted)
        {
            // the host stopped at an unknown point: the candidate run may be stored and the judgement may already be on the ledger
            limits.Add("The job was interrupted at an unknown point and is never resumed. A candidate run may have been stored and an experiment may be on the ledger: check the Experiments list; an experiment there counts as a trial.");
        }

        return new ExperimentJobView(
            job.Id,
            ExperimentJobStates.Code(job.State),
            job.Stage,
            job.Done,
            job.Total,
            job.BaselineRunKey,
            new ExperimentKnobView(job.KnobKind, job.KnobName, job.BaselineValue, job.CandidateValue),
            job.CandidateRunKey,
            job.ExperimentId,
            job.ErrorCode,
            job.ErrorText,
            job.CreatedAt,
            job.StartedAt,
            job.FinishedAt,
            job.ReplayMs,
            job.CompareMs,
            job.Instruments,
            job.OtherHeavyJobsSeen,
            job.CancelRequested,
            Timing(job, baseline),
            limits);
    }

    private static ExperimentTimingView? Timing(ExperimentJob job, StoredRunHeader? baseline)
    {
        if (job.ReplayMs is not { } ms)
        {
            return null;
        }

        var c = CultureInfo.InvariantCulture;
        var span = TimeSpan.FromMilliseconds(ms);
        var window = baseline is null ? string.Empty : string.Create(c, $" over {baseline.From:yyyy-MM-dd} to {baseline.To:yyyy-MM-dd}");
        var text = string.Create(c, $"Replay {(int)span.TotalMinutes}m {span.Seconds}s for {job.Instruments} instruments{window}, measured on this machine; other heavy jobs were running: {(job.OtherHeavyJobsSeen ? "yes" : "no")}.");
        var status = job.Instruments != TargetInstruments ? "not_comparable" : span < Target ? "met" : "not_met";
        text += status == "not_comparable"
            ? string.Create(c, $" The 10-minute target is for {TargetInstruments} instruments: not comparable with {job.Instruments}.")
            : status == "met" ? " The 10-minute target for 214 instruments: met." : " The 10-minute target for 214 instruments: not met.";
        return new ExperimentTimingView(text, status);
    }
}
