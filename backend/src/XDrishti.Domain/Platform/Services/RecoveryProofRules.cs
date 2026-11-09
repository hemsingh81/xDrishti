using XDrishti.Domain.Market.Bars;

namespace XDrishti.Domain.Platform.Services;

/// <summary>The states of one missed session after a recovery step (ADR 0240, spec AU-03). Codes as stored in the item detail.</summary>
public static class SessionProofStates
{
    public const string Recovered = "recovered";
    public const string StillMissing = "still_missing";
    public const string Unfillable = "unfillable";
}

/// <summary>What one instrument has of one session: all its bars (<see cref="Complete"/>), a gap record that says no more can be had (<see cref="Unfillable"/>), or bars still missing or not aggregated (<see cref="Missing"/>).</summary>
public enum InstrumentSessionState
{
    Complete,
    Unfillable,
    Missing,
}

/// <summary>The proof of one session: its state and how the instruments stand.</summary>
public sealed record SessionProof(DateOnly Date, string State, int Instruments, int Complete, int Unfillable, int Missing);

/// <summary>
/// Decides, from what is stored, whether a missed session was really made up (spec AU-03). Pure. A run that reports success proves nothing: the evidence is the 1-minute bar count against the calendar's expected count,
/// the gap record, the aggregates being built and the quality run; every doubt reads as not recovered (fail closed).
/// </summary>
public static class RecoveryProofRules
{
    /// <summary>
    /// One instrument in one session. An <c>unfillable</c> gap record wins even when the count now reaches the expectation (a stale record is corrected by the next gap check; until then the session is not reported as recovered,
    /// AC3); otherwise the day is complete only with every expected bar stored and its aggregates built.
    /// </summary>
    public static InstrumentSessionState Instrument(int expected, int found, GapStatus? gap, bool aggregatesStale)
    {
        if (gap == GapStatus.Unfillable)
        {
            return InstrumentSessionState.Unfillable;
        }

        return BarGap.IsComplete(expected, found) && !aggregatesStale ? InstrumentSessionState.Complete : InstrumentSessionState.Missing;
    }

    /// <summary>A session is <c>recovered</c> only when there is evidence (at least one instrument), none is missing, the quality run of the step succeeded and none is unfillable; unfillable beats recovered, missing beats both.</summary>
    public static SessionProof Session(DateOnly date, IReadOnlyCollection<InstrumentSessionState> states, bool qualityVerified)
    {
        ArgumentNullException.ThrowIfNull(states);
        var complete = states.Count(s => s == InstrumentSessionState.Complete);
        var unfillable = states.Count(s => s == InstrumentSessionState.Unfillable);
        var missing = states.Count(s => s == InstrumentSessionState.Missing);
        var state = states.Count == 0 || missing > 0 || !qualityVerified
            ? SessionProofStates.StillMissing
            : unfillable > 0 ? SessionProofStates.Unfillable : SessionProofStates.Recovered;
        return new SessionProof(date, state, states.Count, complete, unfillable, missing);
    }

    /// <summary>Whether every session of the step was recovered.</summary>
    public static bool AllRecovered(IReadOnlyCollection<SessionProof> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        return sessions.Count > 0 && sessions.All(s => s.State == SessionProofStates.Recovered);
    }
}
