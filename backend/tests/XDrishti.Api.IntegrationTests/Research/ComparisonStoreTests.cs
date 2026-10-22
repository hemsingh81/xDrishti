using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// Criterion 8 of P4-05 over the real adapter on a real TimescaleDB: a comparison is the header of runs that are stored on their own. The same comparison again is "already stored", another record under its key is <c>store.run_conflict</c>, a
/// comparison whose member is not stored is refused, and the members join on <c>trade_key</c> through the entries key.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ComparisonStoreTests(ApiFactory factory)
{
    private const string Entries = "e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly ExitCandidate Two = new("FIXED_RR", [new ExitParameterValue("rr", [2])]);

    private static readonly ExitCandidate Three = new("FIXED_RR", [new ExitParameterValue("rr", [3])]);

    /// <summary>Two runs of the same trades whose window ends on different days, so that their run keys differ and their trade keys are the same: the shape of two candidates of one comparison.</summary>
    private static (SimulationRun A, SimulationRun B) Runs() => (StoredRunFixtures.Run(to: RunStoreRoundTripTests.NextTo()), StoredRunFixtures.Run(to: RunStoreRoundTripTests.NextTo()));

    private static ComparisonRun Comparison(SimulationRun a, SimulationRun b, string entries = Entries) =>
        new([new CandidateRun("FIXED_RR{rr=2}", Two, a, 0), new CandidateRun("FIXED_RR{rr=3}", Three, b, 3)], ComparisonReport.Build([new CandidateRun("FIXED_RR{rr=2}", Two, a, 0), new CandidateRun("FIXED_RR{rr=3}", Three, b, 3)], true), entries);

    private async Task<T> WithScope<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    private Task<bool> StoreRunsAsync(params SimulationRun[] runs) =>
        WithScope(async provider =>
        {
            var handler = provider.GetRequiredService<ICommandHandler<StoreRunCommand, StoreOutcome>>();
            foreach (var run in runs)
            {
                (await handler.HandleAsync(new StoreRunCommand(run, StoreKit.Record(run), SimulateStore.Actor), Ct)).IsSuccess.ShouldBeTrue();
            }

            return true;
        });

    private Task<Result<StoreOutcome>> StoreComparisonAsync(ComparisonRun comparison, ComparisonRecordText? record = null) =>
        WithScope(provider => provider.GetRequiredService<ICommandHandler<StoreComparisonCommand, StoreOutcome>>()
            .HandleAsync(new StoreComparisonCommand(comparison, record ?? SimulateStore.ComparisonRecord(comparison.Report), SimulateStore.Actor), Ct).AsTask());

    [Fact]
    public async Task A_comparison_of_stored_runs_is_stored_once_and_the_same_comparison_again_is_already_stored()
    {
        var (a, b) = Runs();
        await StoreRunsAsync(a, b);
        var comparison = Comparison(a, b);

        var first = await StoreComparisonAsync(comparison);
        var second = await StoreComparisonAsync(comparison);
        var header = await WithScope(provider => provider.GetRequiredService<IRunStore>().FindComparisonAsync(comparison.ComparisonKey, Ct).AsTask());

        first.IsSuccess.ShouldBeTrue(first.IsFailure ? first.Error.Message : string.Empty);
        first.Value.WasAlreadyStored.ShouldBeFalse();
        second.Value.WasAlreadyStored.ShouldBeTrue();
        second.Value.Id.ShouldBe(first.Value.Id);
        header.ShouldNotBeNull();
        header.EntriesKey.ShouldBe(Entries);
        header.Members.ShouldBe([new ComparisonMemberToStore(a.RunKey, "FIXED_RR{rr=2}", 0), new ComparisonMemberToStore(b.RunKey, "FIXED_RR{rr=3}", 3)]);
        (header.StrategyId, header.Unit, header.CommonEntries, header.CreatedBy).ShouldBe(("ORB", "RFilled", comparison.Report.CommonEntries, SimulateStore.Actor));
        (await StoreKit.CountAsync(factory.AdminConnectionString, "SELECT count(*) FROM research.sim_comparisons WHERE comparison_key = @k", Ct, ("k", comparison.ComparisonKey))).ShouldBe(1);
    }

    [Fact]
    public async Task Another_record_under_a_stored_comparison_key_is_a_conflict_and_changes_nothing()
    {
        var (a, b) = Runs();
        await StoreRunsAsync(a, b);
        var comparison = Comparison(a, b);
        (await StoreComparisonAsync(comparison)).IsSuccess.ShouldBeTrue();
        await StoreKit.ExecuteAsync(factory.AdminConnectionString, "UPDATE research.sim_comparisons SET record_sha256 = repeat('f', 64) WHERE comparison_key = @k", Ct, ("k", comparison.ComparisonKey));

        var again = await StoreComparisonAsync(comparison);

        again.IsFailure.ShouldBeTrue();
        again.Error.Code.ShouldBe(StoreErrors.RunConflictCode);
        again.Error.Fields!.Select(f => f.Field).ShouldContain("record");
        (await StoreKit.CountAsync(factory.AdminConnectionString, "SELECT count(*) FROM research.sim_comparisons WHERE comparison_key = @k", Ct, ("k", comparison.ComparisonKey))).ShouldBe(1);
    }

    [Fact]
    public async Task A_comparison_whose_member_is_not_stored_is_refused_and_writes_nothing()
    {
        var (a, b) = Runs();
        await StoreRunsAsync(a);
        var comparison = Comparison(a, b);

        var result = await StoreComparisonAsync(comparison);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(StoreErrors.WriteFailedCode);
        (await StoreKit.CountAsync(factory.AdminConnectionString, "SELECT count(*) FROM research.sim_comparisons WHERE comparison_key = @k", Ct, ("k", comparison.ComparisonKey))).ShouldBe(0);
    }

    [Fact]
    public async Task The_members_of_a_stored_comparison_join_on_the_trade_key()
    {
        var (a, b) = Runs();
        await StoreRunsAsync(a, b);
        var comparison = Comparison(a, b);
        (await StoreComparisonAsync(comparison)).IsSuccess.ShouldBeTrue();

        var joined = await StoreKit.CountAsync(
            factory.AdminConnectionString,
            """
            SELECT count(*) FROM research.sim_comparisons c
            JOIN research.sim_runs ra ON ra.run_key = c.member_run_keys[1]
            JOIN research.sim_runs rb ON rb.run_key = c.member_run_keys[2]
            JOIN research.sim_trades ta ON ta.run_id = ra.id AND ta.open_at_end = false
            JOIN research.sim_trades tb ON tb.run_id = rb.id AND tb.open_at_end = false AND tb.trade_key = ta.trade_key
            WHERE c.comparison_key = @k
            """,
            Ct,
            ("k", comparison.ComparisonKey));

        joined.ShouldBe(a.Trades.Count);
        (await StoreKit.CountAsync(factory.AdminConnectionString, "SELECT count(*) FROM research.sim_comparisons WHERE entries_key = @e", Ct, ("e", Entries))).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_comparison_that_names_fewer_than_two_members_or_a_bad_key_is_refused_before_the_database()
    {
        var (a, b) = Runs();
        var one = Comparison(a, b) with { Candidates = [new CandidateRun("x", Two, a, 0)] };
        var badKey = Comparison(a, b, "not-a-key");

        var few = await StoreComparisonAsync(one);
        var key = await StoreComparisonAsync(badKey);

        few.Error.Code.ShouldBe(StoreErrors.RunInvalidCode);
        few.Error.Fields!.Select(f => f.Field).ShouldContain("comparison.candidates");
        key.Error.Code.ShouldBe(StoreErrors.RunInvalidCode);
        key.Error.Fields!.Select(f => f.Field).ShouldContain("comparison.keys");
    }
}
