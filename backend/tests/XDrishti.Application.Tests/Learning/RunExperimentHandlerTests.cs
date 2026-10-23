using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Learning;

/// <summary>The experiment use case (spec P6-11): two stored runs to a verdict, the ledger count as the trials, idempotence, the honesty refusals.</summary>
public sealed class RunExperimentHandlerTests
{
    private static readonly string Base = new('a', 64);

    private static readonly string Cand = new('b', 64);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IRunStore _runs = Substitute.For<IRunStore>();

    private readonly FakeLedger _ledger = new();

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero));

    private RunExperimentHandler Handler() => new(_runs, _ledger, _time, NullLogger<RunExperimentHandler>.Instance);

    private static StoredRunHeader Header(string key) =>
        new(1, key, "sim", 5, 5, "ORB", new string('c', 64), new DateOnly(2022, 1, 1), new DateOnly(2024, 12, 31), "basket", "x", 1, 1, 0, 0, 0, 0, false, "{}", null, null, 0, new string('d', 64), new string('e', 64), DateTimeOffset.UnixEpoch, "t");

    private static async IAsyncEnumerable<RunTrade> Stream(SimTrade[] trades)
    {
        foreach (var t in trades)
        {
            await Task.Yield();
            yield return new RunTrade(t, []);
        }
    }

    /// <summary>One trade a day on days 1 to 15 of 36 months from 2022-01, each at R <paramref name="r"/>; <paramref name="skip"/> drops some.</summary>
    private static SimTrade[] Trades(double r, Func<int, bool>? skip = null, int extraDay = 0)
    {
        var list = new List<SimTrade>();
        for (var m = 0; m < 36; m++)
        {
            var first = new DateOnly(2022, 1, 1).AddMonths(m);
            for (var d = 1; d <= 15 + extraDay; d++)
            {
                if (skip is null || !skip(d))
                {
                    list.Add(FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Buy, new DateOnly(first.Year, first.Month, d), rExact: r, rFilled: r));
                }
            }
        }

        return [.. list];
    }

    private void Stored(SimTrade[] baseline, SimTrade[] candidate, StoredRunHeader? candidateHeader = null)
    {
        _runs.FindAsync(Base, Ct).Returns(Header(Base));
        _runs.FindAsync(Cand, Ct).Returns(candidateHeader ?? Header(Cand));
        _runs.StreamTradesAsync(Base, false, Ct).Returns(_ => Stream(baseline));
        _runs.StreamTradesAsync(Cand, false, Ct).Returns(_ => Stream(candidate));
    }

    private static RunExperimentCommand Command(string knob = "stop", ExperimentKnobKind kind = ExperimentKnobKind.Parameter, string from = "1.2", string to = "1.0", int holdOut = 0, string? candidate = null) =>
        new(Base, candidate ?? Cand, new ExperimentKnob(knob, kind, from, to), new ExperimentOptions(), holdOut, "cli:xd");

    [Fact]
    public async Task A_clear_gain_is_better_recorded_and_timed_honestly()
    {
        Stored(Trades(0.1), Trades(0.3));

        var result = await Handler().HandleAsync(Command(), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Outcome.ShouldBe("Better");
        result.Value.AlreadyRecorded.ShouldBeFalse();
        result.Value.Trials.ShouldBe(1);
        result.Value.ReportText.ShouldContain("not measured");
        result.Value.ReportText.ShouldContain("No hold-out was kept");
        _ledger.Rows.Count.ShouldBe(1);
        _ledger.Rows[0].Experiment.ReportText.ShouldBe(result.Value.ReportText);
        _ledger.Rows[0].Experiment.CreatedBy.ShouldBe("cli:xd");
    }

    [Fact]
    public async Task The_same_experiment_again_is_not_judged_or_counted_twice()
    {
        Stored(Trades(0.1), Trades(0.3));
        var first = await Handler().HandleAsync(Command(), Ct);
        _runs.ClearReceivedCalls();

        var again = await Handler().HandleAsync(Command(), Ct);

        again.Value.AlreadyRecorded.ShouldBeTrue();
        again.Value.Id.ShouldBe(first.Value.Id);
        _ledger.Rows.Count.ShouldBe(1);
        await _runs.DidNotReceiveWithAnyArgs().FindAsync(default!, Ct);
    }

    [Fact]
    public async Task Every_distinct_experiment_in_30_days_raises_the_trials_and_the_required_improvement()
    {
        // 0.035 R better per trade clears the 0.026 of one trial and the 0.032 of the fourth, not the 0.0374 of the ninth.
        Stored(Trades(0.1), Trades(0.135));
        var outcomes = new List<RunExperimentOutcome>();
        for (var i = 0; i < 9; i++)
        {
            outcomes.Add((await Handler().HandleAsync(Command(knob: "stop" + i), Ct)).Value);
        }

        outcomes.Select(o => o.Trials).ShouldBe([1, 2, 3, 4, 5, 6, 7, 8, 9]);
        outcomes[0].Outcome.ShouldBe("Better");
        outcomes[3].Outcome.ShouldBe("Better");
        outcomes[8].Outcome.ShouldBe("NotBetter");
        _ledger.Rows.Select(r => r.Experiment.RequiredImprovementR).ShouldBeInOrder();
    }

    [Fact]
    public async Task An_experiment_older_than_30_days_is_no_longer_a_trial()
    {
        Stored(Trades(0.1), Trades(0.3));
        await Handler().HandleAsync(Command(knob: "old"), Ct);
        _time.Advance(TimeSpan.FromDays(31));

        var result = await Handler().HandleAsync(Command(knob: "new"), Ct);

        result.Value.Trials.ShouldBe(1);
    }

    [Fact]
    public async Task A_cheaper_slippage_is_recorded_as_such_and_never_better()
    {
        Stored(Trades(0.1), Trades(0.5));

        var result = await Handler().HandleAsync(Command("slippage_bps", ExperimentKnobKind.Slippage, "8", "4"), Ct);

        result.Value.Outcome.ShouldBe("CheaperAssumption");
        _ledger.Rows.Single().Experiment.Outcome.ShouldBe(ExperimentOutcome.CheaperAssumption);
    }

    [Fact]
    public async Task The_hold_out_is_cut_and_stated()
    {
        Stored(Trades(0.1), Trades(0.3));

        var result = await Handler().HandleAsync(Command(holdOut: 6), Ct);

        result.Value.ReportText.ShouldContain("held out");
        _ledger.Rows.Single().Experiment.HoldOutStart.ShouldNotBeNull();
        _ledger.Rows.Single().Experiment.BaselineTrades.ShouldBeLessThan(540);
    }

    [Fact]
    public async Task A_candidate_with_entries_the_baseline_lacks_is_refused()
    {
        Stored(Trades(0.1), Trades(0.3, extraDay: 1));

        var result = await Handler().HandleAsync(Command(), Ct);

        result.Error.Code.ShouldBe(ExperimentErrors.EntriesAddedCode);
        _ledger.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_candidate_that_skips_entries_is_compared_on_the_entries_it_took()
    {
        Stored(Trades(0.1), Trades(0.3, d => d > 12));

        var result = await Handler().HandleAsync(Command(), Ct);

        result.IsSuccess.ShouldBeTrue();
        _ledger.Rows.Single().Experiment.CandidateTrades.ShouldBe(36 * 12);
    }

    [Fact]
    public async Task A_changed_cost_sheet_is_refused_for_every_knob()
    {
        Stored(Trades(0.1), Trades(0.3), Header(Cand) with { CostsSheetId = "cheap" });

        (await Handler().HandleAsync(Command(), Ct)).Error.Code.ShouldBe(ExperimentErrors.AssumptionsDifferCode);
        (await Handler().HandleAsync(Command("slippage_bps", ExperimentKnobKind.Slippage, "8", "12"), Ct)).Error.Code.ShouldBe(ExperimentErrors.AssumptionsDifferCode);
    }

    [Fact]
    public async Task A_changed_fill_basis_is_refused_unless_the_knob_is_the_slippage()
    {
        Stored(Trades(0.1), Trades(0.3), Header(Cand) with { Fills = "{\"slippage\":12}" });

        (await Handler().HandleAsync(Command(), Ct)).Error.Code.ShouldBe(ExperimentErrors.AssumptionsDifferCode);
        (await Handler().HandleAsync(Command("slippage_bps", ExperimentKnobKind.Slippage, "8", "12"), Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_bad_request_is_refused_before_any_read()
    {
        var same = Command(candidate: Base);
        var noChange = Command(to: "1.2");

        (await Handler().HandleAsync(same, Ct)).Error.Code.ShouldBe(LearningErrors.InvalidCode);
        (await Handler().HandleAsync(noChange, Ct)).Error.Code.ShouldBe(LearningErrors.InvalidCode);
        (await Handler().HandleAsync(Command(holdOut: 500), Ct)).Error.Code.ShouldBe(LearningErrors.InvalidCode);
        await _runs.DidNotReceiveWithAnyArgs().FindAsync(default!, Ct);
    }

    [Fact]
    public async Task An_unknown_run_is_not_found()
    {
        _runs.FindAsync(Base, Ct).Returns((StoredRunHeader?)null);
        _runs.FindAsync(Cand, Ct).Returns(Header(Cand));

        var result = await Handler().HandleAsync(Command(), Ct);

        result.Error.Code.ShouldBe(LearningErrors.RunNotFoundCode);
    }

    private sealed class FakeLedger : IExperimentLedger
    {
        public List<StoredExperiment> Rows { get; } = [];

        public ValueTask<int> CountSinceAsync(DateTimeOffset since, string excludingKey, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Rows.Count(r => r.Experiment.CreatedAt >= since && r.Experiment.ExperimentKey != excludingKey));

        public ValueTask<StoredExperiment?> FindByKeyAsync(string experimentKey, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Rows.FirstOrDefault(r => r.Experiment.ExperimentKey == experimentKey));

        public ValueTask<RecordedExperiment> AppendAsync(ExperimentToStore experiment, CancellationToken cancellationToken)
        {
            var row = new StoredExperiment(Rows.Count + 1, experiment);
            Rows.Add(row);
            return ValueTask.FromResult(new RecordedExperiment(row.Id, true));
        }

        public ValueTask<StoredExperiment?> FindAsync(long id, CancellationToken cancellationToken) => ValueTask.FromResult(Rows.FirstOrDefault(r => r.Id == id));

        public ValueTask<IReadOnlyList<StoredExperiment>> ListAsync(int limit, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<StoredExperiment>>([.. Rows.OrderByDescending(r => r.Id).Take(limit)]);
    }
}
