using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Domain.Tests.Platform.Services;

/// <summary>The session classifier of the recovery proof (spec AU-03, ADR 0240).</summary>
public sealed class RecoveryProofRulesTests
{
    private static readonly DateOnly Day = new(2026, 10, 9);

    [Theory]
    [InlineData(375, 375, null, false, InstrumentSessionState.Complete)]
    [InlineData(375, 375, GapStatus.Filled, false, InstrumentSessionState.Complete)]
    [InlineData(375, 370, null, false, InstrumentSessionState.Missing)]
    [InlineData(375, 0, null, false, InstrumentSessionState.Missing)]
    [InlineData(375, 200, GapStatus.Open, false, InstrumentSessionState.Missing)]
    [InlineData(375, 375, null, true, InstrumentSessionState.Missing)] // bars there, aggregates not built
    [InlineData(375, 200, GapStatus.Unfillable, false, InstrumentSessionState.Unfillable)]
    [InlineData(375, 375, GapStatus.Unfillable, false, InstrumentSessionState.Unfillable)] // a stale unfillable record is never read as recovered
    public void An_instrument_is_complete_only_with_every_bar_and_its_aggregates_and_unfillable_wins_over_a_count(int expected, int found, GapStatus? gap, bool stale, InstrumentSessionState state)
    {
        RecoveryProofRules.Instrument(expected, found, gap, stale, 1).ShouldBe(state);
    }

    [Theory]
    [InlineData(375, 372, 1, InstrumentSessionState.Complete)] // 3 minutes of 375 are 1 %
    [InlineData(375, 371, 1, InstrumentSessionState.Missing)]
    [InlineData(375, 374, 0, InstrumentSessionState.Missing)]
    public void An_instrument_short_by_no_more_than_the_tolerance_is_complete(int expected, int found, double tolerance, InstrumentSessionState state)
    {
        RecoveryProofRules.Instrument(expected, found, null, false, tolerance).ShouldBe(state);
    }

    [Fact]
    public void A_session_of_complete_instruments_with_a_verified_quality_run_is_recovered()
    {
        var proof = RecoveryProofRules.Session(Day, [InstrumentSessionState.Complete, InstrumentSessionState.Complete], true);

        (proof.State, proof.Instruments, proof.Complete, proof.Missing).ShouldBe((SessionProofStates.Recovered, 2, 2, 0));
    }

    [Fact]
    public void One_missing_instrument_makes_the_session_still_missing_even_beside_an_unfillable_one()
    {
        var proof = RecoveryProofRules.Session(Day, [InstrumentSessionState.Complete, InstrumentSessionState.Unfillable, InstrumentSessionState.Missing], true);

        (proof.State, proof.Complete, proof.Unfillable, proof.Missing).ShouldBe((SessionProofStates.StillMissing, 1, 1, 1));
    }

    [Fact]
    public void Unfillable_instruments_are_served_beside_more_complete_ones_but_a_session_they_do_not_trail_is_unfillable_never_recovered()
    {
        RecoveryProofRules.Session(Day, [InstrumentSessionState.Complete, InstrumentSessionState.Complete, InstrumentSessionState.Unfillable], true).State.ShouldBe(SessionProofStates.Recovered);
        RecoveryProofRules.Session(Day, [InstrumentSessionState.Complete, InstrumentSessionState.Unfillable], true).State.ShouldBe(SessionProofStates.Unfillable);
        RecoveryProofRules.Session(Day, [InstrumentSessionState.Unfillable], true).State.ShouldBe(SessionProofStates.Unfillable);
    }

    [Fact]
    public void A_session_without_evidence_or_without_a_verified_quality_run_is_still_missing()
    {
        RecoveryProofRules.Session(Day, [], true).State.ShouldBe(SessionProofStates.StillMissing);
        RecoveryProofRules.Session(Day, [InstrumentSessionState.Complete], false).State.ShouldBe(SessionProofStates.StillMissing);
    }

    [Fact]
    public void Only_a_step_whose_sessions_are_all_recovered_is_all_recovered()
    {
        var ok = new SessionProof(Day, SessionProofStates.Recovered, 1, 1, 0, 0);
        var bad = ok with { State = SessionProofStates.StillMissing };

        RecoveryProofRules.AllRecovered([ok, ok]).ShouldBeTrue();
        RecoveryProofRules.AllRecovered([ok, bad]).ShouldBeFalse();
        RecoveryProofRules.AllRecovered([]).ShouldBeFalse();
    }
}
