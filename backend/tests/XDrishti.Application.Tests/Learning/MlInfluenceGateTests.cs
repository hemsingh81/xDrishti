using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;
using static XDrishti.Application.Tests.Learning.MlInfluenceKit;

namespace XDrishti.Application.Tests.Learning;

/// <summary>CF-10 part 10c-2 (ADR 0217): the gate and the read-only view. Fail closed: with the switch off (the only switch there is), for an unknown model, a non-champion, a spent hold-out, or when the score cannot be read, no score is handed out.</summary>
public sealed class MlInfluenceGateTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IModelRegistry _registry = Substitute.For<IModelRegistry>();

    private readonly IMlInfluenceChecks _checks = Substitute.For<IMlInfluenceChecks>();

    private readonly IQueryHandler<ScoreWithModelQuery, Result<ModelScoreView>> _scorer = Substitute.For<IQueryHandler<ScoreWithModelQuery, Result<ModelScoreView>>>();

    private readonly FakeHoldOutStore _holdOuts = new(Block());

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero));

    private readonly MlInfluenceOptions _options = new();

    private IMlInfluenceSwitch _switch = new MlInfluenceSwitchOff();

    private IMlInfluenceEvidence _evidence = new NoMlInfluenceEvidence();

    private MlInfluenceEvaluator Evaluator() => new(_registry, new ModelHoldOutLooks(_holdOuts, _time), _evidence, _switch, _options);

    private MlInfluenceGate Gate() => new(Evaluator(), _checks, _scorer, _time);

    [Fact]
    public async Task With_the_switch_off_the_default_the_champion_is_refused_with_the_reasons_and_no_score_is_read()
    {
        _registry.FindAsync(7, Ct).Returns(Model());

        var answer = await Gate().AnswerAsync(7, Ct);
        var score = await Gate().ScoreAsync(7, [0.1, 0.2, 0.3], Ct);

        answer.MayInfluence.ShouldBeFalse();
        answer.Reasons.Select(r => r.Code).ShouldContain("switch_off");
        answer.Reasons.Select(r => r.Code).ShouldContain("holdout_not_scored");
        score.PWin.ShouldBeNull();
        await _scorer.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public async Task The_switch_on_alone_does_not_open_the_gate_while_the_figures_are_unavailable()
    {
        _registry.FindAsync(7, Ct).Returns(Model());
        _switch = Substitute.For<IMlInfluenceSwitch>();
        _switch.IsOnAsync(Ct).Returns(true);

        var answer = await Gate().AnswerAsync(7, Ct);

        answer.MayInfluence.ShouldBeFalse();
        answer.Reasons.Select(r => r.Code).ShouldNotContain("switch_off");
        answer.Reasons.Select(r => r.Code).ShouldBe(["holdout_not_scored", "forward_not_available", "calibration_alarm_unavailable"]);
    }

    [Fact]
    public async Task An_unknown_model_is_refused()
    {
        _registry.FindAsync(99, Ct).Returns((StoredModel?)null);

        var answer = await Gate().AnswerAsync(99, Ct);

        answer.MayInfluence.ShouldBeFalse();
        answer.Reasons.ShouldHaveSingleItem().Code.ShouldBe("model_not_found");
        await _checks.DidNotReceiveWithAnyArgs().AppendIfChangedAsync(default!, Ct);
    }

    [Fact]
    public async Task A_model_that_is_not_the_champion_is_refused_for_that_too()
    {
        _registry.FindAsync(7, Ct).Returns(Model(role: ModelRole.Challenger));

        var answer = await Gate().AnswerAsync(7, Ct);

        answer.Reasons.Select(r => r.Code).ShouldContain("not_champion");
        answer.MayInfluence.ShouldBeFalse();
    }

    [Fact]
    public async Task A_spent_hold_out_is_refused_as_holdout_reused()
    {
        _registry.FindAsync(8, Ct).Returns(Model(8, hash: 'b'));
        await new ModelHoldOutLooks(_holdOuts, _time).ClaimAsync(Model(7), "owner", "owner", new string('d', 64), Ct);

        var answer = await Gate().AnswerAsync(8, Ct);

        answer.MayInfluence.ShouldBeFalse();
        answer.Reasons.Select(r => r.Code).ShouldContain("holdout_reused");
    }

    [Fact]
    public async Task Every_answer_is_recorded_as_a_check_with_the_terms_and_the_settings_hash()
    {
        _registry.FindAsync(7, Ct).Returns(Model());
        MlInfluenceCheck? seen = null;
        await _checks.AppendIfChangedAsync(Arg.Do<MlInfluenceCheck>(c => seen = c), Ct);

        await Gate().AnswerAsync(7, Ct);

        seen.ShouldNotBeNull();
        seen.ModelId.ShouldBe(7);
        seen.MayInfluence.ShouldBeFalse();
        seen.Terms.Select(t => t.Name).ShouldBe([.. MlInfluence.TermNames, MlInfluenceEvaluation.IsChampion]);
        seen.SettingHash.ShouldMatch("^[0-9a-f]{64}$");
        seen.InputsHash.ShouldMatch("^[0-9a-f]{64}$");
        seen.EvaluatedAt.ShouldBe(_time.GetUtcNow());
    }

    [Fact]
    public async Task The_inputs_hash_is_the_same_for_the_same_evaluation_and_changes_with_the_switch()
    {
        _registry.FindAsync(7, Ct).Returns(Model());
        var hashes = new List<(string Setting, string Inputs)>();
        await _checks.AppendIfChangedAsync(Arg.Do<MlInfluenceCheck>(c => hashes.Add((c.SettingHash, c.InputsHash))), Ct);

        await Gate().AnswerAsync(7, Ct);
        await Gate().AnswerAsync(7, Ct);
        _switch = Substitute.For<IMlInfluenceSwitch>();
        _switch.IsOnAsync(Ct).Returns(true);
        await Gate().AnswerAsync(7, Ct);

        hashes[0].ShouldBe(hashes[1]);
        hashes[2].Setting.ShouldNotBe(hashes[0].Setting);
        hashes[2].Inputs.ShouldNotBe(hashes[0].Inputs);
    }

    [Fact]
    public async Task The_view_lists_the_terms_and_reasons_and_changes_nothing()
    {
        _registry.FindAsync(7, Ct).Returns(Model());
        var handler = new GetMlInfluenceHandler(Evaluator(), _options);

        var view = (await handler.HandleAsync(new GetMlInfluenceQuery(7), Ct)).Value;

        view.MayInfluence.ShouldBeFalse();
        view.Role.ShouldBe("champion");
        view.SwitchOn.ShouldBeFalse();
        view.Terms.Select(t => (t.Name, t.State)).ShouldBe([("HoldOutBeatsNoMl", "unavailable"), ("ForwardShadowOk", "unavailable"), ("NoCalibrationAlarm", "unavailable"), ("SwitchOn", "fails"), ("IsChampion", "holds")]);
        view.HoldOut.State.ShouldBe("kept");
        view.HoldOut.Family.ShouldBe("model-family:tf-1");
        view.HoldOut.Rows.ShouldBeNull();
        view.Reasons.Select(r => r.Code).ShouldBe(["holdout_not_scored", "forward_not_available", "calibration_alarm_unavailable", "switch_off"]);
        view.Settings.ShouldBe(_options.KeyText());
        view.Notes.ShouldNotBeEmpty();
        await _checks.DidNotReceiveWithAnyArgs().AppendIfChangedAsync(default!, Ct);
        _holdOuts.Unlocks.ShouldBeEmpty("reading the view takes no look");
    }

    [Fact]
    public async Task The_view_of_a_challenger_adds_not_champion()
    {
        _registry.FindAsync(7, Ct).Returns(Model(role: ModelRole.Challenger));

        var view = (await new GetMlInfluenceHandler(Evaluator(), _options).HandleAsync(new GetMlInfluenceQuery(7), Ct)).Value;

        view.Role.ShouldBe("challenger");
        view.Reasons.Select(r => r.Code).ShouldContain("not_champion");
    }

    [Fact]
    public async Task The_view_of_an_unknown_model_is_not_found()
    {
        _registry.FindAsync(99, Ct).Returns((StoredModel?)null);

        var result = await new GetMlInfluenceHandler(Evaluator(), _options).HandleAsync(new GetMlInfluenceQuery(99), Ct);

        result.Error.Code.ShouldBe(ModelErrors.NotFoundCode);
    }

    [Fact]
    public async Task The_only_switch_is_off()
    {
        (await new MlInfluenceSwitchOff().IsOnAsync(Ct)).ShouldBeFalse();
    }

    /// <summary>A fake source of every figure the predicate needs, so that the allow path of the gate is exercised; the model owns a fresh look at the block.</summary>
    private async Task<MlInfluenceGate> AllowingGateAsync(StoredModel model, bool switchOn = true, MlHoldOutFigures? figures = null, MlForwardFacts? forward = null, bool? alarm = false)
    {
        _registry.FindAsync(model.Id, Ct).Returns(model);
        (await new ModelHoldOutLooks(_holdOuts, _time).ClaimAsync(model, "owner", "owner", new string('d', 64), Ct)).IsSuccess.ShouldBeTrue();
        var evidence = Substitute.For<IMlInfluenceEvidence>();
        evidence.ReadAsync(model, Ct).Returns(new MlInfluenceEvidence(figures ?? new MlHoldOutFigures(150, 0.97, 0.05), forward ?? new MlForwardFacts(40, 6, 0.1, 3), alarm));
        _evidence = evidence;
        _switch = Substitute.For<IMlInfluenceSwitch>();
        _switch.IsOnAsync(Ct).Returns(switchOn);
        return Gate();
    }

    private static ModelScoreView Scored(double p) => new(7, p, []);

    [Fact]
    public async Task When_every_term_holds_and_the_model_is_the_champion_the_score_is_handed_out()
    {
        _scorer.HandleAsync(Arg.Any<ScoreWithModelQuery>(), Ct).Returns(Result.Success(Scored(0.62)));
        var gate = await AllowingGateAsync(Model());

        var score = await gate.ScoreAsync(7, [0.1, 0.2, 0.3], Ct);

        score.Answer.MayInfluence.ShouldBeTrue();
        score.Answer.Reasons.ShouldBeEmpty();
        score.PWin.ShouldBe(0.62);
        await _checks.Received(1).AppendIfChangedAsync(Arg.Is<MlInfluenceCheck>(c => c.MayInfluence), Ct);
    }

    [Fact]
    public async Task Every_term_holding_is_still_refused_for_a_challenger()
    {
        var gate = await AllowingGateAsync(Model(role: ModelRole.Challenger));

        var score = await gate.ScoreAsync(7, [0.1, 0.2, 0.3], Ct);

        score.PWin.ShouldBeNull();
        score.Answer.Reasons.ShouldHaveSingleItem().Code.ShouldBe("not_champion");
        await _scorer.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public async Task Every_term_holding_is_still_refused_when_the_switch_is_off()
    {
        var gate = await AllowingGateAsync(Model(), switchOn: false);

        var score = await gate.ScoreAsync(7, [0.1, 0.2, 0.3], Ct);

        score.PWin.ShouldBeNull();
        score.Answer.Reasons.ShouldHaveSingleItem().Code.ShouldBe("switch_off");
        await _scorer.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public async Task The_hold_out_figures_count_only_for_the_model_that_owns_a_fresh_look()
    {
        // the figures are good, but the family's look at this block belongs to another model: they are dropped and the term fails as reused
        var other = Model(8, hash: 'b');
        (await new ModelHoldOutLooks(_holdOuts, _time).ClaimAsync(Model(7), "owner", "owner", new string('d', 64), Ct)).IsSuccess.ShouldBeTrue();
        _registry.FindAsync(8, Ct).Returns(other);
        var evidence = Substitute.For<IMlInfluenceEvidence>();
        evidence.ReadAsync(other, Ct).Returns(new MlInfluenceEvidence(new MlHoldOutFigures(150, 0.99, 1), new MlForwardFacts(40, 6, 0.1, 3), false));
        _evidence = evidence;
        _switch = Substitute.For<IMlInfluenceSwitch>();
        _switch.IsOnAsync(Ct).Returns(true);

        var answer = await Gate().AnswerAsync(8, Ct);

        answer.MayInfluence.ShouldBeFalse();
        answer.Reasons.ShouldHaveSingleItem().Code.ShouldBe("holdout_reused");
    }

    [Fact]
    public async Task The_view_and_the_stored_check_carry_the_same_terms_including_is_champion()
    {
        _registry.FindAsync(7, Ct).Returns(Model(role: ModelRole.Challenger));
        MlInfluenceCheck? seen = null;
        await _checks.AppendIfChangedAsync(Arg.Do<MlInfluenceCheck>(c => seen = c), Ct);

        await Gate().AnswerAsync(7, Ct);
        var view = (await new GetMlInfluenceHandler(Evaluator(), _options).HandleAsync(new GetMlInfluenceQuery(7), Ct)).Value;

        view.Terms.Select(t => t.Name).ShouldBe(seen!.Terms.Select(t => t.Name));
        view.Terms.Select(t => t.Name).ShouldContain("IsChampion");
        view.Terms.Select(t => t.State).ShouldBe(seen.Terms.Select(t => t.State.ToString().ToLowerInvariant()));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(1.5)]
    [InlineData(-0.1)]
    public async Task A_score_that_is_not_a_probability_is_not_handed_out(double value)
    {
        _scorer.HandleAsync(Arg.Any<ScoreWithModelQuery>(), Ct).Returns(Result.Success(Scored(value)));
        var gate = await AllowingGateAsync(Model());

        var score = await gate.ScoreAsync(7, [0.1, 0.2, 0.3], Ct);

        score.PWin.ShouldBeNull();
        score.Answer.MayInfluence.ShouldBeFalse();
        score.Answer.Reasons.ShouldHaveSingleItem().Code.ShouldBe("score_unavailable");
    }

    [Fact]
    public async Task A_model_file_that_cannot_be_read_gives_no_score()
    {
        _scorer.HandleAsync(Arg.Any<ScoreWithModelQuery>(), Ct).Returns(Result.Failure<ModelScoreView>(ModelErrors.FileCorrupt(new string('e', 64))));
        var gate = await AllowingGateAsync(Model());

        var score = await gate.ScoreAsync(7, [0.1, 0.2, 0.3], Ct);

        score.PWin.ShouldBeNull();
    }
}
