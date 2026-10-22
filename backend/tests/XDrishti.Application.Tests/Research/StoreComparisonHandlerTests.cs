using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Research;

/// <summary>P4-05, D10 at the use case: a comparison is validated without the database, the instant is read once, the store is given the members in order, and a conflict or a failure is logged by code and returned.</summary>
public sealed class StoreComparisonHandlerTests
{
    private static readonly string Hex = new('a', 64);

    private static readonly string Other = new('b', 64);

    private static readonly ComparisonRecordText Record = new("a,b\n", "c,d\n", Hex);

    private static readonly ExitCandidate Two = new("FIXED_RR", [new ExitParameterValue("rr", [2])]);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ComparisonRun Comparison(string entries, params (string Id, string RunKey, long Gated)[] members)
    {
        var candidates = members.Select(m => new CandidateRun(m.Id, Two, FabricatedRun.Standard() with { RunKey = m.RunKey }, m.Gated)).ToList();
        return new ComparisonRun(candidates, ComparisonReport.Build(candidates, true), entries);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Store = Substitute.For<IRunStore>();
            Store.StoreComparisonAsync(Arg.Any<ComparisonToStore>(), Arg.Any<CancellationToken>()).Returns(Result.Success(StoreOutcome.Stored(9, 0)));
            Time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 22, 5, 0, 0, TimeSpan.Zero));
            Log = new StructuredLogger<StoreComparisonHandler>();
            Handler = new StoreComparisonHandler(Store, Time, Log);
        }

        public IRunStore Store { get; }

        public FakeTimeProvider Time { get; }

        public StructuredLogger<StoreComparisonHandler> Log { get; }

        public StoreComparisonHandler Handler { get; }

        public ValueTask<Result<StoreOutcome>> RunAsync(ComparisonRun? comparison, string actor = "cli:simulate") => Handler.HandleAsync(new StoreComparisonCommand(comparison, Record, actor), Ct);
    }

    [Fact]
    public async Task The_store_is_given_the_members_in_order_with_their_gated_counts_the_keys_and_the_instant_read_once()
    {
        var fixture = new Fixture();
        var comparison = Comparison(Hex, ("FIXED_RR{rr=2}", Hex, 0), ("FIXED_RR{rr=3}", Other, 4));

        var result = await fixture.RunAsync(comparison);

        result.IsSuccess.ShouldBeTrue();
        await fixture.Store.Received(1).StoreComparisonAsync(
            Arg.Is<ComparisonToStore>(c =>
                c.ComparisonKey == comparison.ComparisonKey && c.EntriesKey == Hex && c.StrategyId == "ORB" && c.Unit == "RFilled" && c.Actor == "cli:simulate" && c.CreatedAt == fixture.Time.GetUtcNow()
                && c.Members.Count == 2 && c.Members[0] == new ComparisonMemberToStore(Hex, "FIXED_RR{rr=2}", 0) && c.Members[1] == new ComparisonMemberToStore(Other, "FIXED_RR{rr=3}", 4)),
            Ct);
    }

    [Fact]
    public async Task A_comparison_that_cannot_be_kept_is_refused_before_the_store_is_asked()
    {
        var fixture = new Fixture();

        var missing = await fixture.RunAsync(null);
        var one = await fixture.RunAsync(Comparison(Hex, ("only", Hex, 0)));
        var badKey = await fixture.RunAsync(Comparison("nope", ("a", Hex, 0), ("b", Other, 0)));
        var twice = await fixture.RunAsync(Comparison(Hex, ("a", Hex, 0), ("b", Hex, 0)));
        var negative = await fixture.RunAsync(Comparison(Hex, ("a", Hex, 0), ("b", Other, -1)));
        var actor = await fixture.RunAsync(Comparison(Hex, ("a", Hex, 0), ("b", Other, 0)), " ");

        foreach (var (result, field) in new[] { (missing, "comparison"), (one, "comparison.candidates"), (badKey, "comparison.keys"), (twice, "comparison.candidates"), (negative, "comparison.candidates.gatedBlocked"), (actor, "actor") })
        {
            result.Error.Code.ShouldBe(StoreErrors.RunInvalidCode);
            result.Error.Fields!.Select(f => f.Field).ShouldContain(field);
        }

        await fixture.Store.DidNotReceiveWithAnyArgs().StoreComparisonAsync(default!, Ct);
    }

    [Fact]
    public async Task A_conflict_is_logged_with_the_figures_that_differ_and_returned_as_it_is()
    {
        var fixture = new Fixture();
        fixture.Store.StoreComparisonAsync(Arg.Any<ComparisonToStore>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<StoreOutcome>(StoreErrors.RunConflict(Hex, ["record", "members"])));

        var result = await fixture.RunAsync(Comparison(Hex, ("a", Hex, 0), ("b", Other, 0)));

        result.Error.Code.ShouldBe(StoreErrors.RunConflictCode);
        fixture.Log.Events.ShouldContain(e => e.Id == 4426 && e.Message.Contains("record, members", StringComparison.Ordinal));
        fixture.Log.Events.ShouldNotContain(e => e.Message.Contains("a,b", StringComparison.Ordinal), "the table is never logged");
    }

    [Fact]
    public async Task An_exception_is_logged_by_code_and_raised_again()
    {
        var fixture = new Fixture();
        fixture.Store.StoreComparisonAsync(Arg.Any<ComparisonToStore>(), Arg.Any<CancellationToken>()).Returns<Result<StoreOutcome>>(_ => throw new InvalidOperationException("boom"));

        await Should.ThrowAsync<InvalidOperationException>(async () => await fixture.RunAsync(Comparison(Hex, ("a", Hex, 0), ("b", Other, 0))));

        fixture.Log.Events.ShouldContain(e => e.Id == 4427 && e.Message.Contains(StoreErrors.WriteFailedCode, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_stored_comparison_is_logged_as_completed_with_the_count_of_its_members()
    {
        var fixture = new Fixture();

        await fixture.RunAsync(Comparison(Hex, ("a", Hex, 0), ("b", Other, 0)));

        fixture.Log.Events.ShouldContain(e => e.Id == 4425 && e.Message.Contains("2 members, stored", StringComparison.Ordinal));
    }
}
