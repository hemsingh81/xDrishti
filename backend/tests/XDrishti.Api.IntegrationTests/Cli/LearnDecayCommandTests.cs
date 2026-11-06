using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.Cli;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Confidence;
using XDrishti.Domain.Common;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The verb <c>xd learn decay</c> (spec CF-12, part 12c-1) over fake use cases: the options, the text with every kill-rule state, the unavailable path, the open proposals, the refusals and the routing through <see cref="XdCommand"/>.</summary>
public sealed class LearnDecayCommandTests
{
    private static readonly DateOnly Day = new(2026, 11, 5);

    private sealed class FakeDecay(Func<GetDecayReportQuery, Result<DecayReportView>> answer) : IQueryHandler<GetDecayReportQuery, Result<DecayReportView>>
    {
        public List<GetDecayReportQuery> Seen { get; } = [];

        public ValueTask<Result<DecayReportView>> HandleAsync(GetDecayReportQuery query, CancellationToken cancellationToken)
        {
            Seen.Add(query);
            return ValueTask.FromResult(answer(query));
        }
    }

    private sealed class FakeProposals(Result<KillProposalsView> answer) : IQueryHandler<GetKillProposalsQuery, Result<KillProposalsView>>
    {
        public List<GetKillProposalsQuery> Seen { get; } = [];

        public ValueTask<Result<KillProposalsView>> HandleAsync(GetKillProposalsQuery query, CancellationToken cancellationToken)
        {
            Seen.Add(query);
            return ValueTask.FromResult(answer);
        }
    }

    private static DecaySeriesView Series(string state, int trades, params DecayWindowView[] windows) =>
        new("net R, modelled fills", null, state, trades, trades - (windows.Length * 60), windows.Count(w => w.Below), windows.Length >= 2 && windows[^1].Below && windows[^2].Below, windows.Length >= 2 ? 2 : null, windows,
            windows.Length == 0 ? null : new DecayCusumView(true, 1, "CUSUM crossed its limit"), windows.Length == 0 ? [] : [new DecayQuarterView("2026-Q4", trades, -0.12, true)], windows.Length == 0 ? null : 1);

    private static DecayReportView Report(bool available = true, bool hasSeries = true)
    {
        var below = new DecayWindowView(1, 60, Day.AddDays(-80), Day.AddDays(-40), -0.15, 1.9, -0.0910, true);
        var second = new DecayWindowView(2, 60, Day.AddDays(-39), Day, -0.2, 1.9, -0.0910, true);
        var edge = available ? Series("alert", 130, below, second) : new DecaySeriesView("net R, modelled fills", "unavailable: no backtest trade of this strategy before the epoch's start", "unavailable", 130, 130, 0, false, null, [], null, [], null);
        DecayKillView[] kill =
        [
            new("cost_stress", "held", -0.01, 0, 100, false, "cost stress: net at 1.5 times the charges is below zero, but only 5 new trades since it last fired and 20 are needed"),
            new("drawdown", "not_evaluated", null, null, 130, false, "drawdown not evaluated: the shadow record has no cell for a trade"),
            new("decay_twice", available ? "fires" : "not_evaluated", available ? -0.2 : null, available ? -0.091 : null, 130, false, "decay twice in a row"),
            new("failure_boundary", "holds", -1.2, -2.944439, 130, true, "the CF-09 sum is above the failure boundary"),
        ];
        var reference = available
            ? new DecayReferenceView("available", null, 320, 0.12, 1.0, "net")
            : new DecayReferenceView("unavailable", "there is no paper epoch yet, so there is no point in time to take the backtest reference at", null, null, null, null);
        return new DecayReportView(
            "ORB", available ? 3 : null, hasSeries, new DecaySettingsView(60, 1.5, -0.05, new string('a', 64), "window=60", 0.3, true), reference, edge,
            new DecaySeriesView("P10 behavioural series", "No counted paper trade of this strategy in the epoch yet.", "no_series", 0, 0, 0, false, null, [], null, [], null),
            kill, ["Report only: nothing is proposed, stored or paused and no order exists."], "decay monitor of ORB (report only)");
    }

