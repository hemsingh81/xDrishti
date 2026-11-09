#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using NSubstitute;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Platform.Services;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Tests.Platform.Services;

/// <summary>The guard the nightly, shadow grading and paper capture ask (spec AU-03): a partial newest end-of-day item with a still-missing session is a reason to wait, proved again live.</summary>
public sealed class RecoveryDataGuardTests
{
    private static readonly DateOnly Friday = new(2026, 10, 9);
    private static readonly DateOnly Thursday = new(2026, 10, 8);

    private readonly InMemoryRecoveryLedger _ledger = new();
    private readonly IRecoveryProof _proof = Substitute.For<IRecoveryProof>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private RecoveryDataGuard Guard() => new(_ledger, [_proof]);

    private async Task StoreAsync(string state, params (DateOnly Day, string State)[] sessions)
    {
        var pass = await _ledger.BeginPassAsync("startup", DateTimeOffset.UtcNow, Ct);
        var detail = RecoveryProofDetail.Write([.. sessions.Select(s => new SessionProof(s.Day, s.State, 1, 0, 0, 1))], true, DateTimeOffset.UtcNow);
        await _ledger.SaveAsync(new RecoveryItemRow(Guid.NewGuid(), pass, "eod", "2026-10-09", Thursday, Friday, state, null, 1, null, detail), Ct);
    }

    private void ProofSays(params (DateOnly Day, string State)[] sessions) =>
        _proof.ProveAsync(Arg.Any<IReadOnlyList<DateOnly>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(new RecoveryProofResult(
                sessions.All(x => x.State == SessionProofStates.Recovered),
                RecoveryProofDetail.Write([.. sessions.Select(x => new SessionProof(x.Day, x.State, 1, 0, 0, 1))], true, DateTimeOffset.UtcNow),
                null)));

    public RecoveryDataGuardTests() => _proof.ServiceId.Returns("eod");

    [Fact]
    public async Task Without_an_end_of_day_item_or_with_a_succeeded_one_nothing_is_in_doubt()
    {
        (await Guard().WhyUnprovenAsync(null, Ct)).ShouldBeNull();

        await StoreAsync(RecoveryStates.Succeeded, (Friday, SessionProofStates.Recovered));

        (await Guard().WhyUnprovenAsync(null, Ct)).ShouldBeNull();
        await _proof.DidNotReceiveWithAnyArgs().ProveAsync(default!, default, Ct);
    }

    [Fact]
    public async Task A_partial_item_with_a_session_still_missing_gives_a_reason_naming_the_date_but_only_for_sessions_from_the_asked_one()
    {
        await StoreAsync(RecoveryStates.Partial, (Thursday, SessionProofStates.StillMissing), (Friday, SessionProofStates.Recovered));
        ProofSays((Thursday, SessionProofStates.StillMissing), (Friday, SessionProofStates.Recovered));

        (await Guard().WhyUnprovenAsync(null, Ct)).ShouldNotBeNull().ShouldContain("2026-10-08");
        (await Guard().WhyUnprovenAsync(Friday, Ct)).ShouldBeNull(); // Thursday is not read
    }

    [Fact]
    public async Task A_partial_item_the_data_has_since_filled_is_no_reason_to_wait_and_an_unfillable_session_never_waits()
    {
        await StoreAsync(RecoveryStates.Partial, (Thursday, SessionProofStates.StillMissing), (Friday, SessionProofStates.Unfillable));
        ProofSays((Thursday, SessionProofStates.Recovered), (Friday, SessionProofStates.Unfillable));

        (await Guard().WhyUnprovenAsync(null, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task A_proof_that_cannot_be_read_again_is_a_reason_fail_closed()
    {
        await StoreAsync(RecoveryStates.Partial, (Thursday, SessionProofStates.StillMissing));
        _proof.ProveAsync(Arg.Any<IReadOnlyList<DateOnly>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns<ValueTask<RecoveryProofResult>>(_ => throw new InvalidOperationException("down"));

        (await Guard().WhyUnprovenAsync(null, Ct)).ShouldNotBeNull().ShouldContain("could not be proved");
    }
}
