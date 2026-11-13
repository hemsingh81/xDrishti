using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Lab;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using static XDrishti.Application.Tests.Learning.MlInfluenceKit;

namespace XDrishti.Application.Tests.Learning;

/// <summary>CF-10 part 10c-2 (ADR 0217): the one look a model family takes at the locked hold-out, and what a spent hold-out reads as.</summary>
public sealed class ModelHoldOutLooksTests
{
    private static readonly string Hash = new('d', 64);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero));

    private (ModelHoldOutLooks Looks, FakeHoldOutStore Store) Make(HoldOutBlock? block)
    {
        var store = new FakeHoldOutStore(block);
        return (new ModelHoldOutLooks(store, _time), store);
    }

    [Fact]
    public async Task A_model_that_has_not_looked_reads_as_kept_with_no_figures()
    {
        var (looks, _) = Make(Block());

        var reading = await looks.ReadAsync(Model(), Ct);

        reading.State.ShouldBe("kept");
        reading.Facts.ShouldBeNull();
    }

    [Fact]
    public async Task A_model_trained_without_a_hold_out_reads_as_not_kept()
    {
        var (looks, _) = Make(Block());

        var reading = await looks.ReadAsync(Model(heldOut: false), Ct);

        reading.State.ShouldBe("not_kept");
        reading.Facts!.Kept.ShouldBeFalse();
    }

    [Fact]
    public async Task The_first_model_of_a_family_takes_the_look_as_a_fresh_one_and_owns_it()
    {
        var (looks, store) = Make(Block());
        var model = Model();

        var claim = await looks.ClaimAsync(model, "auto", "system:ml", Hash, Ct);
        var reading = await looks.ReadAsync(model, Ct);

        claim.IsSuccess.ShouldBeTrue();
        claim.Value.Kind.ShouldBe("model");
        claim.Value.StrategyId.ShouldBe("model-family:tf-1@2026-05-01");
        claim.Value.Version.ShouldBe(1);
        claim.Value.ReusedWindow.ShouldBeFalse();
        claim.Value.Result.Passed.ShouldBeFalse();
        claim.Value.BlockStart.ShouldBe(BlockStart);
        store.Unlocks.Count.ShouldBe(1);
        reading.State.ShouldBe("used");
        reading.Facts!.Reused.ShouldBeFalse();
        reading.Facts.ProbabilityBeatsNoMl.ShouldBeNull("the figures are not scored in this part: unavailable, never a pass");
    }

    [Fact]
    public async Task A_second_model_of_the_same_family_is_refused_and_reads_as_reused()
    {
        var (looks, store) = Make(Block());
        await looks.ClaimAsync(Model(7, hash: 'e'), "owner", "owner", Hash, Ct);
        var other = Model(8, hash: 'b');

        var claim = await looks.ClaimAsync(other, "owner", "owner", Hash, Ct);
        var reading = await looks.ReadAsync(other, Ct);

        claim.Error.Code.ShouldBe(MlInfluenceErrors.HoldOutReusedCode);
        claim.Error.Kind.ShouldBe(ErrorKind.Conflict);
        store.Unlocks.Count.ShouldBe(1, "nothing is written by the refused look");
        reading.State.ShouldBe("reused");
        reading.Facts!.Reused.ShouldBeTrue();
    }

    [Fact]
    public async Task The_same_model_cannot_look_twice()
    {
        var (looks, store) = Make(Block());
        var model = Model();
        await looks.ClaimAsync(model, "owner", "owner", Hash, Ct);

        var again = await looks.ClaimAsync(model, "owner", "owner", Hash, Ct);

        again.Error.Code.ShouldBe(MlInfluenceErrors.HoldOutReusedCode);
        store.Unlocks.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_different_family_has_its_own_look_but_the_block_is_already_seen_so_it_is_a_reused_window_that_cannot_count()
    {
        var (looks, store) = Make(Block());
        await looks.ClaimAsync(Model(7), "owner", "owner", Hash, Ct);
        var other = Model(9, featureVersion: "tf-2", hash: 'c');

        var claim = await looks.ClaimAsync(other, "owner", "owner", Hash, Ct);
        var reading = await looks.ReadAsync(other, Ct);

        claim.IsSuccess.ShouldBeTrue();
        claim.Value.ReusedWindow.ShouldBeTrue();
        store.Unlocks.Count.ShouldBe(2);
        reading.State.ShouldBe("reused");
        reading.Facts!.Reused.ShouldBeTrue("a look at a block somebody had seen cannot make the term true");
    }

    [Fact]
    public async Task A_strategys_fresh_look_at_the_block_does_not_burn_the_models_slot_when_the_model_was_registered_before_it()
    {
        var (looks, store) = Make(Block());
        store.Unlocks.Add(new StoredUnlock("ORB", 1, "strategy", "owner", "owner", _time.GetUtcNow(), Hash, false, BlockStart, Guid.NewGuid(), null!));
        var model = Model();

        var claim = await looks.ClaimAsync(model, "owner", "owner", Hash, Ct);

        claim.Value.ReusedWindow.ShouldBeFalse("one fresh look per kind (HO-01 D5)");
        claim.Value.Reason.ShouldBeNull();
        (await looks.ReadAsync(model, Ct)).State.ShouldBe("used");
    }

    [Fact]
    public async Task At_block_one_a_model_registered_after_the_first_look_is_stored_not_predated_and_cannot_count()
    {
        var (looks, store) = Make(Block());
        store.Unlocks.Add(new StoredUnlock("ORB", 1, "strategy", "owner", "owner", DateTimeOffset.UnixEpoch.AddDays(1), Hash, false, BlockStart, Guid.NewGuid(), null!));
        var model = Model(registeredAt: DateTimeOffset.UnixEpoch.AddDays(2));

        var claim = await looks.ClaimAsync(model, "owner", "owner", Hash, Ct);
        var reading = await looks.ReadAsync(model, Ct);

        claim.Value.ReusedWindow.ShouldBeTrue();
        claim.Value.Reason.ShouldBe("not_predated");
        claim.Value.Result.ReusedReason.ShouldBe("not_predated");
        reading.State.ShouldBe("reused");
    }

    [Fact]
    public async Task At_a_later_block_a_model_registered_on_or_after_its_start_is_not_predated_and_one_registered_before_is_fresh()
    {
        var later = new DateOnly(2026, 11, 1);
        var (looks, store) = Make(Block(later) with { PrevStart = BlockStart });
        var late = Model(7, hash: 'e', registeredAt: new DateTimeOffset(2026, 11, 1, 3, 0, 0, TimeSpan.Zero)); // 08:30 India time on the start date
        var early = Model(8, featureVersion: "tf-2", hash: 'b', registeredAt: new DateTimeOffset(2026, 10, 31, 9, 0, 0, TimeSpan.Zero));

        var lateClaim = await looks.ClaimAsync(late, "owner", "owner", Hash, Ct);
        var earlyClaim = await looks.ClaimAsync(early, "owner", "owner", Hash, Ct);

        (lateClaim.Value.ReusedWindow, lateClaim.Value.Reason).ShouldBe((true, "not_predated"));
        (earlyClaim.Value.ReusedWindow, earlyClaim.Value.Reason).ShouldBe((false, null), "a not-predated look burns no slot");
        store.Unlocks.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_model_without_a_kept_hold_out_cannot_look()
    {
        var (looks, store) = Make(Block());

        var claim = await looks.ClaimAsync(Model(heldOut: false), "owner", "owner", Hash, Ct);

        claim.Error.Code.ShouldBe(MlInfluenceErrors.HoldOutUnavailableCode);
        store.Unlocks.ShouldBeEmpty();
    }

    [Fact]
    public async Task No_fixed_block_means_no_look()
    {
        var (looks, store) = Make(null);

        var claim = await looks.ClaimAsync(Model(), "owner", "owner", Hash, Ct);

        claim.Error.Code.ShouldBe(MlInfluenceErrors.HoldOutUnavailableCode);
        store.Unlocks.ShouldBeEmpty();
    }

    [Fact]
    public void The_family_is_the_feature_version_and_a_models_look_id_is_stable_and_differs_by_file_hash()
    {
        ModelHoldOutLooks.FamilyId(Model().Model).ShouldBe("model-family:tf-1");
        ModelHoldOutLooks.LookKey(Model().Model, BlockStart).ShouldBe("model-family:tf-1@2026-05-01");
        ModelHoldOutLooks.LookId(Model(hash: 'e').Model).ShouldBe(ModelHoldOutLooks.LookId(Model(hash: 'e').Model));
        ModelHoldOutLooks.LookId(Model(hash: 'e').Model).ShouldNotBe(ModelHoldOutLooks.LookId(Model(hash: 'b').Model));
    }

    [Fact]
    public async Task A_model_whose_kept_hold_out_starts_after_the_locked_block_cannot_look()
    {
        var (looks, store) = Make(Block());
        var model = Model(holdOutStart: BlockStart.AddDays(1));

        var claim = await looks.ClaimAsync(model, "owner", "owner", Hash, Ct);

        claim.Error.Code.ShouldBe(MlInfluenceErrors.HoldOutUnavailableCode);
        store.Unlocks.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_model_that_kept_an_earlier_start_than_the_block_may_look()
    {
        var (looks, _) = Make(Block());

        var claim = await looks.ClaimAsync(Model(holdOutStart: BlockStart.AddDays(-30)), "owner", "owner", Hash, Ct);

        claim.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_look_on_a_block_that_starts_before_the_hold_out_the_model_kept_does_not_count()
    {
        var (looks, store) = Make(Block());
        var model = Model(holdOutStart: BlockStart.AddDays(10));
        var view = new HoldOutResultView("x", 1, "model", "owner", "used", _time.GetUtcNow(), BlockStart, BlockStart, false, false, false, "inconclusive", "r", null, null, null, 0, null, Hash, "o", []);
        store.Unlocks.Add(new StoredUnlock(ModelHoldOutLooks.LookKey(model.Model, BlockStart), 1, "model", "owner", "o", _time.GetUtcNow(), Hash, false, BlockStart, ModelHoldOutLooks.LookId(model.Model), view));

        var reading = await looks.ReadAsync(model, Ct);

        reading.State.ShouldBe("reused");
        reading.Facts!.Reused.ShouldBeTrue();
    }

    [Fact]
    public async Task When_the_block_advances_the_family_gets_a_fresh_look_and_the_model_still_reads_its_look_at_the_old_block()
    {
        var (looks, store) = Make(Block());
        var model = Model(holdOutStart: new DateOnly(2026, 1, 1));
        await looks.ClaimAsync(model, "owner", "owner", Hash, Ct);
        var later = new DateOnly(2026, 11, 1); // the next block of the chain: it starts after the old one ends
        store.Block = Block(later) with { PrevStart = BlockStart };

        var before = await looks.ReadAsync(model, Ct);
        var claim = await looks.ClaimAsync(Model(9, hash: 'b', holdOutStart: new DateOnly(2026, 1, 1)), "owner", "owner", Hash, Ct);

        before.State.ShouldBe("used", "HO-01 D8: a release does not erase the model's hold-out figures");
        before.BlockStart.ShouldBe(BlockStart);
        claim.IsSuccess.ShouldBeTrue();
        claim.Value.StrategyId.ShouldBe("model-family:tf-1@2026-11-01");
        claim.Value.ReusedWindow.ShouldBeFalse("the old look burned only the old block; nobody has looked at the new one");
    }

    [Fact]
    public async Task The_model_reads_its_newest_fresh_look_across_blocks_so_a_later_fail_beats_an_earlier_pass()
    {
        var later = new DateOnly(2026, 11, 1);
        var (looks, store) = Make(Block(later) with { PrevStart = BlockStart });
        var model = Model(holdOutStart: new DateOnly(2026, 1, 1));
        store.Unlocks.Add(LookRow(model, BlockStart, passed: true));
        store.Unlocks.Add(LookRow(model, later, passed: false));

        var reading = await looks.ReadAsync(model, Ct);

        reading.State.ShouldBe("used");
        reading.BlockStart.ShouldBe(later);
        reading.Look.ShouldNotBeNull().Result.Passed.ShouldBeFalse("the newest fresh look is the one read");
    }

    [Fact]
    public async Task A_re_used_look_never_hides_an_earlier_fresh_one()
    {
        var later = new DateOnly(2026, 11, 1);
        var (looks, store) = Make(Block(later) with { PrevStart = BlockStart });
        var model = Model(holdOutStart: new DateOnly(2026, 1, 1));
        store.Unlocks.Add(LookRow(model, BlockStart, passed: true));
        store.Unlocks.Add(LookRow(model, later, passed: false, reused: true));

        var reading = await looks.ReadAsync(model, Ct);

        reading.State.ShouldBe("used");
        reading.BlockStart.ShouldBe(BlockStart);
    }

    private StoredUnlock LookRow(StoredModel model, DateOnly block, bool passed, bool reused = false)
    {
        var view = new HoldOutResultView("x", 1, "model", "owner", reused ? "reused" : "used", _time.GetUtcNow(), block, block, reused, false, passed, passed ? "pass" : "fail", "r", null, null, null, 0, null, Hash, "o", []);
        return new StoredUnlock(ModelHoldOutLooks.LookKey(model.Model, block), 1, "model", "owner", "o", _time.GetUtcNow(), Hash, reused, block, ModelHoldOutLooks.LookId(model.Model), view);
    }

    [Fact]
    public async Task A_model_slot_taken_between_the_read_and_the_insert_stores_the_claim_as_a_reused_window()
    {
        var (looks, store) = Make(Block());
        store.AfterAnyUnlock = () =>
        {
            store.AfterAnyUnlock = null;
            store.Unlocks.Add(new StoredUnlock("model-family:tf-2@2026-05-01", 1, "model", "owner", "o", _time.GetUtcNow(), Hash, false, BlockStart, Guid.NewGuid(), null!));
        };

        var claim = await looks.ClaimAsync(Model(), "owner", "owner", Hash, Ct);

        claim.IsSuccess.ShouldBeTrue();
        claim.Value.ReusedWindow.ShouldBeTrue("the block's model slot went to the other look");
        store.Unlocks.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData("admin", "owner", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("owner", "", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("owner", "owner", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("owner", "owner", "abc")]
    public async Task A_mode_an_actor_or_a_hash_that_cannot_be_stored_is_a_refusal_not_a_database_error(string mode, string actor, string hash)
    {
        var (looks, store) = Make(Block());

        var claim = await looks.ClaimAsync(Model(), mode, actor, hash, Ct);

        claim.Error.Code.ShouldBe(LearningErrors.InvalidCode);
        store.Unlocks.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_model_that_kept_no_hold_out_reads_as_not_kept_even_when_the_block_has_a_look()
    {
        var (looks, store) = Make(Block());
        var model = Model(heldOut: false);
        var view = new HoldOutResultView("x", 1, "model", "owner", "used", _time.GetUtcNow(), BlockStart, BlockStart, false, false, false, "inconclusive", "r", null, null, null, 0, null, Hash, "o", []);
        store.Unlocks.Add(new StoredUnlock(ModelHoldOutLooks.LookKey(model.Model, BlockStart), 1, "model", "owner", "o", _time.GetUtcNow(), Hash, false, BlockStart, Guid.NewGuid(), view));

        var reading = await looks.ReadAsync(model, Ct);

        reading.State.ShouldBe("not_kept");
        reading.Facts!.Kept.ShouldBeFalse();
        reading.Facts.Reused.ShouldBeFalse();
    }

    [Fact]
    public async Task A_look_with_the_same_key_that_appears_between_the_read_and_the_insert_is_refused_as_reused_without_a_retry()
    {
        var (looks, store) = Make(Block());
        var model = Model();
        var view = new HoldOutResultView("x", 1, "model", "owner", "used", _time.GetUtcNow(), BlockStart, BlockStart, false, false, false, "inconclusive", "r", null, null, null, 0, null, Hash, "o", []);
        store.AfterAnyUnlock = () =>
        {
            store.AfterAnyUnlock = null;
            store.Unlocks.Add(new StoredUnlock(ModelHoldOutLooks.LookKey(model.Model, BlockStart), 1, "model", "owner", "o", _time.GetUtcNow(), Hash, false, BlockStart, Guid.NewGuid(), view));
        };

        var claim = await looks.ClaimAsync(model, "owner", "owner", Hash, Ct);

        claim.Error.Code.ShouldBe(MlInfluenceErrors.HoldOutReusedCode);
        store.Unlocks.Count.ShouldBe(1, "only the row that got there first; no retry stored a second one");
    }
}