    private static KillProposalView Proposal(long id) =>
        new(id, "ORB", "decay_twice", false, -0.2, -0.091, 130, "Decay twice in a row: two windows below their alert level.", ["ORB|a|swing", "ORB|b|intraday"], new string('b', 64), Day, DateTimeOffset.UnixEpoch, "proposed", null, null, null);

    private static Result<KillProposalsView> Proposals(bool auto, params KillProposalView[] items) =>
        Result.Success(new KillProposalsView(items, new KillSettingsView(auto, null, 1.5, 2, new string('c', 64)), ["A proposal changes nothing by itself."]));

    // the text is wrapped to a terminal width, so a sentence is compared with its line breaks and indents folded to one space
    private static async Task<(int Code, string Out, string Error)> RunAsync(FakeDecay decay, FakeProposals proposals, params string[] args)
    {
        var (code, output, error) = await RunRawAsync(decay, proposals, args);

        return (code, string.Join(' ', output.Split(['\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries)), error);
    }

    private static async Task<(int Code, string Out, string Error)> RunRawAsync(FakeDecay decay, FakeProposals proposals, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var services = new ServiceCollection()
            .AddSingleton<IQueryHandler<GetDecayReportQuery, Result<DecayReportView>>>(decay)
            .AddSingleton<IQueryHandler<GetKillProposalsQuery, Result<KillProposalsView>>>(proposals)
            .BuildServiceProvider();

        var code = await XdCommand.RunAsync(["learn", "decay", .. args], services, new SimulateConsole(output, error), TimeSpan.Zero, TestContext.Current.CancellationToken);

        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public void The_strategy_is_required_and_given_once()
    {
        var (options, errors) = LearnDecayOptions.Parse(["--strategy", " ORB "]);

        errors.ShouldBeEmpty();
        options!.Strategy.ShouldBe("ORB");
        LearnDecayOptions.Parse([]).Options.ShouldBeNull();
    }

    [Theory]
    [InlineData("--strategy")]
    [InlineData("--strategy", "")]
    [InlineData("--strategy", "  ")]
    [InlineData("--strategy", "A", "--strategy", "B")]
    [InlineData("--strategy", "A", "--epoch", "1")]
    [InlineData("A")]
    public void Wrong_input_lists_errors_and_no_options(params string[] args)
    {
        var (options, errors) = LearnDecayOptions.Parse(args);

        options.ShouldBeNull();
        errors.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task The_text_shows_the_windows_every_kill_state_and_the_open_proposals()
    {
        var decay = new FakeDecay(_ => Result.Success(Report()));
        var proposals = new FakeProposals(Proposals(false, Proposal(7)));

        var (code, output, error) = await RunAsync(decay, proposals, "--strategy", "ORB");

        code.ShouldBe(0);
        error.ShouldBeEmpty();
        output.ShouldContain("decay monitor of ORB (report only)");
        output.ShouldContain("reference: 320 backtest trades (N_ref), mean 0.1200 R (mu_ref), sigma 1.0000, net of costs");
        output.ShouldContain("window 1: 2026-08-17 to 2026-09-26, 60 trades, mean -0.1500 R, alert level -0.0910 (design effect 1.90): BELOW (alert)");
        output.ShouldContain("twice in a row: yes (first reached at window 2)");
        output.ShouldContain("CUSUM: ALARM, 1 event(s)");
        output.ShouldContain("cost_stress: HELD (fires on the numbers but waits for new trades; not a pass)");
        output.ShouldContain("drawdown: NOT EVALUATED (not a pass), 130 trade(s)");
        output.ShouldContain("decay_twice: FIRES, value -0.2000 against -0.0910, 130 trade(s)");
        output.ShouldContain("failure_boundary: holds");
        output.ShouldContain("proposal only, the automatic setting never executes it");
        output.ShouldContain("automatic demotion (confidence.kill.auto): OFF (a proposal waits for the owner)");
        output.ShouldContain("#7 decay_twice, proposed 2026-11-05, would pause 2 cell(s): ORB|a|swing, ORB|b|intraday");
        output.ShouldContain("behavioural series (P10 paper trades, beside it): no_series, 0 trade(s)");
        output.ShouldContain("no window is judged; this is not a pass");
        decay.Seen.ShouldHaveSingleItem().Strategy.ShouldBe("ORB");
        var asked = proposals.Seen.ShouldHaveSingleItem();
        (asked.State, asked.Strategy).ShouldBe(("proposed", "ORB"));
    }

    [Fact]
    public async Task An_unavailable_reference_says_so_and_why_and_is_never_a_pass()
    {
        var (code, output, _) = await RunAsync(new FakeDecay(_ => Result.Success(Report(available: false))), new FakeProposals(Proposals(true)), "--strategy", "ORB");

        code.ShouldBe(0);
        output.ShouldContain("reference: unavailable: there is no paper epoch yet");
        output.ShouldContain("A series with no reference is not judged; that is not a pass.");
        output.ShouldContain("edge series (graded shadow trades, the primary reading): unavailable, 130 trade(s)");
        output.ShouldContain("note: unavailable: no backtest trade of this strategy before the epoch's start");
        output.ShouldContain("no window is judged; this is not a pass");
        output.ShouldContain("decay_twice: NOT EVALUATED (not a pass)");
        output.ShouldContain("automatic demotion (confidence.kill.auto): ON (the owner switched it on)");
        output.ShouldContain("open kill proposals: none");
        output.ShouldNotContain("(alert)");
    }

    [Fact]
    public async Task A_strategy_with_no_series_prints_the_report_and_exits_1()
    {
        var (code, output, _) = await RunAsync(new FakeDecay(_ => Result.Success(Report(available: false, hasSeries: false))), new FakeProposals(Proposals(false)), "--strategy", "ORB");

        code.ShouldBe(1);
        output.ShouldContain("decay monitor of ORB");
    }

    [Fact]
    public async Task A_refused_strategy_exits_1_with_the_reason_and_asks_for_no_proposals()
    {
        var decay = new FakeDecay(_ => Result.Failure<DecayReportView>(Error.Validation("paper.invalid", "A strategy id holds no `|`.")));
        var proposals = new FakeProposals(Proposals(false));

        var (code, output, error) = await RunAsync(decay, proposals, "--strategy", "A|B");

        code.ShouldBe(1);
        output.ShouldBeEmpty();
        error.ShouldContain("paper.invalid: A strategy id holds no `|`.");
        proposals.Seen.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_bad_line_exits_2_without_calling_the_use_case()
    {
        var decay = new FakeDecay(_ => Result.Success(Report()));

        var (code, _, error) = await RunAsync(decay, new FakeProposals(Proposals(false)), "--strategy");

        code.ShouldBe(2);
        error.ShouldContain("usage:");
        decay.Seen.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_failed_proposal_read_is_shown_as_unavailable_not_as_none()
    {
        var failing = new FakeProposals(Result.Failure<KillProposalsView>(Error.Validation("paper.invalid", "no")));

        var (code, output, _) = await RunAsync(new FakeDecay(_ => Result.Success(Report())), failing, "--strategy", "ORB");

        code.ShouldBe(0);
        output.ShouldContain("open kill proposals: unavailable: paper.invalid: no");
        output.ShouldNotContain("open kill proposals: none");
    }

    [Fact]
    public async Task Long_sentences_are_wrapped_and_stored_text_cannot_write_control_characters()
    {
        var report = Report() with { Line = string.Join(' ', Enumerable.Repeat("word", 80)), Statements = ["bad\u001b[31m text\nsecond line"] };

        var (_, output, _) = await RunRawAsync(new FakeDecay(_ => Result.Success(report)), new FakeProposals(Proposals(false)), "--strategy", "ORB");

        output.Split('\n').ShouldAllBe(l => l.TrimEnd('\r').Length <= LearnReportText.Width);
        output.ShouldNotContain("\u001b");
        string.Join(' ', output.Split(['\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries)).ShouldContain("bad [31m text second line");
    }
}
