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

/// <summary>The phase check use case (spec P6-18): the champion before the newest challenger, nothing stored is not a pass, the comparison of two stored runs is measured, and nothing is written.</summary>
public sealed class EvaluatePhaseCheckHandlerTests
{
    private static readonly string Base = new('a', 64);

    private static readonly string Cand = new('b', 64);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IModelRegistry _models = Substitute.For<IModelRegistry>();

    private readonly IExperimentLedger _ledger = Substitute.For<IExperimentLedger>();

    private readonly IRunStore _runs = Substitute.For<IRunStore>();

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero));

    public EvaluatePhaseCheckHandlerTests()
    {
        _models.ListAsync(default, default, default).ReturnsForAnyArgs([]);
        _ledger.ListAsync(default, default).ReturnsForAnyArgs([]);
    }

    private EvaluatePhaseCheckHandler Handler() => new(_models, _ledger, _runs, _time, NullLogger<EvaluatePhaseCheckHandler>.Instance);

    private static StoredModel Model(long id, ModelRole role, ValidationOutcome verdict = ValidationOutcome.Eligible, int folds = 6, double ece = 3) =>
        new(id, role, new ModelToRegister(
            new string('f', 64), new string('e', 64), 3, "FastTree", null, new string('a', 64), "tf-1", 3, 30, 10, 40, new DateOnly(2024, 8, 1), new DateOnly(2026, 6, 1), new DateOnly(2026, 4, 1), verdict, folds, 5, 4, true, ece, 0.7, 0.25,
            verdict == ValidationOutcome.Eligible ? string.Empty : "does not beat", "report", "params", [0, 1], [0.4, 0.6], [0.5, 0.5, 0.5], 1, 3, DateTimeOffset.UnixEpoch, "t"),
            1);

    private static string Fills() => "{\"entry_limit_percent\":0.1,\"slippage\":{\"ticks\":1,\"bar_range_frac\":0.1,\"stop_exit_extra_ticks\":1,\"gap_fill\":\"at_open\"}}";

    private static StoredRunHeader Header(string key) =>
        new(1, key, "sim", 5, 5, "ORB", new string('c', 64), new DateOnly(2022, 1, 1), new DateOnly(2024, 12, 31), "basket", "x", 1, 1, 0, 0, 0, 0, false, Fills(), null, null, 0, new string('d', 64), new string('e', 64), DateTimeOffset.UnixEpoch, "t");

    private static async IAsyncEnumerable<RunTrade> Stream(SimTrade[] trades)
    {
        foreach (var t in trades)
        {
            await Task.Yield();
            yield return new RunTrade(t, []);
        }
    }

    private static SimTrade[] Trades(double r)
    {
        var list = new List<SimTrade>();
        for (var m = 0; m < 36; m++)
        {
            var first = new DateOnly(2022, 1, 1).AddMonths(m);
            for (var d = 1; d <= 15; d++)
            {
                list.Add(FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Buy, new DateOnly(first.Year, first.Month, d), rExact: r, rFilled: r));
            }
        }

        return [.. list];
    }

    private static StoredExperiment Experiment(string outcome = "Better") =>
        new(3, new ExperimentToStore(
            new string('k', 64), new ExperimentKnob("stop", ExperimentKnobKind.Parameter, "1.2", "1.0"), Base, Cand, new DateOnly(2022, 1, 1), new DateOnly(2024, 12, 31), new DateOnly(2024, 7, 1), Enum.Parse<ExperimentOutcome>(outcome), 1, 6, 5, 4, 0.026, 400, 400,
            0.1, 0.3, 40, 120, 0.02, 1400, string.Empty, string.Empty, string.Empty, DateTimeOffset.UnixEpoch, "t"));

    private void Stored(StoredExperiment experiment)
    {
        _ledger.ListAsync(1, Ct).Returns([experiment]);
        _runs.FindAsync(Base, Ct).Returns(Header(Base));
        _runs.FindAsync(Cand, Ct).Returns(Header(Cand));
        _runs.StreamTradesAsync(Base, false, Ct).Returns(_ => Stream(Trades(0.1)));
        _runs.StreamTradesAsync(Cand, false, Ct).Returns(_ => Stream(Trades(0.3)));
    }

    [Fact]
    public async Task With_nothing_stored_every_criterion_is_not_evaluable_and_nothing_is_fabricated()
    {
        var result = await Handler().HandleAsync(new EvaluatePhaseCheckQuery(null), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Report.Criteria.ShouldAllBe(c => c.Verdict == PhaseVerdict.NotEvaluable);
        result.Value.Text.ShouldContain("limit: about 2.1 years");
        await _runs.DidNotReceiveWithAnyArgs().FindAsync(default!, Ct);
    }

    [Fact]
    public async Task The_champion_is_judged_before_a_challenger()
    {
        _models.ListAsync(ModelRole.Champion, 1, Ct).Returns([Model(11, ModelRole.Champion)]);
        _models.ListAsync(ModelRole.Challenger, 1, Ct).Returns([Model(12, ModelRole.Challenger, ValidationOutcome.CannotBeatBaselines)]);

        var result = await Handler().HandleAsync(new EvaluatePhaseCheckQuery(null), Ct);

        result.Value.Report.Criteria[0].Verdict.ShouldBe(PhaseVerdict.Met);
        result.Value.Report.Criteria[0].Detail.ShouldContain("model 11 (champion)");
    }

    [Fact]
    public async Task Without_a_champion_the_newest_challenger_is_judged_and_a_failed_one_is_not_met()
    {
        _models.ListAsync(ModelRole.Challenger, 1, Ct).Returns([Model(12, ModelRole.Challenger, ValidationOutcome.CannotBeatBaselines, ece: 8)]);

        var result = await Handler().HandleAsync(new EvaluatePhaseCheckQuery(null), Ct);

        result.Value.Report.Criteria[0].Verdict.ShouldBe(PhaseVerdict.NotMet);
        result.Value.Report.Criteria[0].Detail.ShouldContain("newest challenger");
        result.Value.Report.Criteria[1].Verdict.ShouldBe(PhaseVerdict.NotMet);
    }

    [Fact]
    public async Task A_stored_experiment_is_re_read_and_judged_and_without_a_replay_time_it_is_not_met_or_called()
    {
        Stored(Experiment());

        var result = await Handler().HandleAsync(new EvaluatePhaseCheckQuery(null), Ct);

        result.Value.Report.Criteria[2].Verdict.ShouldBe(PhaseVerdict.NotEvaluable);
        result.Value.Report.Criteria[2].Detail.ShouldContain("baseline trades");
        result.Value.Report.Criteria[2].Detail.ShouldContain("not measured");
        await _ledger.DidNotReceiveWithAnyArgs().AppendAsync(default!, default, default, Ct);
    }

    [Fact]
    public async Task A_supplied_replay_time_and_a_reproduced_outcome_make_the_timing_met()
    {
        Stored(Experiment("Better"));

        var result = await Handler().HandleAsync(new EvaluatePhaseCheckQuery(120), Ct);

        result.Value.Report.Criteria[2].Verdict.ShouldBe(PhaseVerdict.Met);
    }

    [Fact]
    public async Task An_outcome_that_does_not_reproduce_is_not_called()
    {
        Stored(Experiment("NotBetter"));

        var result = await Handler().HandleAsync(new EvaluatePhaseCheckQuery(120), Ct);

        result.Value.Report.Criteria[2].Verdict.ShouldBe(PhaseVerdict.NotEvaluable);
        result.Value.Report.Criteria[2].Detail.ShouldContain("not reproduced");
    }

    [Fact]
    public async Task A_stored_run_that_is_gone_is_not_evaluable_with_the_reason()
    {
        _ledger.ListAsync(1, Ct).Returns([Experiment()]);
        _runs.FindAsync(Base, Ct).Returns((StoredRunHeader?)null);

        var result = await Handler().HandleAsync(new EvaluatePhaseCheckQuery(null), Ct);

        result.Value.Report.Criteria[2].Verdict.ShouldBe(PhaseVerdict.NotEvaluable);
        result.Value.Report.Criteria[2].Detail.ShouldContain("not found");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    [InlineData(2_000_000)]
    public async Task A_replay_time_that_is_not_a_sensible_number_is_refused(double seconds)
    {
        var result = await Handler().HandleAsync(new EvaluatePhaseCheckQuery(seconds), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(LearningErrors.InvalidCode);
    }
}
