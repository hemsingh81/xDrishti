using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.Cli;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Confidence;
using XDrishti.Application.Lab;
using XDrishti.Domain.Common;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>
/// The verb <c>xd readiness</c> (spec CF-11, part 2a, ADR 0221) over fake use cases: every state of a sub-score and of a row, the hard fails, both gates, the snapshot, the enforce setting ON and OFF, the system card, the latest-version lookup, the refused and failed reads
/// (never shown as a pass or as nothing), the bad command lines and the routing through <see cref="XdCommand"/>. Nothing here touches a database.
/// </summary>
public sealed class ReadinessCliCommandTests
{
    private static readonly string Hash = new('a', 64);

    private sealed class Fake(Result<ScorecardView> card, Result<ScorecardListView>? list = null, Result<StrategyDetail>? detail = null, Exception? failure = null)
        : IQueryHandler<GetScorecardQuery, Result<ScorecardView>>, IQueryHandler<ListScorecardsQuery, Result<ScorecardListView>>, IQueryHandler<GetStrategyQuery, Result<StrategyDetail>>
    {
        public List<GetScorecardQuery> Cards { get; } = [];

        public int Lists { get; private set; }

        public List<GetStrategyQuery> Details { get; } = [];

        public ValueTask<Result<ScorecardView>> HandleAsync(GetScorecardQuery query, CancellationToken cancellationToken)
        {
            Cards.Add(query);
            return failure is null ? ValueTask.FromResult(card) : throw failure;
        }

        public ValueTask<Result<ScorecardListView>> HandleAsync(ListScorecardsQuery query, CancellationToken cancellationToken)
        {
            Lists++;
            return failure is null ? ValueTask.FromResult(list ?? Result.Failure<ScorecardListView>(new Error("x", "no list"))) : throw failure;
        }

        public ValueTask<Result<StrategyDetail>> HandleAsync(GetStrategyQuery query, CancellationToken cancellationToken)
        {
            Details.Add(query);
            return ValueTask.FromResult(detail ?? Result.Success(new StrategyDetail(query.Id, "intraday", false, DateTimeOffset.UnixEpoch, 3, [])));
        }
    }

    private static ScorecardRowView Row(int n, string status, string value = "n_eff 45") => new(n, $"Row {n} name", "CF-01", status, "n_eff at least 300", value);

    private static ScorecardView Card(bool enforce = false, params ScorecardPointView[] history) => new(
        "ORB", 3, 20.2, 25.0, "not_ready", "Not ready",
        [
            new SubScoreView("Evidence", "scored", 60.7, "forward n_eff 45", "55 more effective trades reach 100"),
            new SubScoreView("Power at 0.3 R", "scored", 20.2, "power 38.08 % to see 0.3 R at n_eff 45", "about 155 effective trades (110 more) reach 80 % power"),
            new SubScoreView("Backtest agreement", "unavailable", null, "no point-in-time backtest band, or no forward mean to compare with it", null),
            new SubScoreView("Calibration", "not_applicable", null, "no ML model takes part, so there is nothing to calibrate", null),
        ],
        ["integrity fault (the input is unavailable, which is never a pass)", "an input of the Backtest agreement sub-score is unavailable (no band)"],
        ["Calibration sub-score (no ML model takes part)"], "Backtest agreement", "a net band is needed",
        [Row(1, "pass", "registered and frozen"), Row(2, "fail"), Row(13, "unavailable", "unavailable: no same-universe benchmark producer exists")],
        1,
        new ReadinessGateView("R", false, true, [Row(2, "fail"), Row(13, "unavailable")]),
        new ReadinessGateView("S", false, false, [Row(2, "fail")]),
        ["row 13 Benchmark: waiting for P8-07 baskets benchmarks"], ["The same-day correlation is the placeholder 0.3."], enforce, Hash, "cf11-v1/nEff=300", history);

    private static ScorecardListView List(bool enforce = false, long? switchId = null, params ScorecardSummaryView[] items) => new(
        new SystemScorecardView(0, "not_ready", "Not ready", 0, 36.5, true, "B", "Power at 0.3 R", items.Length, "an integrity check failed or cannot be read"),
        items, new ReadinessSettingsView(enforce, switchId, Hash, "cf11-v1/nEff=300"), ["The scorecard is derived on every read."]);

    private static ScorecardSummaryView Line(string id, double score, bool r = false, bool s = false) =>
        new(id, 1, score, "building", "Building", "Power at 0.3 R", 7, ["integrity fault"], r, s);

