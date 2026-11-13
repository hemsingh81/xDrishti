#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Lab;
using XDrishti.Application.Learning;
using XDrishti.Application.Tests.Lab;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Learning;
using static XDrishti.Application.Tests.Learning.MlInfluenceKit;

namespace XDrishti.Application.Tests.Learning;

/// <summary>CF-10e part 3 (ADR 0285): the model's one hold-out look, its stored figures, the owner and auto modes, the seal step and the model slot.</summary>
public sealed class ModelHoldOutScorerTests
{
    private static readonly string Hash = new('a', 64);
    private static readonly DateOnly One = new(2026, 5, 1);
    private static readonly DateOnly OneEnd = new(2026, 10, 30);
    private static readonly DateOnly Two = new(2026, 10, 31);
    private static readonly DateOnly TwoEnd = new(2027, 4, 30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2027, 5, 5, 6, 0, 0, TimeSpan.Zero));
    private readonly MemoryHoldOutStore _store = new();
    private readonly MemoryScores _scores = new();
    private readonly FakePass _pass = new();
    private readonly IModelRegistry _registry = Substitute.For<IModelRegistry>();
    private readonly IMlSwitchSettings _switches = Substitute.For<IMlSwitchSettings>();
    private readonly StoredModel _model = Model(7);

    public ModelHoldOutScorerTests()
    {
        // block 1 released, block 2 in force and sealed
        _store.Blocks.Add(new HoldOutBlock(One, new DateOnly(2024, 10, 1), OneEnd, 6, Hash, DateTimeOffset.UnixEpoch));
        _store.Blocks.Add(new HoldOutBlock(Two, new DateOnly(2024, 10, 1), TwoEnd, 6, Hash, DateTimeOffset.UnixEpoch, One));
        _store.Releases.Add(new StoredRelease(One, DateTimeOffset.UnixEpoch, "owner", "owner:x", Hash, true, Two));
        _registry.FindAsync(7, Arg.Any<CancellationToken>()).Returns(_model);
        _registry.ListAsync(ModelRole.Champion, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([_model]);
        SetAuto(true);
    }

    private void SetAuto(bool on) => _switches.CurrentAsync(Arg.Any<CancellationToken>()).Returns(MlSwitches.AllOff with { AutoHoldOutScore = new MlSwitchState(3, on) });

    private ModelHoldOutLooks Looks() => new(_store, _time, _scores);

    private ModelHoldOutScorer Scorer() => new(_registry, _store, Looks(), _pass, _scores, _switches, new MlInfluenceOptions(), _time, NullLogger<ModelHoldOutScorer>.Instance);

    private ModelAutoLookStep Step() => new(new HoldOutEpochs(_store, new FixedCalendar(), _time), new ModelHoldOutCandidates(_registry, _store), Scorer(), _switches);

    private sealed class FakePass : IModelHoldOutPass
    {
        public int Calls { get; private set; }

        public Result<ModelHoldOutRows>? Answer { get; set; }

        public ValueTask<Result<ModelHoldOutRows>> RunAsync(StoredModel model, HoldOutBlock block, CancellationToken cancellationToken)
        {
            Calls++;
            if (Answer is { } fixedAnswer)
            {
                return ValueTask.FromResult(fixedAnswer);
            }

            var rows = new List<ModelHoldOutRow>();
            for (var d = 0; d < 30; d++)
            {
                for (var k = 0; k < 5; k++)
                {
                    var win = (d + k) % 2 == 0;
                    rows.Add(new ModelHoldOutRow($"t{d}-{k}", block.Start.AddDays(d), win ? 0.8 : 0.2, win, win ? 1 : -1, 0.5));
                }
            }

            return ValueTask.FromResult(Result.Success(new ModelHoldOutRows(rows, 0.5, new string('9', 64), 2)));
        }
    }

    private sealed class MemoryScores : IMlHoldOutScores
    {
        public List<StoredMlHoldOutScore> Rows { get; } = [];

        public ValueTask<StoredMlHoldOutScore?> FindAsync(long modelId, DateOnly blockStart, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Rows.FirstOrDefault(r => r.ModelId == modelId && r.BlockStart == blockStart));

        public ValueTask<bool> InsertAsync(StoredMlHoldOutScore score, CancellationToken cancellationToken)
        {
            if (Rows.Any(r => r.ModelId == score.ModelId && r.BlockStart == score.BlockStart))
            {
                return ValueTask.FromResult(false);
            }

            Rows.Add(score);
            return ValueTask.FromResult(true);
        }
    }

    [Fact]
    public async Task The_owner_score_claims_the_look_stores_the_figures_and_the_gate_reads_them()
    {
        var result = await Scorer().ScoreAsync(7, "owner", "owner:hem", Ct);
        var reading = await Looks().ReadAsync(_model, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.ShouldBe(150);
        result.Value.ProbabilityBeatsNoMl.ShouldNotBeNull().ShouldBeGreaterThan(0.95);
        result.Value.Dropped.ShouldBe(2);
        _store.Unlocks.Count.ShouldBe(1);
        _store.Unlocks[0].Kind.ShouldBe("model");
        _store.Unlocks[0].Mode.ShouldBe("owner");
        _scores.Rows.Count.ShouldBe(1);
        _scores.Rows[0].Actor.ShouldBe("owner:hem");
        reading.State.ShouldBe("used");
        reading.Facts!.Rows.ShouldBe(150);
        reading.Facts.ProbabilityBeatsNoMl.ShouldBe(result.Value.ProbabilityBeatsNoMl);
        reading.Score.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_second_score_of_the_same_model_is_refused_as_reused_and_the_stored_row_is_unchanged()
    {
        await Scorer().ScoreAsync(7, "owner", "owner:hem", Ct);
        var before = _scores.Rows.Single();

        var again = await Scorer().ScoreAsync(7, "owner", "owner:hem", Ct);

        again.Error.Code.ShouldBe(MlInfluenceErrors.HoldOutReusedCode);
        _scores.Rows.Single().ShouldBe(before);
        _pass.Calls.ShouldBe(1, "the second attempt never reads the block");
    }

    [Fact]
    public async Task With_no_block_the_answer_is_unavailable_and_nothing_is_written()
    {
        _store.Blocks.Clear();
        _store.Releases.Clear();

        var result = await Scorer().ScoreAsync(7, "owner", "owner:hem", Ct);

        result.Error.Code.ShouldBe(MlInfluenceErrors.HoldOutUnavailableCode);
        _store.Unlocks.ShouldBeEmpty();
        _scores.Rows.ShouldBeEmpty();
        _pass.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_model_registered_after_the_horizon_is_refused_and_nothing_is_stored()
    {
        var late = Model(7, registeredAt: new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero));
        _registry.FindAsync(7, Arg.Any<CancellationToken>()).Returns(late);

        var result = await Scorer().ScoreAsync(7, "owner", "owner:hem", Ct);

        result.Error.Code.ShouldBe(MlInfluenceErrors.HoldOutUnavailableCode);
        _store.Unlocks.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_block_with_nothing_to_score_answers_422_code_and_the_look_stays_burned()
    {
        _pass.Answer = Result.Success(new ModelHoldOutRows([], 0.5, new string('9', 64), 4));

        var result = await Scorer().ScoreAsync(7, "owner", "owner:hem", Ct);
        var reading = await Looks().ReadAsync(_model, Ct);

        result.Error.Code.ShouldBe(MlInfluenceErrors.HoldOutNoRowsCode);
        _store.Unlocks.Count.ShouldBe(1);
        _scores.Rows.ShouldBeEmpty();
        reading.State.ShouldBe("used");
        reading.Facts!.ProbabilityBeatsNoMl.ShouldBeNull("a look with no figures is unavailable, never a pass");
    }

    [Fact]
    public async Task A_pass_that_cannot_read_its_inputs_stores_no_figures_and_the_look_stays_burned()
    {
        _pass.Answer = Result.Failure<ModelHoldOutRows>(MlInfluenceErrors.HoldOutUnavailable("no cell statistics"));

        var result = await Scorer().ScoreAsync(7, "owner", "owner:hem", Ct);

        result.IsFailure.ShouldBeTrue();
        _store.Unlocks.Count.ShouldBe(1);
        _scores.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_auto_mode_is_recorded_as_auto_with_the_snapshot_and_refused_while_the_setting_is_off()
    {
        SetAuto(false);
        var refused = await Scorer().ScoreAsync(7, "auto", ModelHoldOutScorer.AutoActor, Ct);
        SetAuto(true);

        var done = await Scorer().ScoreAsync(7, "auto", ModelHoldOutScorer.AutoActor, Ct);

        refused.IsFailure.ShouldBeTrue();
        _store.Unlocks.Count.ShouldBe(1, "the refused attempt claimed nothing");
        done.IsSuccess.ShouldBeTrue();
        _store.Unlocks[0].Mode.ShouldBe("auto");
        _scores.Rows.Single().Mode.ShouldBe("auto");
        _scores.Rows.Single().SettingHash.ShouldBe(done.Value.SettingHash);
        done.Value.SettingHash.Length.ShouldBe(64);
    }

    [Fact]
    public async Task An_unknown_model_is_not_found()
    {
        _registry.FindAsync(99, Arg.Any<CancellationToken>()).Returns((StoredModel?)null);

        var result = await Scorer().ScoreAsync(99, "owner", "owner:hem", Ct);

        result.Error.Code.ShouldBe(ModelErrors.NotFoundCode);
    }

    [Fact]
    public async Task The_seal_step_scores_the_pre_dated_champion_once_and_a_second_evening_does_nothing()
    {
        var first = await Step().RunAsync(Ct);
        var second = await Step().RunAsync(Ct);

        first.ShouldBe("scored model 7");
        second.ShouldBe("no candidate");
        _pass.Calls.ShouldBe(1);
        _store.Unlocks.Single().Mode.ShouldBe("auto");
    }

    [Fact]
    public async Task The_seal_step_does_nothing_while_the_setting_is_off()
    {
        SetAuto(false);

        (await Step().RunAsync(Ct)).ShouldBe("auto score off");

        _pass.Calls.ShouldBe(0);
        _store.Unlocks.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_seal_step_never_takes_block_one_and_never_a_filling_block()
    {
        var one = new MemoryHoldOutStore();
        one.Blocks.Add(new HoldOutBlock(One, new DateOnly(2024, 10, 1), OneEnd, 6, Hash, DateTimeOffset.UnixEpoch));
        var step = new ModelAutoLookStep(new HoldOutEpochs(one, new FixedCalendar(), _time), new ModelHoldOutCandidates(_registry, one), Scorer(), _switches);
        (await step.RunAsync(Ct)).ShouldBe("block 1 is the owner's");

        var filling = new FakeTimeProvider(new DateTimeOffset(2026, 12, 1, 6, 0, 0, TimeSpan.Zero));
        var fill = new MemoryHoldOutStore();
        fill.Blocks.Add(new HoldOutBlock(One, new DateOnly(2024, 10, 1), OneEnd, 6, Hash, DateTimeOffset.UnixEpoch));
        fill.Blocks.Add(new HoldOutBlock(Two, new DateOnly(2024, 10, 1), TwoEnd, 6, Hash, DateTimeOffset.UnixEpoch, One));
        fill.Releases.Add(new StoredRelease(One, DateTimeOffset.UnixEpoch, "owner", "owner:x", Hash, true, Two));
        var fillStep = new ModelAutoLookStep(new HoldOutEpochs(fill, new FixedCalendar(), filling), new ModelHoldOutCandidates(_registry, fill), Scorer(), _switches);
        (await fillStep.RunAsync(Ct)).ShouldBe("block filling");

        _pass.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Only_an_eligible_pre_dated_champion_is_a_candidate()
    {
        var block = _store.Blocks[1];
        var candidates = new ModelHoldOutCandidates(_registry, _store);
        _registry.ListAsync(ModelRole.Champion, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([Model(7, registeredAt: new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero))]);
        (await candidates.FindAsync(block, Ct)).ShouldBeEmpty("registered after the block started");

        var inconclusive = _model with { Model = _model.Model with { Verdict = ValidationOutcome.Inconclusive } };
        _registry.ListAsync(ModelRole.Champion, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([inconclusive]);
        (await candidates.FindAsync(block, Ct)).ShouldBeEmpty("not Eligible");

        _registry.ListAsync(ModelRole.Champion, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([_model]);
        (await candidates.FindAsync(block, Ct)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_model_slot_is_available_with_an_eligible_champion_used_after_the_look_and_unavailable_on_a_read_error()
    {
        var slot = new ModelLookSlot(_store, new ModelHoldOutCandidates(_registry, _store));

        (await slot.ReadAsync(Two, Ct)).ShouldBe(LookAvailability.Available);
        await Scorer().ScoreAsync(7, "owner", "owner:hem", Ct);
        (await slot.ReadAsync(Two, Ct)).ShouldBe(LookAvailability.Used);

        _registry.ListAsync(ModelRole.Champion, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromException<IReadOnlyList<StoredModel>>(new InvalidOperationException("down")));
        var fresh = new MemoryHoldOutStore();
        fresh.Blocks.AddRange(_store.Blocks);
        fresh.Releases.AddRange(_store.Releases);
        await Should.ThrowAsync<InvalidOperationException>(async () => await new ModelLookSlot(fresh, new ModelHoldOutCandidates(_registry, fresh)).ReadAsync(Two, Ct));
        var none = Substitute.For<IModelRegistry>();
        none.ListAsync(ModelRole.Champion, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        (await new ModelLookSlot(fresh, new ModelHoldOutCandidates(none, fresh)).ReadAsync(Two, Ct)).ShouldBe(LookAvailability.NoSubject);
    }
}
