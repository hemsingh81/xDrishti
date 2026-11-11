using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Pairs;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Pairs;
using XDrishti.Domain.Common;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.PairResearch;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Pairs;

/// <summary>P14-13 criteria 1, 2 and 10 at the use case: every candidate is in the ledger before any statistic or trade, the six variants before anything, the call order, and nothing new on a re-run.</summary>
public sealed class RunPairsHandlerTests
{
    private static readonly DateOnly Quarter = new(2025, 7, 1);
    private static readonly DateOnly LastSession = new(2025, 6, 30);

    private readonly Journal _journal = new();
    private readonly FakeLedger _ledger;
    private readonly FakeStore _store;
    private readonly FakeSimulator _simulator;
    private readonly FakeSettings _settings = new();
    private readonly IPairUniverseSource _universe = Substitute.For<IPairUniverseSource>();
    private readonly IBarQueries _daily = Substitute.For<IBarQueries>();
    private readonly IBarStore _minutes = Substitute.For<IBarStore>();
    private readonly ICostSheetStore _sheets = Substitute.For<ICostSheetStore>();
    private readonly Dictionary<Guid, IReadOnlyList<BarRow>> _rows = [];

    public RunPairsHandlerTests()
    {
        _ledger = new FakeLedger(_journal);
        _store = new FakeStore(_journal);
        _simulator = new FakeSimulator(_journal);
        _daily.ReadDailyBarsAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(call => _rows.GetValueOrDefault((Guid)call[0]) ?? []);
        _minutes.ReadAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(call => (IReadOnlyList<Bar>)[]);
    }

    private RunPairsHandler Handler() => new(_universe, _daily, _minutes, new XDrishti.Application.Tests.Market.History.FakeSeams(), _ledger, _store, _simulator, _settings, _sheets, new FakeTimeProvider(new DateTimeOffset(2025, 8, 1, 12, 0, 0, TimeSpan.Zero)), NullLogger<RunPairsHandler>.Instance);

    private static RunPairsCommand Command() => new(Quarter, Quarter, null);

    private PairInstrument Add(string symbol, double[] lnClose, int sessions = 300, bool asm = false, string? sector = "Banks")
    {
        var id = Guid.NewGuid();
        var days = PairsData.Sessions(LastSession, sessions);
        _rows[id] = [.. days.Select((d, i) => PairsData.Day(d, Math.Exp(lnClose[i])))];
        return new PairInstrument(id, symbol, sector, 5m, asm);
    }

    private void Universe(params PairInstrument[] instruments) => _universe.ListAsync(Arg.Any<CancellationToken>()).Returns(call => (IReadOnlyList<PairInstrument>)instruments);

    private List<PairInstrument> Cointegrated()
    {
        var rng = new SeededRandom(7);
        var x = PairsData.Walk(rng, 300).Select(v => v + 4.6).ToArray();
        var u = new double[300];
        for (var i = 1; i < 300; i++)
        {
            u[i] = (0.85 * u[i - 1]) + (0.003 * PairsData.Normal(rng));
        }

        var y = x.Select((v, i) => 1.0 + (0.8 * v) + u[i]).ToArray();
        var c = PairsData.Walk(rng, 300).Select(v => v + 4.5).ToArray();
        return [Add("NSE:COINT_Y", y), Add("NSE:COINT_X", x), Add("NSE:INDEP_C", c)];
    }