    private static async Task<(int Code, string Out, string Error)> RunRawAsync(Fake fake, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var services = new ServiceCollection()
            .AddSingleton<IQueryHandler<GetScorecardQuery, Result<ScorecardView>>>(fake)
            .AddSingleton<IQueryHandler<ListScorecardsQuery, Result<ScorecardListView>>>(fake)
            .AddSingleton<IQueryHandler<GetStrategyQuery, Result<StrategyDetail>>>(fake)
            .BuildServiceProvider();

        var code = await XdCommand.RunAsync(["readiness", .. args], services, new SimulateConsole(output, error), TimeSpan.Zero, TestContext.Current.CancellationToken);

        return (code, output.ToString(), error.ToString());
    }

    private static async Task<(int Code, string Out, string Error)> RunAsync(Fake fake, params string[] args)
    {
        var (code, output, error) = await RunRawAsync(fake, args);

        return (code, string.Join(' ', output.Split(['\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries)), error);
    }

    [Fact]
    public async Task A_strategy_card_prints_the_score_every_sub_score_state_the_hard_fails_and_each_row_with_its_input()
    {
        var fake = new Fake(Result.Success(Card()));

        var (code, output, error) = await RunAsync(fake, "ORB");

        code.ShouldBe(0);
        error.ShouldBeEmpty();
        output.ShouldContain("readiness of ORB version 3");
        output.ShouldContain("score: 20.2 of 100 = the minimum of the scored sub-scores");
        output.ShouldContain("label: Not ready");
        output.ShouldContain("before the hard fails: 25.0");
        output.ShouldContain("Evidence: 60.7 (forward n_eff 45); to raise it: 55 more effective trades reach 100");
        output.ShouldContain("Backtest agreement: unavailable: no point-in-time backtest band, or no forward mean to compare with it. Unavailable is not a finding and not a pass. It is a hard fail.");
        output.ShouldContain("Calibration: not applicable (no ML model takes part, so there is nothing to calibrate); left out of the minimum");
        output.ShouldContain("hard fails (each turns the score into 0; an input that is unavailable is one):");
        output.ShouldContain("- integrity fault (the input is unavailable, which is never a pass)");
        output.ShouldContain("weakest link: Backtest agreement; what would move it: a net band is needed");
        output.ShouldContain("edge checklist (1 of 3 rows pass):");
        output.ShouldContain("1. Row 1 name: PASS");
        output.ShouldContain("input: registered and frozen");
        output.ShouldContain("2. Row 2 name: FAIL");
        output.ShouldContain("13. Row 13 name: UNAVAILABLE (counts as a fail)");
        output.ShouldContain("unavailable: unavailable: no same-universe benchmark producer exists. Unavailable is not a finding and not a pass.");
        output.ShouldContain("producer: CF-01; threshold: n_eff at least 300");
        fake.Details.Single().Id.ShouldBe("ORB");
        fake.Cards.ShouldHaveSingleItem().ShouldBe(new GetScorecardQuery("ORB", 3), "the latest version of the strategy");
    }

    [Fact]
    public async Task Both_gates_print_what_stops_them_and_a_gate_that_does_not_pass_says_it_is_not_a_pass()
    {
        var (_, output, _) = await RunAsync(new Fake(Result.Success(Card())), "ORB");

        output.ShouldContain("gate R (Candidate to Paper, and the owner's re-enable): rows 1 to 12 and 14 all pass: DOES NOT PASS (not a pass)");
        output.ShouldContain("stopped by rows: 2 (fail), 13 (unavailable)");
        output.ShouldContain("gate S (Paper to Active): gate R, rows 13 and 15, and the label Ready for the next phase: DOES NOT PASS (not a pass)");
        output.ShouldContain("the label is not Ready for the next phase");
        output.ShouldContain("waiting for (each is unavailable, which counts as a fail):");
        output.ShouldContain("row 13 Benchmark: waiting for P8-07 baskets benchmarks");
    }

    [Fact]
    public async Task A_passing_gate_says_passes()
    {
        var passing = Card() with { GateR = new ReadinessGateView("R", true, true, []), GateS = new ReadinessGateView("S", true, true, []) };

        var (_, output, _) = await RunAsync(new Fake(Result.Success(passing)), "ORB", "--version", "3");

        output.ShouldContain("rows 1 to 12 and 14 all pass: PASSES");
        output.ShouldContain("Ready for the next phase: PASSES");
        output.ShouldNotContain("stopped by rows");
    }

    [Fact]
    public async Task The_newest_snapshot_with_its_id_and_hash_is_printed_or_the_absence_of_one_is_said()
    {
        var with = await RunAsync(new Fake(Result.Success(Card(false, new ScorecardPointView(12, new DateOnly(2026, 11, 5), 18.5, "not_ready", Hash), new ScorecardPointView(11, new DateOnly(2026, 11, 4), 10, "not_ready", Hash)))), "ORB");
        var without = await RunAsync(new Fake(Result.Success(Card())), "ORB");

        with.Out.ShouldContain("snapshot: newest daily snapshot #12 of 2026-11-05, score 18.5 (not_ready), settings " + Hash + "; 2 point(s) of the trend are stored");
        without.Out.ShouldContain("snapshot: no daily snapshot is stored yet");
        without.Out.ShouldContain("settings hash " + Hash + " (cf11-v1/nEff=300)");
    }

    [Theory]
    [InlineData(false, "enforce: OFF.", "only warns")]
    [InlineData(true, "enforce: ON.", "REFUSES the lifecycle move it judges")]
    public async Task The_enforce_setting_and_its_meaning_are_printed_both_ways(bool enforce, string head, string meaning)
    {
        var (_, output, _) = await RunAsync(new Fake(Result.Success(Card(enforce))), "ORB");

        output.ShouldContain(head);
        output.ShouldContain(meaning);
        if (enforce)
        {
            output.ShouldContain("(Candidate to Paper, the owner's re-enable, Paper to Active)");
        }
    }

    [Fact]
    public async Task An_explicit_version_skips_the_latest_version_lookup()
    {
        var fake = new Fake(Result.Success(Card()));

        var (code, _, _) = await RunAsync(fake, "ORB", "--version", "2");

        code.ShouldBe(0);
        fake.Details.ShouldBeEmpty();
        fake.Cards.Single().ShouldBe(new GetScorecardQuery("ORB", 2));
    }

    [Fact]
    public async Task The_system_card_lists_the_strategies_weakest_first_with_the_weights_the_reason_and_the_setting()
    {
        var fake = new Fake(Result.Success(Card()), Result.Success(List(true, 9, Line("B", 12.5), Line("A", 60, true, false))));

        var (code, output, _) = await RunAsync(fake);

        code.ShouldBe(0);
        output.ShouldContain("readiness of the system");
        output.ShouldContain("system score: 0.0 of 100 = min(integrity, weighted mean of the strategy scores); label: Not ready");
        output.ShouldContain("weights: equal (the plan exposes no planned risk share per strategy)");
        output.ShouldContain("weakest strategy: B, weakest sub-score: Power at 0.3 R");
        output.ShouldContain("why it is 0 or low apart from the strategies: an integrity check failed or cannot be read");
        output.IndexOf("B v1: score 12.5", StringComparison.Ordinal).ShouldBeLessThan(output.IndexOf("A v1: score 60.0", StringComparison.Ordinal));
        output.ShouldContain("A v1: score 60.0, Building, rows passed 7 of 15, gate R passes, gate S does not pass");
        output.ShouldContain("enforce: ON (switch row 9).");
        fake.Lists.ShouldBe(1);
        fake.Cards.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_word_system_is_the_system_card_and_an_empty_lab_says_so()
    {
        var fake = new Fake(Result.Success(Card()), Result.Success(List()));

        var (code, output, _) = await RunAsync(fake, "system");

        code.ShouldBe(0);
        output.ShouldContain("strategies: none in the Lab yet");
        output.ShouldContain("enforce: OFF (no row is stored: the default).");
    }

    [Fact]
    public async Task An_unknown_strategy_is_refused_with_exit_1_and_prints_nothing_to_the_output()
    {
        var fake = new Fake(Result.Success(Card()), detail: Result.Failure<StrategyDetail>(Error.NotFound("lab.not_found", "No strategy NOPE.")));

        var (code, output, error) = await RunRawAsync(fake, "NOPE");

        code.ShouldBe(1);
        output.ShouldBeEmpty();
        error.ShouldContain("lab.not_found: No strategy NOPE.");
        fake.Cards.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_version_and_a_strategy_with_no_version_are_refused()
    {
        var unknown = await RunRawAsync(new Fake(Result.Failure<ScorecardView>(ReadinessErrors.ScorecardNotFound("ORB", 9))), "ORB", "--version", "9");
        var none = await RunRawAsync(new Fake(Result.Success(Card()), detail: Result.Success(new StrategyDetail("ORB", "intraday", false, DateTimeOffset.UnixEpoch, 0, []))), "ORB");

        unknown.Code.ShouldBe(1);
        unknown.Out.ShouldBeEmpty();
        unknown.Error.ShouldContain("confidence.scorecard_not_found");
        none.Code.ShouldBe(1);
        none.Out.ShouldBeEmpty();
        none.Error.ShouldContain("has no version in the Lab");
    }

    [Fact]
    public async Task A_failed_read_of_the_list_exits_1_and_is_never_shown_as_a_pass_or_as_an_empty_lab()
    {
        var (code, output, error) = await RunRawAsync(new Fake(Result.Success(Card()), Result.Failure<ScorecardListView>(Error.Upstream("x.down", "the store is down"))));

        code.ShouldBe(1);
        output.ShouldBeEmpty();
        error.ShouldContain("x.down: the store is down");
        output.ShouldNotContain("none in the Lab");
    }

    [Fact]
    public async Task A_thrown_failure_exits_3_and_prints_no_card()
    {
        var (code, output, error) = await RunRawAsync(new Fake(Result.Success(Card()), failure: new InvalidOperationException("boom")), "ORB", "--version", "1");

        code.ShouldBe(3);
        output.ShouldBeEmpty();
        error.ShouldContain("failed:");
    }

    [Fact]
    public async Task A_cancelled_run_exits_130()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var services = new ServiceCollection()
            .AddSingleton<IQueryHandler<GetScorecardQuery, Result<ScorecardView>>>(new Fake(Result.Success(Card()), failure: new OperationCanceledException(cts.Token)))
            .AddSingleton<IQueryHandler<GetStrategyQuery, Result<StrategyDetail>>>(new Fake(Result.Success(Card())))
            .BuildServiceProvider();

        var code = await XdCommand.RunAsync(["readiness", "ORB", "--version", "1"], services, new SimulateConsole(output, error), TimeSpan.Zero, cts.Token);

        code.ShouldBe(130);
        error.ToString().ShouldContain("cancelled");
        output.ToString().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("--version", "2")]
    [InlineData("ORB", "--version")]
    [InlineData("ORB", "--version", "0")]
    [InlineData("ORB", "--version", "x")]
    [InlineData("ORB", "--version", "1", "--version", "2")]
    [InlineData("system", "--version", "1")]
    [InlineData("ORB", "OTHER")]
    [InlineData("ORB", "--nope")]
    [InlineData("A|B")]
    [InlineData("   ")]
    public async Task A_bad_command_line_is_exit_2_with_the_usage_and_no_use_case_is_called(params string[] args)
    {
        var fake = new Fake(Result.Success(Card()), Result.Success(List()));

        var (code, output, error) = await RunRawAsync(fake, args);

        code.ShouldBe(2);
        output.ShouldBeEmpty();
        error.ShouldContain("usage:");
        error.ShouldContain("xd readiness STRATEGY [--version N]");
        (fake.Cards.Count + fake.Details.Count + fake.Lists).ShouldBe(0);
    }

    [Fact]
    public async Task Control_characters_of_a_stored_text_are_stripped_and_no_line_is_wider_than_100_columns()
    {
        var long_ = string.Join(' ', Enumerable.Repeat("a very long reason for an unavailable input", 12));
        var card = Card() with { Strategy = "OR\u001b[31mB", Waiting = [long_], Notes = [long_ + "\u0007"] };

        var (code, output, _) = await RunRawAsync(new Fake(Result.Success(card)), "ORB", "--version", "3");

        code.ShouldBe(0);
        output.ShouldNotContain("\u001b");
        output.ShouldNotContain("\u0007");
        output.ShouldContain("OR [31mB");
        output.Split('\n').ShouldAllBe(l => l.TrimEnd('\r').Length <= 100);
    }

    [Fact]
    public void The_usage_names_both_forms_the_exit_codes_and_that_nothing_is_changed()
    {
        ReadinessCliOptions.Usage.ShouldContain("xd readiness [system]");
        ReadinessCliOptions.Usage.ShouldContain("exit codes: 0 done, 1 refused");
        ReadinessCliOptions.Usage.ShouldContain("read only");
        XdOptions.Usage.ShouldContain("xd readiness STRATEGY [--version N]");
    }
}
