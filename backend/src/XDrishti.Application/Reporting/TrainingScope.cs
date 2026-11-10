using XDrishti.Application.Abstractions.SimAll;

namespace XDrishti.Application.Reporting;

/// <summary>
/// The range a training report may read and what it says about the rest (spec AU-14, acceptance 4): the asked range cut to the training window, which ends before the hold-out block. Nothing past the window end is ever asked of the store, so no number from the block can appear.
/// </summary>
/// <param name="Job">The job.</param>
/// <param name="Counts">Its chunk counts.</param>
/// <param name="From">First entry date read.</param>
/// <param name="To">Last entry date read.</param>
/// <param name="Cut">The asked range reached past the window.</param>
/// <param name="Locked">The sentence that says the rest is locked; null when the range did not reach past the window.</param>
internal sealed record TrainingScope(SimAllJob Job, SimAllCounts Counts, DateOnly From, DateOnly To, bool Cut, string? Locked)
{
    /// <summary>True when the asked range holds none of the window: the answer is empty, and nothing is read.</summary>
    public bool Nothing => From > To;

    /// <summary>The scope of a job for the asked dates (either may be null).</summary>
    public static TrainingScope Of(SimAllSnapshot progress, DateOnly? askedFrom, DateOnly? askedTo)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var job = progress.Job;
        var windowEnd = job.WindowTo < job.HoldOutStart ? job.WindowTo : job.HoldOutStart.AddDays(-1);
        var from = askedFrom is { } f && f > job.WindowFrom ? f : job.WindowFrom;
        var to = askedTo is { } t && t < windowEnd ? t : windowEnd;
        var cut = askedTo > windowEnd || askedFrom > windowEnd;
        var scope = new TrainingScope(job, progress.Counts, from, to, cut, null);
        return cut
            ? scope with { Locked = $"The asked range reaches past {windowEnd:yyyy-MM-dd}, the end of the training window: from {job.HoldOutStart:yyyy-MM-dd} the hold-out block is locked and no number from it is read. This answer is the part {(scope.Nothing ? "before it, which is empty" : $"up to {to:yyyy-MM-dd}")}." }
            : scope;
    }

    /// <summary>The completeness block for the read at <paramref name="asOf"/>.</summary>
    public TrainingCompletenessView Completeness(DateTimeOffset asOf, IReadOnlyList<string> symbolsWithoutResults) => new(
        TrainingCompletenessView.TrainingKind, TrainingCompletenessView.TrainingLabel, Job.Id, Job.State,
        Counts.Pending == 0 && Counts.Running == 0 && Counts.Failed == 0 && Job.State == "completed",
        Counts.Done, Counts.Total, Counts.Failed, Counts.Running, Counts.Pending, asOf, Job.WindowFrom, Job.WindowTo, Job.HoldOutStart,
        Nothing ? null : From, Nothing ? null : To, Cut, Locked, symbolsWithoutResults, $"/api/simulate-all/{Job.Id:D}/training");
}
