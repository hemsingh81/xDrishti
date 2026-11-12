using XDrishti.Application.Abstractions.Market.History;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.History;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Market.History;

/// <summary>
/// What the admission rule said about one instrument. <see cref="HasSeamRow"/> is false for an instrument with no seam row (it has no pre-seam bars to admit or exclude and is not counted) and for a switch that is off. <see cref="Reason"/> is the first failing reason
/// (<see cref="SeamVerification"/>), null when the instrument is verified; <see cref="Admitted"/> is then set.
/// </summary>
public sealed record PreSeamDecision(bool HasSeamRow, string? Reason, PreSeamRecord? Admitted)
{
    public bool IsAdmitted => Admitted is not null;

    /// <summary>No seam row, or the switch is off: nothing is admitted and nothing is counted.</summary>
    public static PreSeamDecision NotApplicable(string reason) => new(false, reason, null);
}

/// <summary>
/// Reads the stored facts and applies <see cref="SeamVerification.Decide"/> to one instrument (CF-04d). The one place that turns facts into a decision, shared by the run readers and by <c>xd data history</c>. Anything it cannot establish is a reason, never a pass.
/// </summary>
internal static class PreSeamAdmission
{
    public const string NoSeam = "no_seam";

    /// <param name="seams">The port that holds the facts.</param>
    /// <param name="instrumentId">The instrument.</param>
    /// <param name="factors">Its applied actions (plain mode).</param>
    /// <param name="csvAdjustedThrough">The date its loaded CSV files are adjusted through, when it has any.</param>
    /// <param name="forReport">The switch is reported as on, so the answer says what the instrument would be.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public static async ValueTask<PreSeamDecision> DecideAsync(IBackfillSeams seams, Guid instrumentId, AdjustmentFactors factors, DateOnly? csvAdjustedThrough, bool forReport, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seams);
        ArgumentNullException.ThrowIfNull(factors);
        var facts = await seams.AdmissionFactsAsync(instrumentId, forReport, cancellationToken) ?? SeamAdmissionFacts.Off;
        if (!facts.SwitchOn)
        {
            return PreSeamDecision.NotApplicable(SeamVerification.SwitchOff);
        }

        if (facts.Record is not { } record)
        {
            return PreSeamDecision.NotApplicable(NoSeam);
        }

        var seam = facts.CurrentSeam ?? record.SeamAt;
        var basis = SeamBasis.Of(seam, factors.Actions, csvAdjustedThrough);
        var reason = SeamVerification.Decide(Input(facts, record, basis, dhanInSpan: false));
        var seamDate = IndiaTime.Today(record.SeamAt);
        if (reason is null && LastInSpanExDate(factors, seamDate, csvAdjustedThrough) is { } last && await seams.HasDhanBarsAsync(instrumentId, Midnight(seamDate), Midnight(last), cancellationToken))
        {
            reason = SeamVerification.DhanInCsvSpan;
        }

        return new PreSeamDecision(true, reason, reason is null ? new PreSeamRecord(seamDate, basis) : null);
    }

    private static SeamVerificationInput Input(SeamAdmissionFacts facts, SeamRecord record, string basis, bool dhanInSpan) =>
        new(true, record.ProbeStatus, record.SeamAt, facts.CurrentSeam, facts.HighWater ?? DateTimeOffset.MaxValue, facts.UnfinishedChunks, record.ContinuityBasis, record.SeamJump, record.ProbeBasis, basis, dhanInSpan);

    // The last ex-date inside the CSV span (after the seam date, on or before the date the CSV is adjusted through): a Dhan bar dated before it would be raw and the CSV rule would leave it unadjusted.
    private static DateOnly? LastInSpanExDate(AdjustmentFactors factors, DateOnly seamDate, DateOnly? through)
    {
        if (through is not { } end)
        {
            return null;
        }

        DateOnly? last = null;
        foreach (var action in factors.Actions)
        {
            if (action.ExDate > seamDate && action.ExDate <= end && (last is null || action.ExDate > last))
            {
                last = action.ExDate;
            }
        }

        return last;
    }

    // an instant with offset 0: Npgsql writes a DateTimeOffset to timestamptz only so
    private static DateTimeOffset Midnight(DateOnly date) => new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).ToUniversalTime();
}