    [Fact]
    public async Task With_2000_candidates_the_ledger_has_2006_trials_before_any_formation_statistic_or_trade_and_the_verdict_factor_is_374()
    {
        var walk = PairsData.Walk(new SeededRandom(3), 300);
        Universe([.. Enumerable.Range(0, 65).Select(i => Add($"NSE:S{i:D2}", [.. walk.Select(v => v + 4.0 + (i * 0.001))]))]);

        var result = await Handler().HandleAsync(Command(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var firstFormation = _journal.Events.IndexOf("formation");
        firstFormation.ShouldBeGreaterThan(0);
        _journal.Events.Take(firstFormation).ShouldAllBe(e => e == "ledger");
        _journal.Events.Take(firstFormation).Count().ShouldBe(2006);
        _ledger.Rows.Select(r => r.StrategyHash).Distinct().Count().ShouldBe(2006);
        _journal.Events.Skip(firstFormation).ShouldNotContain("ledger");
        result.Value.Trials.ShouldBe(2006);
        result.Value.Factor.ShouldBe(MultipleTesting.Factor(2006), 1e-12);
        result.Value.Lines[0].ShouldContain("N 2006, F 3.74");
        _store.Formations.ShouldHaveSingleItem().Candidates.ShouldBe(2000);
        _store.Formations[0].Unavailable.ShouldBe(2000);
        _store.Formations[0].Selected.ShouldBe(0);
        _simulator.Inputs.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_six_variants_are_in_the_ledger_before_anything_is_read()
    {
        Universe();
        await Handler().HandleAsync(Command(), TestContext.Current.CancellationToken);

        _journal.Events.ShouldBe(["ledger", "ledger", "ledger", "ledger", "ledger", "ledger"]);
        await _universe.Received(1).ListAsync(Arg.Any<CancellationToken>());
        _ledger.Rows.Select(r => r.StrategyId).Distinct().ShouldBe(["pairs:intraday-1"]);
    }

    [Fact]
    public async Task Re_running_the_same_quarter_appends_nothing_to_the_ledger_and_writes_nothing_new()
    {
        Universe(Cointegrated().ToArray());
        await Handler().HandleAsync(Command(), TestContext.Current.CancellationToken);
        var rows = _ledger.Appended;
        var formations = _store.Formations.Count;
        var trades = _store.Trades.Count;

        var again = await Handler().HandleAsync(Command(), TestContext.Current.CancellationToken);

        _ledger.Appended.ShouldBe(rows);
        _store.Formations.Count.ShouldBe(formations);
        _store.Trades.Count.ShouldBe(trades);
        again.Value.FormationsWritten.ShouldBe(0);
        again.Value.TradesWritten.ShouldBe(0);
        again.Value.Lines.ShouldContain(l => l.Contains("(already stored)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_cointegrated_pair_is_selected_and_walked_for_each_of_the_six_variants_after_the_formation_is_stored()
    {
        Universe(Cointegrated().ToArray());
        _simulator.Walker = input => new PairWalkResult(
            [new PairTrade(input.Pair.Key, PairSide.LongSpread, 1_000, 2_000, -2.2, 0.3, "exit", 10, 8, 100m, null, 50m, 2.0, 12_000m, 1m, 2m, 3m, 4m, ["gross"])], PairWalkCounters.Zero);

        var result = await Handler().HandleAsync(Command(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var formation = _store.Formations.Single();
        formation.Selected.ShouldBe(1);
        formation.Candidates.ShouldBe(1);
        formation.Family.ShouldBe("pairs:intraday-1");
        formation.Quarter.ShouldBe("2025Q3");
        formation.DetailJson.ShouldContain("\"ban_list\":\"unavailable\"");
        _simulator.Inputs.Count.ShouldBe(6);
        _simulator.Inputs.Select(i => i.Variant.Label).ShouldBe(PairVariant.Grid.Select(v => v.Label));
        _simulator.Inputs.ShouldAllBe(i => i.From == Quarter && i.To == new DateOnly(2025, 9, 30));
        _journal.Events.IndexOf("formation").ShouldBeGreaterThan(_journal.Events.LastIndexOf("ledger"));
        _journal.Events.IndexOf("walk").ShouldBeGreaterThan(_journal.Events.IndexOf("formation"));
        _store.Trades.Count.ShouldBe(6);
        _store.Trades.Select(t => t.RunId).Distinct().Count().ShouldBe(6);
        var row = _store.Trades.First(t => t.Labels.Contains("variant:e2.0/L5"));
        row.Ordinal.ShouldBe(1);
        row.Side.ShouldBe("long_spread");
        row.Labels.ShouldContain("engine:pair-1");
        row.Labels.ShouldContain("fills:sim-9");
        row.Labels.ShouldContain("turnover_inr:12000.00");
        row.Labels.ShouldContain("gross");
        row.Labels.ShouldContain(l => l.StartsWith("cfg:", StringComparison.Ordinal));
        result.Value.Lines.ShouldContain(l => l.StartsWith("ledger: N 7, F", StringComparison.Ordinal) && l.Contains("pair-1 on sim-9", StringComparison.Ordinal) && l.Contains("gross", StringComparison.Ordinal));
        result.Value.Lines.ShouldContain(l => l.Contains("ban_list unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_minute_bars_of_the_selected_pairs_are_read_once_per_instrument_with_utc_instants_over_the_quarter()
    {
        var shares = Cointegrated();
        Universe(shares.ToArray());

        await Handler().HandleAsync(Command(), TestContext.Current.CancellationToken);

        foreach (var share in shares.Take(2))
        {
            await _minutes.Received(1).ReadAsync(share.Id, Arg.Is<DateTimeOffset>(d => d.Offset == TimeSpan.Zero && d == new DateTimeOffset(2025, 5, 31, 18, 30, 0, TimeSpan.Zero)), Arg.Is<DateTimeOffset>(d => d.Offset == TimeSpan.Zero && d == new DateTimeOffset(2025, 9, 30, 18, 29, 59, TimeSpan.Zero)), Arg.Any<CancellationToken>());
        }

        await _minutes.DidNotReceive().ReadAsync(shares[2].Id, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Daily_rows_on_or_after_the_quarter_start_and_an_instrument_that_only_starts_then_leave_the_formation_unchanged_the_look_ahead_property()
    {
        var shares = Cointegrated();
        Universe(shares.ToArray());
        await Handler().HandleAsync(Command(), TestContext.Current.CancellationToken);
        var formation = _store.Formations.Single().FormationId;
        var candidates = _ledger.Appended;

        foreach (var share in shares)
        {
            _rows[share.Id] = [.. _rows[share.Id], PairsData.Day(Quarter, 999), PairsData.Day(Quarter.AddDays(1), 1), PairsData.Day(Quarter.AddDays(40), 5000)];
        }

        var late = Add("NSE:LATE", [.. Enumerable.Repeat(4.6, 300)], sessions: 300);
        _rows[late.Id] = [.. PairsData.Sessions(new DateOnly(2025, 8, 29), 20).Select(d => PairsData.Day(d, 100))];
        Universe([.. shares, late]);

        var again = await Handler().HandleAsync(Command(), TestContext.Current.CancellationToken);

        _store.Formations.Count.ShouldBe(1);
        _store.Formations.Single().FormationId.ShouldBe(formation);
        again.Value.FormationsWritten.ShouldBe(0);
        _ledger.Appended.ShouldBe(candidates);
    }

    [Fact]
    public async Task Minute_bars_of_the_quarter_never_change_the_formation_and_the_variant_trials_key_on_the_quarter_span()
    {
        Universe(Cointegrated().ToArray());
        await Handler().HandleAsync(new RunPairsCommand(new DateOnly(2025, 7, 15), new DateOnly(2025, 7, 20), [PairVariant.Primary]), TestContext.Current.CancellationToken);
        var formation = _store.Formations.Single().FormationId;
        _minutes.ReadAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(call => (IReadOnlyList<Bar>)[new Bar(1_751_000_000, 9, 9, 9, 9, 9)]);

        await Handler().HandleAsync(Command(), TestContext.Current.CancellationToken);

        _store.Formations.Single().FormationId.ShouldBe(formation);
        var variant = _ledger.Rows.Where(r => r.Method == "pairs-variant").ToList();
        variant.ShouldAllBe(r => r.From == Quarter && r.To == new DateOnly(2025, 9, 30));
        variant.Count.ShouldBe(6);
        _ledger.Rows.Where(r => r.RunKey == "pairs-variant:e2.0/L5").Select(r => r.RunId).Distinct().Count().ShouldBe(1);
    }

    [Fact]
    public async Task One_named_variant_is_the_only_trial_and_the_only_walk()
    {
        Universe(Cointegrated().ToArray());

        var result = await Handler().HandleAsync(new RunPairsCommand(Quarter, Quarter, [PairVariant.Primary]), TestContext.Current.CancellationToken);

        _simulator.Inputs.Count.ShouldBe(1);
        result.Value.Trials.ShouldBe(2);
    }

    [Fact]
    public async Task The_same_bars_and_settings_give_the_same_run_ids_and_formation_id()
    {
        Universe(Cointegrated().ToArray());
        _simulator.Walker = input => new PairWalkResult(
            [new PairTrade(input.Pair.Key, PairSide.ShortSpread, 1_000, 2_000, 2.2, 0.3, "exit", 10, 8, 100m, 90m, 50m, 1.8, 12_000m, 1m, 2m, 3m, 4m, [])], PairWalkCounters.Zero);
        await Handler().HandleAsync(Command(), TestContext.Current.CancellationToken);
        var ids = _store.Trades.Select(t => t.RunId).Order().ToList();
        var formation = _store.Formations.Single().FormationId;

        var other = new RunPairsHandlerTests();
        other.Universe(other.Cointegrated().ToArray());
        other._simulator.Walker = _simulator.Walker;
        await other.Handler().HandleAsync(Command(), TestContext.Current.CancellationToken);

        other._store.Trades.Select(t => t.RunId).Order().ShouldBe(ids);
        other._store.Formations.Single().FormationId.ShouldBe(formation);
    }

    [Fact]
    public async Task A_quarter_without_252_sessions_before_it_has_no_formation_and_says_so()
    {
        Universe(Add("NSE:AAA", [.. Enumerable.Repeat(4.6, 100)], sessions: 100), Add("NSE:BBB", [.. Enumerable.Repeat(4.6, 100)], sessions: 100));

        var result = await Handler().HandleAsync(Command(), TestContext.Current.CancellationToken);

        result.Value.Lines.ShouldContain(l => l.StartsWith("2025Q3: no formation, 100 of 252 sessions", StringComparison.Ordinal));
        _store.Formations.ShouldBeEmpty();
        _journal.Events.ShouldNotContain("formation");
    }

    [Fact]
    public async Task A_share_with_todays_asm_gsm_flag_is_excluded_and_counted()
    {
        var walk = PairsData.Walk(new SeededRandom(5), 300);
        Universe(
            Add("NSE:AAA", [.. walk.Select(v => v + 4.0)]),
            Add("NSE:BBB", [.. walk.Select(v => v + 4.1)]),
            Add("NSE:CCC", [.. walk.Select(v => v + 4.2)], asm: true));

        var result = await Handler().HandleAsync(Command(), TestContext.Current.CancellationToken);

        _store.Formations.Single().Candidates.ShouldBe(1);
        _store.Formations[0].DetailJson.ShouldContain("\"asm_gsm_excluded\":1");
        result.Value.Lines.ShouldContain(l => l.Contains("asm_gsm as of today (1 flagged", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_stored_sheet_is_passed_to_the_walker_and_a_missing_one_refuses_the_run_before_anything_is_counted()
    {
        _settings.CostSheetId = "mine";
        _sheets.FindAsync("mine", Arg.Any<CancellationToken>()).Returns(Result.Failure<XDrishti.Domain.Research.CostSheet>(Error.NotFound("costs.sheet_not_found", "none")));

        var refused = await Handler().HandleAsync(Command(), TestContext.Current.CancellationToken);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("costs.sheet_not_found");
        _journal.Events.ShouldBeEmpty();

        var sheet = TestCostSheet.Of("test-2025", TestCostSheet.Schedule(new DateOnly(2025, 1, 1), TestCostSheet.IntradayA, TestCostSheet.DeliveryA));
        _sheets.FindAsync("mine", Arg.Any<CancellationToken>()).Returns(Result.Success(sheet));
        var entry = new DateTimeOffset(2025, 7, 1, 10, 0, 0, TimeSpan.FromMinutes(330)).ToUnixTimeSeconds();
        _simulator.Walker = input => new PairWalkResult(
            [new PairTrade(input.Pair.Key, PairSide.LongSpread, entry, entry + 600, -2.2, 0.3, "exit", 10, 8, 743.25m, null, 600m, 1.2, 12_000m, 100m, 101m, 100m, 100m, [])], PairWalkCounters.Zero);
        Universe(Cointegrated().ToArray());
        var ok = await Handler().HandleAsync(Command(), TestContext.Current.CancellationToken);

        ok.IsSuccess.ShouldBeTrue();
        ok.Value.Lines[0].ShouldContain("net of sheet test-2025");
        _store.Trades.ShouldAllBe(t => t.NetInr != null && !t.Labels.Contains("gross"));
    }

    [Theory]
    [InlineData("2025-07-02", "2025-07-01")]
    public async Task Dates_out_of_order_are_refused(string from, string to)
    {
        var result = await Handler().HandleAsync(new RunPairsCommand(DateOnly.Parse(from), DateOnly.Parse(to), null), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("pairs.invalid");
        _journal.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_settings_are_validated_before_anything_is_counted()
    {
        _settings.MaxCandidates = 0;

        var result = await Handler().HandleAsync(Command(), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe("pairs.invalid");
        _journal.Events.ShouldBeEmpty();
    }
}
