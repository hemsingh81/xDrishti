#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market.Actions;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Market.History;
using XDrishti.Application.Tests.Market.Bars;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.History;

namespace XDrishti.Application.Tests.Market.History;

/// <summary>CF-04 Part B criteria 1, 9 and 10: the probe makes no request with the switch off, decides with data, and never writes a bar.</summary>
public sealed class ProbeSeamHandlerTests
{
    private static readonly DateTimeOffset Seam = new(2024, 4, 1, 9, 15, 0, IndiaTime.Offset);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Bar At(int minute, double close) => new(Seam.AddMinutes(minute).ToUnixTimeSeconds(), close, close + 1, close - 1, close, 10);

    private sealed class World
    {
        public World()
        {
            Targets.Targets.Add(new FetchTarget(Id, "ABC", "share", "1333", "NSE_EQ", null));
            Store.CurrentSeams[Id] = Seam;
            Bars.Stored[Id] = [At(0, 100), At(1, 100.1), At(2, 99.9)];
            Actions.ListCsvAdjustedThroughAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, DateOnly> { [Id] = new DateOnly(2026, 4, 30) });
            Adjustments.GetAsync(Id, Arg.Any<CancellationToken>()).Returns(AdjustmentFactors.None);
        }

        public Guid Id { get; } = Guid.NewGuid();

        public FakeBackfillSettings Settings { get; } = new();

        public FakeBackfillStore Store { get; } = new();

        public FakeBarStore Bars { get; } = new();

        public FakeIntradaySource Source { get; } = new();

        public FakeFetchTargets Targets { get; } = new();

        public ICorporateActionQueries Actions { get; } = Substitute.For<ICorporateActionQueries>();

        public IAdjustmentFactors Adjustments { get; } = Substitute.For<IAdjustmentFactors>();

        public ProbeSeamHandler Handler() =>
            new(Settings, Source, FakeAccountStatus.Connected(), Targets, Bars, Store, Actions, Adjustments, new FakeTimeProvider(new DateTimeOffset(2026, 10, 27, 6, 30, 0, TimeSpan.Zero)), NullLogger<ProbeSeamHandler>.Instance);
    }

    [Fact]
    public async Task With_the_switch_off_the_probe_requests_nothing()
    {
        var world = new World();
        world.Settings.Enabled = false;

        var result = await world.Handler().HandleAsync(new ProbeSeamCommand(), Ct);

        result.Error.Code.ShouldBe(BackfillErrors.DisabledCode);
        world.Source.Plans.ShouldBeEmpty();
        world.Store.SeamWrites.ShouldBe(0);
    }

    [Fact]
    public async Task Matching_bars_pass_the_probe_which_stores_only_the_seam_row_and_never_a_bar()
    {
        var world = new World();
        world.Source.Respond = t => new IntradayResult(t.InstrumentId, [At(0, 100.00), At(1, 100.10), At(2, 99.95)], 1);

        var result = await world.Handler().HandleAsync(new ProbeSeamCommand(), Ct);

        result.Value.Passed.ShouldBe(1);
        world.Store.Records[world.Id].ProbeStatus.ShouldBe(ProbeStatus.Passed);
        world.Bars.Sessions.ShouldBeEmpty();
        world.Bars.Stored[world.Id].Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_pass_stores_the_basis_it_rests_on_and_a_later_fail_clears_it_CF_04d()
    {
        var world = new World();
        var split = new AppliedAction(new DateOnly(2026, 6, 15), CorporateActionKind.Split, 1, 2, 0.5m);
        world.Adjustments.GetAsync(world.Id, Arg.Any<CancellationToken>()).Returns(AdjustmentFactors.From([split]));
        world.Source.Respond = t => new IntradayResult(t.InstrumentId, [At(0, 100.00), At(1, 100.10), At(2, 99.95)], 1);
        var seam = world.Store.CurrentSeams[world.Id];

        await world.Handler().HandleAsync(new ProbeSeamCommand(), Ct);

        world.Store.Records[world.Id].ProbeBasis.ShouldBe(SeamBasis.Of(seam, [split], null));
        world.Source.Respond = t => new IntradayResult(t.InstrumentId, [At(0, 20.00), At(1, 20.02), At(2, 19.98)], 1);

        await world.Handler().HandleAsync(new ProbeSeamCommand(), Ct);

        world.Store.Records[world.Id].ProbeStatus.ShouldBe(ProbeStatus.Unresolved);
        world.Store.Records[world.Id].ProbeBasis.ShouldBeNull();
    }

    [Fact]
    public async Task Dhan_bars_at_one_fifth_of_the_csv_fail_the_probe_and_the_instrument_is_seam_unresolved()
    {
        var world = new World();
        world.Source.Respond = t => new IntradayResult(t.InstrumentId, [At(0, 20.00), At(1, 20.02), At(2, 19.98)], 1);

        var result = await world.Handler().HandleAsync(new ProbeSeamCommand(), Ct);

        result.Value.Unresolved.ShouldBe(1);
        world.Store.Records[world.Id].ProbeStatus.ShouldBe(ProbeStatus.Unresolved);
        world.Store.Records[world.Id].MedianBps!.Value.ShouldBeGreaterThan(5);
    }

    [Fact]
    public async Task Raw_dhan_bars_five_times_the_csv_pass_once_the_split_inside_the_csv_span_is_applied_and_fail_without_it()
    {
        var withSplit = new World();
        withSplit.Adjustments.GetAsync(withSplit.Id, Arg.Any<CancellationToken>()).Returns(AdjustmentFactors.From([new AppliedAction(new DateOnly(2024, 6, 3), CorporateActionKind.Split, 1, 5, 0.2m)]));
        withSplit.Source.Respond = t => new IntradayResult(t.InstrumentId, [At(0, 500.00), At(1, 500.50), At(2, 499.50)], 1);
        var without = new World();
        without.Source.Respond = withSplit.Source.Respond;

        var passed = await withSplit.Handler().HandleAsync(new ProbeSeamCommand(), Ct);
        var failed = await without.Handler().HandleAsync(new ProbeSeamCommand(), Ct);

        passed.Value.Passed.ShouldBe(1);
        failed.Value.Unresolved.ShouldBe(1);
    }

    [Fact]
    public async Task No_overlapping_minute_proves_nothing_and_changes_nothing()
    {
        var world = new World();
        world.Source.Respond = t => new IntradayResult(t.InstrumentId, [At(200, 100)], 1);

        var result = await world.Handler().HandleAsync(new ProbeSeamCommand(), Ct);

        result.Value.NoOverlap.ShouldBe(1);
        world.Store.SeamWrites.ShouldBe(0);
    }
}
