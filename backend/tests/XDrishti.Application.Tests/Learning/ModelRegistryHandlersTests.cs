using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Tests.Learning;

/// <summary>The designation, read and score use cases of the model registry (spec P6-07): the champion rule, append-only roles, honest limits in the views and a model file that is never used without its hash.</summary>
public sealed class ModelRegistryHandlersTests
{
    private static readonly string Hash = new('e', 64);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IModelRegistry _registry = Substitute.For<IModelRegistry>();

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));

    private static StoredModel Stored(ValidationOutcome verdict, ModelRole role = ModelRole.Challenger, string? note = null, int width = 3, int folds = 6, int trials = 1, int required = 1, bool heldOut = true) =>
        new(7, role, new ModelToRegister(
            new string('f', 64), Hash, 3, "FastTree", note, new string('a', 64), "tf-1", width, 30, 10, 40, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 1), heldOut ? new DateOnly(2026, 7, 1) : null, verdict, folds, 2, 4, false, 7.5, 0.7, 0.25,
            verdict == ValidationOutcome.Eligible ? string.Empty : "does not beat\nnot calibrated", "report", "params", [0, 1], [0.4, 0.6], [.. Enumerable.Repeat(0.5, width)], trials, 3, DateTimeOffset.UnixEpoch, "t"),
            required);

    private DesignateModelHandler Designate()
    {
        _registry.DesignateAsync(default, default, default!, default!, default, default).ReturnsForAnyArgs(true);
        return new(_registry, _time, NullLogger<DesignateModelHandler>.Instance);
    }

    [Theory]
    [InlineData(ValidationOutcome.CannotBeatBaselines)]
    [InlineData(ValidationOutcome.Inconclusive)]
    public async Task Only_an_eligible_model_can_be_named_champion(ValidationOutcome verdict)
    {
        _registry.FindAsync(7, Ct).Returns(Stored(verdict));

        var result = await Designate().HandleAsync(new DesignateModelCommand(7, ModelRole.Champion, "best", "test"), Ct);

        result.Error.Code.ShouldBe(ModelErrors.NotEligibleCode);
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        await _registry.DidNotReceiveWithAnyArgs().DesignateAsync(default, default, default!, default!, default, Ct);
    }

    [Theory]
    [InlineData(false, 6, 1, 1, "hold-out")]
    [InlineData(true, 5, 1, 1, "5 folds")]
    [InlineData(true, 6, 1, 3, "3 models")]
    public async Task An_eligible_verdict_is_not_enough_for_champion_without_a_hold_out_enough_folds_and_the_registry_trials(bool heldOut, int folds, int trials, int required, string reason)
    {
        _registry.FindAsync(7, Ct).Returns(Stored(ValidationOutcome.Eligible, folds: folds, trials: trials, required: required, heldOut: heldOut));

        var result = await Designate().HandleAsync(new DesignateModelCommand(7, ModelRole.Champion, "best", "test"), Ct);

        result.Error.Code.ShouldBe(ModelErrors.NotEligibleCode);
        result.Error.Message.ShouldContain(reason);
        await _registry.DidNotReceiveWithAnyArgs().DesignateAsync(default, default, default!, default!, default, Ct);
    }

    [Fact]
    public async Task A_designation_that_lost_a_race_for_the_same_role_reports_no_change()
    {
        _registry.FindAsync(7, Ct).Returns(Stored(ValidationOutcome.Eligible));
        var handler = Designate();
        _registry.DesignateAsync(7, ModelRole.Champion, "x", "test", Arg.Any<DateTimeOffset>(), Ct).Returns(false);

        var result = await handler.HandleAsync(new DesignateModelCommand(7, ModelRole.Champion, "x", "test"), Ct);

        result.Value.Changed.ShouldBeFalse();
    }

    [Fact]
    public async Task An_eligible_model_becomes_champion_by_an_appended_designation()
    {
        _registry.FindAsync(7, Ct).Returns(Stored(ValidationOutcome.Eligible));

        var result = await Designate().HandleAsync(new DesignateModelCommand(7, ModelRole.Champion, " proven ", "test"), Ct);

        result.Value.Changed.ShouldBeTrue();
        result.Value.Role.ShouldBe("champion");
        await _registry.Received(1).DesignateAsync(7, ModelRole.Champion, "proven", "test", _time.GetUtcNow(), Ct);
    }

    [Fact]
    public async Task A_model_that_cannot_be_champion_can_still_be_challenger_or_retired_and_a_role_it_has_writes_nothing()
    {
        _registry.FindAsync(7, Ct).Returns(Stored(ValidationOutcome.Inconclusive, ModelRole.Challenger));

        var same = await Designate().HandleAsync(new DesignateModelCommand(7, ModelRole.Challenger, "keep", "test"), Ct);
        var retire = await Designate().HandleAsync(new DesignateModelCommand(7, ModelRole.Retired, "set aside", "test"), Ct);

        same.Value.Changed.ShouldBeFalse();
        retire.Value.Changed.ShouldBeTrue();
        await _registry.Received(1).DesignateAsync(7, ModelRole.Retired, "set aside", "test", Arg.Any<DateTimeOffset>(), Ct);
        await _registry.DidNotReceive().DesignateAsync(7, ModelRole.Challenger, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Ct);
    }

    [Fact]
    public async Task An_unknown_model_and_bad_input_are_refused()
    {
        _registry.FindAsync(9, Ct).Returns((StoredModel?)null);

        var missing = await Designate().HandleAsync(new DesignateModelCommand(9, ModelRole.Retired, "x", "test"), Ct);
        var bad = await Designate().HandleAsync(new DesignateModelCommand(9, (ModelRole)99, " ", ""), Ct);

        missing.Error.Code.ShouldBe(ModelErrors.NotFoundCode);
        bad.Error.Code.ShouldBe(LearningErrors.InvalidCode);
        bad.Error.Fields!.Count.ShouldBe(3);
    }

    [Fact]
    public async Task The_list_filters_by_role_and_limit_and_refuses_what_it_does_not_know()
    {
        _registry.ListAsync(ModelRole.Champion, 50, Ct).Returns([Stored(ValidationOutcome.Eligible, ModelRole.Champion)]);
        var handler = new ListModelsHandler(_registry);

        var champions = await handler.HandleAsync(new ListModelsQuery("champion", 50), Ct);
        var bad = await handler.HandleAsync(new ListModelsQuery("king", 0), Ct);

        champions.Value.Single().Role.ShouldBe("champion");
        champions.Value.Single().MayBeChampion.ShouldBeTrue();
        bad.Error.Code.ShouldBe(LearningErrors.InvalidCode);
        bad.Error.Fields!.Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_detail_names_the_features_orders_them_and_carries_the_limits_the_note_and_the_history()
    {
        var width = TradeFeatureCatalog.Names.Count;
        var stored = Stored(ValidationOutcome.CannotBeatBaselines, note: "trained with FastTree", width: width);
        var importance = stored.Model.Importance.ToArray();
        importance[3] = 2.0;
        _registry.FindAsync(7, Ct).Returns(stored with { Model = stored.Model with { Importance = importance } });
        _registry.DesignationsAsync(7, Ct).Returns([new StoredDesignation(1, 7, ModelRole.Challenger, "registered", DateTimeOffset.UnixEpoch, "t")]);

        var result = await new GetModelHandler(_registry).HandleAsync(new GetModelQuery(7), Ct);

        var view = result.Value;
        view.Importance[0].Feature.ShouldBe(TradeFeatureCatalog.Names[3]);
        view.Limits.ShouldContain(l => l.Contains("Advisory", StringComparison.Ordinal));
        view.Limits.ShouldContain(l => l.Contains("ISS-19", StringComparison.Ordinal));
        view.Limits.ShouldContain("trained with FastTree");
        view.Model.Reasons.ShouldBe(["does not beat", "not calibrated"]);
        view.Model.MayBeChampion.ShouldBeFalse();
        view.Calibration.Count.ShouldBe(2);
        view.Designations.Single().Reason.ShouldBe("registered");
        (await new GetModelHandler(_registry).HandleAsync(new GetModelQuery(8), Ct)).Error.Code.ShouldBe(ModelErrors.NotFoundCode);
    }

    private sealed class FixedScorer : IExplainableScorer
    {
        public double Score(ValidationRow row) => 0.62;

        public IReadOnlyList<FeatureContribution> Explain(ValidationRow row, int top) => [new FeatureContribution(1, 0.3)];
    }

    [Fact]
    public async Task A_score_reads_the_file_through_the_hash_check_and_names_the_contributions()
    {
        var width = TradeFeatureCatalog.Names.Count;
        _registry.FindAsync(7, Ct).Returns(Stored(ValidationOutcome.Eligible, width: width));
        var files = Substitute.For<IModelFiles>();
        files.ReadAsync(Hash, Ct).Returns(Result.Success(new byte[] { 1, 2, 3 }));
        var loader = Substitute.For<IModelScorerLoader>();
        loader.Load(Arg.Any<byte[]>(), Hash).Returns(Result.Success<IExplainableScorer>(new FixedScorer()));

        var result = await new ScoreWithModelHandler(_registry, files, loader, NullLogger<ScoreWithModelHandler>.Instance).HandleAsync(new ScoreWithModelQuery(7, new double[width], 3), Ct);

        result.Value.Probability.ShouldBe(0.62);
        result.Value.Contributions.Single().Feature.ShouldBe(TradeFeatureCatalog.Names[1]);
        loader.Received(1).Load(Arg.Any<byte[]>(), Hash);
    }

    [Fact]
    public async Task A_file_that_fails_its_hash_is_not_loaded_and_nothing_is_scored()
    {
        _registry.FindAsync(7, Ct).Returns(Stored(ValidationOutcome.Eligible));
        var files = Substitute.For<IModelFiles>();
        files.ReadAsync(Hash, Ct).Returns(Result.Failure<byte[]>(ModelErrors.FileCorrupt(Hash)));
        var loader = Substitute.For<IModelScorerLoader>();

        var result = await new ScoreWithModelHandler(_registry, files, loader, NullLogger<ScoreWithModelHandler>.Instance).HandleAsync(new ScoreWithModelQuery(7, [1, 2, 3], 3), Ct);

        result.Error.Code.ShouldBe(ModelErrors.FileCorruptCode);
        loader.DidNotReceiveWithAnyArgs().Load(default!, default!);
    }

    [Fact]
    public async Task A_score_with_the_wrong_number_of_features_or_an_unknown_model_is_refused()
    {
        _registry.FindAsync(7, Ct).Returns(Stored(ValidationOutcome.Eligible));
        _registry.FindAsync(8, Ct).Returns((StoredModel?)null);
        var handler = new ScoreWithModelHandler(_registry, Substitute.For<IModelFiles>(), Substitute.For<IModelScorerLoader>(), NullLogger<ScoreWithModelHandler>.Instance);

        var width = await handler.HandleAsync(new ScoreWithModelQuery(7, [1.0], 3), Ct);
        var missing = await handler.HandleAsync(new ScoreWithModelQuery(8, [1.0], 3), Ct);
        var top = await handler.HandleAsync(new ScoreWithModelQuery(7, [1.0, 2.0, 3.0], 99), Ct);

        width.Error.Code.ShouldBe(LearningErrors.InvalidCode);
        missing.Error.Code.ShouldBe(ModelErrors.NotFoundCode);
        top.Error.Code.ShouldBe(LearningErrors.InvalidCode);
    }
}
