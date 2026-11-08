#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Application.Confidence;

namespace XDrishti.Application.Tests.Confidence;

/// <summary>CF-11 row 15 (SC-S4a, ADR 0226): the owner's verify-pack --gate record feeds the P3-09 term; only a PASS over 20 trades passes; none or unreadable is unavailable.</summary>
public sealed class PackGateResultTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PackGateResultRow Row(string status, int trades = 20) => new(7, "pack-1", status, trades, DateTimeOffset.UnixEpoch);

    [Theory]
    [InlineData("PASS", 20, true)]
    [InlineData("PASS", 19, false)]
    [InlineData("PASS_WITH_DATA_FINDINGS", 30, false)]
    [InlineData("RERUN_NEEDED", 30, false)]
    [InlineData("DATA_REPAIR_NEEDED", 30, false)]
    [InlineData("BLOCKED_DEFECT", 30, false)]
    public async Task Only_a_pass_over_twenty_trades_is_a_pass(string status, int trades, bool expected)
    {
        var f = new ReadinessFixture { PackGateRow = Row(status, trades) };

        var reading = await f.PackGate.ReadAsync("ORB", 1, Ct);

        reading.Value.ShouldBe(expected);
    }

    [Fact]
    public async Task No_record_no_version_and_a_failed_read_are_unavailable()
    {
        (await new ReadinessFixture { PackGateRow = null }.PackGate.ReadAsync("ORB", 1, Ct)).Value.ShouldBeNull();
        (await new ReadinessFixture().PackGate.ReadAsync("ORB", null, Ct)).Value.ShouldBeNull();
        var broken = await new ReadinessFixture { PackGateFailure = new InvalidOperationException("down") }.PackGate.ReadAsync("ORB", 1, Ct);
        broken.Value.ShouldBeNull();
        broken.Reason.ShouldStartWith("unavailable");
    }

    [Fact]
    public async Task A_cancelled_read_is_not_swallowed()
    {
        var f = new ReadinessFixture { PackGateFailure = new OperationCanceledException() };

        await Should.ThrowAsync<OperationCanceledException>(async () => await f.PackGate.ReadAsync("ORB", 1, Ct));
    }

    [Fact]
    public async Task Row_15_follows_the_record_on_the_card()
    {
        var pass = await new ReadinessFixture().Reader.BuildAsync("ORB", 1, Ct);
        var fail = await new ReadinessFixture { PackGateRow = Row("RERUN_NEEDED") }.Reader.BuildAsync("ORB", 1, Ct);
        var none = await new ReadinessFixture { PackGateRow = null }.Reader.BuildAsync("ORB", 1, Ct);

        pass.Rows.Single(r => r.Number == 15).Status.ShouldBe("pass");
        fail.Rows.Single(r => r.Number == 15).Status.ShouldBe("fail");
        none.Rows.Single(r => r.Number == 15).Status.ShouldBe("unavailable");
    }

    private static (RecordPackGateResultHandler Handler, IPackGateResultStore Store) Handler(ReadinessFixture f)
    {
        var store = Substitute.For<IPackGateResultStore>();
        store.AppendAsync(default!, 0, default!, default!, 0, default!, default!, default).ReturnsForAnyArgs(ValueTask.FromResult(5L));
        return (new RecordPackGateResultHandler(store, f.StrategyDetails, NullLogger<RecordPackGateResultHandler>.Instance), store);
    }

    private static RecordPackGateResultCommand Command(string actor = "owner:me@x.test", string strategy = "ORB", int version = 1, string pack = "pack-1", string status = "PASS", int trades = 20, string reason = "checked by hand") =>
        new(actor, strategy, version, pack, status, trades, reason);

    [Fact]
    public async Task The_owner_records_a_pass_and_it_counts()
    {
        var (handler, store) = Handler(new ReadinessFixture());

        var result = await handler.HandleAsync(Command(), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new PackGateRecorded(5, "PASS", true));
        await store.Received(1).AppendAsync("ORB", 1, "pack-1", "PASS", 20, "owner:me@x.test", "checked by hand", Ct);
    }

    [Fact]
    public async Task A_short_pass_is_stored_but_does_not_count()
    {
        var (handler, _) = Handler(new ReadinessFixture());

        (await handler.HandleAsync(Command(trades: 5), Ct)).Value.CountsAsPass.ShouldBeFalse();
    }

    [Theory]
    [InlineData("auto", "ORB", 1, "pack-1", "PASS", 20, "why", "confidence.owner_only")]
    [InlineData("owner:me@x.test", "ORB", 1, "pack-1", "PASS", 20, " ", "confidence.reason_invalid")]
    [InlineData("owner:me@x.test", "ORB", 1, "", "PASS", 20, "why", "confidence.readiness_invalid")]
    [InlineData("owner:me@x.test", "ORB", 1, "pack-1", "pass", 20, "why", "confidence.readiness_invalid")]
    [InlineData("owner:me@x.test", "ORB", 1, "pack-1", "PASS", -1, "why", "confidence.readiness_invalid")]
    [InlineData("owner:me@x.test", "ORB", 0, "pack-1", "PASS", 20, "why", "confidence.readiness_invalid")]
    [InlineData("owner:me@x.test", "NOPE", 1, "pack-1", "PASS", 20, "why", "confidence.scorecard_not_found")]
    [InlineData("owner:me@x.test", "ORB", 2, "pack-1", "PASS", 20, "why", "confidence.scorecard_not_found")]
    public async Task A_bad_record_is_refused_and_nothing_is_written(string actor, string strategy, int version, string pack, string status, int trades, string reason, string code)
    {
        var (handler, store) = Handler(new ReadinessFixture());

        var result = await handler.HandleAsync(Command(actor, strategy, version, pack, status, trades, reason), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
        await store.DidNotReceiveWithAnyArgs().AppendAsync(default!, 0, default!, default!, 0, default!, default!, Ct);
    }
}
