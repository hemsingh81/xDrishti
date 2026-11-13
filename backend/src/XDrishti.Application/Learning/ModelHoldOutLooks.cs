using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Lab;
using XDrishti.Domain.Common;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// The one look a model family takes at the locked hold-out, per block (CF-10 part 10c-2, spec D6 and CF-06 D5, ADR 0217). A look is a row of <c>lab.holdout_unlocks</c> of kind <c>model</c> whose id is the family AND the block (<c>model-family:{feature version}@{block start}</c>) and whose
/// version is 1, so the database lets a family have exactly one look per block, and a block that the owner advances gives the family a fresh one. Whoever takes it first owns it; a later model of the family, or a look at a block somebody had already looked at (<c>reused_window</c>), reads as
/// <c>holdout_reused</c> and can never make <c>HoldOutBeatsNoMl</c> true. A model counts only if the hold-out it kept excludes the locked block (its kept start is not after the block's start). The row is insert-only, so a look that fails or is abandoned stays burned.
/// This type claims and reads the look; scoring the hold-out rows is not built in this part, and no session of the hold-out is read here.
/// </summary>
internal sealed class ModelHoldOutLooks(IHoldOutStore store, TimeProvider time)
{
    /// <summary>The kind of the unlock row.</summary>
    public const string ModelKind = "model";

    /// <summary>The version of the unlock row: a family has one look per block.</summary>
    public const int FamilyVersion = 1;

    private const string AutoMode = "auto";
    private const string OwnerMode = "owner";

    /// <summary>The family of a model: its feature version (the unit the registry counts trials in).</summary>
    public static string FamilyId(ModelToRegister model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return "model-family:" + model.FeatureVersion;
    }

    /// <summary>The id of the family's look at one block.</summary>
    public static string LookKey(ModelToRegister model, DateOnly blockStart) => FamilyId(model) + "@" + blockStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>A stable id of a model's look: the first 16 bytes of its file hash, so the unlock row says which model owns the family's look.</summary>
    public static Guid LookId(ModelToRegister model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("model-look|" + model.Sha256))[..16]);
    }

    /// <summary>
    /// Reads where the model stands with the family's looks (HO-01 D8). The model's own newest fresh look across all blocks counts (<c>used</c>, with that block and look), so a release of the block in force does not erase its hold-out figures; a later fail beats an earlier pass because
    /// the newest look is the one read. With no such look, a look of another model or a re-used one at the block in force reads as <c>reused</c>; a free block in force reads as <c>kept</c>. Nothing looked at reads as not scored, never as a pass.
    /// </summary>
    public async ValueTask<HoldOutReading> ReadAsync(StoredModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        var kept = model.Model.HoldOutStart is not null;
        var block = await store.CurrentAsync(cancellationToken);
        var family = await store.ListUnlocksByIdPrefixAsync(ModelKind, FamilyId(model.Model) + "@", cancellationToken);
        var atBlock = block is null ? null : family.FirstOrDefault(l => l.BlockStart == block.Start);

        // a model that kept no hold-out never counts, and says so (not the look's owner or the block's history)
        if (!kept)
        {
            return new HoldOutReading("not_kept", new MlHoldOutFacts(false, false, null, null, null), atBlock?.BlockStart);
        }

        // the model's own fresh looks count only if the hold-out it kept excludes the block (a model that kept a later start trained on days of the block); the newest block wins
        var ownedId = LookId(model.Model);
        var own = family
            .Where(l => l.RunId == ownedId && !l.ReusedWindow && model.Model.HoldOutStart is { } start && l.BlockStart >= start)
            .OrderByDescending(l => l.BlockStart)
            .FirstOrDefault();
        if (own is not null)
        {
            return new HoldOutReading(HoldOutReading.Used, new MlHoldOutFacts(true, false, null, null, null), own.BlockStart, own);
        }

        return atBlock is null
            ? new HoldOutReading("kept", null, null)
            : new HoldOutReading("reused", new MlHoldOutFacts(true, true, null, null, null), atBlock.BlockStart);
    }

    /// <summary>
    /// Takes the family's one look at the current block for the model before anything is scored: stores the unlock row. A model without a kept hold-out, a kept hold-out that starts after the block, or no fixed block, cannot look (<c>learning.holdout_unavailable</c>, nothing written);
    /// a family that has looked at this block is refused <c>learning.holdout_reused</c> and nothing is written. The first look at the block is <c>fresh</c> and every later one by any strategy or model is stored as <c>reused_window</c> and cannot count (CF-06 D5).
    /// </summary>
    public async ValueTask<Result<StoredUnlock>> ClaimAsync(StoredModel model, string mode, string actor, string settingHash, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (mode is not (OwnerMode or AutoMode))
        {
            return Result.Failure<StoredUnlock>(MlInfluenceErrors.InvalidLook("The mode of a look is owner or auto."));
        }

        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 100 || settingHash is not { Length: 64 } || !settingHash.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f')))
        {
            return Result.Failure<StoredUnlock>(MlInfluenceErrors.InvalidLook("A look needs an actor (at most 100 characters) and a setting hash of 64 lower-case hex characters."));
        }

        if (model.Model.HoldOutStart is not { } kept)
        {
            return Result.Failure<StoredUnlock>(MlInfluenceErrors.HoldOutUnavailable("The model was trained without a hold-out."));
        }

        var block = await store.CurrentAsync(cancellationToken);
        if (block is null)
        {
            return Result.Failure<StoredUnlock>(MlInfluenceErrors.HoldOutUnavailable("No locked hold-out block is fixed yet."));
        }

        if (kept > block.Start)
        {
            return Result.Failure<StoredUnlock>(MlInfluenceErrors.HoldOutUnavailable("The hold-out the model kept starts after the locked block, so the model was trained on days of the block."));
        }

        // the blocks of the chain never overlap (HO-01 D1), so a family's look at a later block reads days no earlier look saw
        var key = LookKey(model.Model, block.Start);
        if (await store.GetUnlockAsync(key, FamilyVersion, ModelKind, block.Start, cancellationToken) is not null)
        {
            return Result.Failure<StoredUnlock>(MlInfluenceErrors.HoldOutReused(key));
        }

        // HO-01 D5: fresh only when the model was registered before the block's horizon and the model slot of the block is free
        var firstBlock = block.PrevStart is null;
        var firstLookAt = firstBlock ? await store.FirstLookAtAsync(block.Start, cancellationToken) : null;
        var predated = HoldOutHorizon.IsPredated(ModelKind, firstBlock, block.Start, model.Model.CreatedAt, firstLookAt);
        var reused = !predated || await store.HasFreshLookAsync(block.Start, ModelKind, cancellationToken);
        var look = Build(model, block, key, mode, actor, settingHash, reused, predated);
        var stored = await store.InsertUnlockAsync(look, cancellationToken);
        if (!stored && !reused && await store.GetUnlockAsync(key, FamilyVersion, ModelKind, block.Start, cancellationToken) is null)
        {
            // another look took the block's model slot between the read and the insert: store this one as re-used
            look = Build(model, block, key, mode, actor, settingHash, true, predated);
            stored = await store.InsertUnlockAsync(look, cancellationToken);
        }

        return stored ? Result.Success(look) : Result.Failure<StoredUnlock>(MlInfluenceErrors.HoldOutReused(key));
    }

    private StoredUnlock Build(StoredModel model, HoldOutBlock block, string key, string mode, string actor, string settingHash, bool reused, bool predated)
    {
        var now = time.GetUtcNow();
        var view = new HoldOutResultView(
            key,
            FamilyVersion,
            ModelKind,
            mode,
            reused ? "reused" : "used",
            now,
            block.Start,
            block.HistoryLast,
            reused,
            false,
            false,
            "inconclusive",
            "The family's look at this block is claimed; the hold-out rows are scored by a later step and no figure is stored yet.",
            null,
            null,
            null,
            0,
            null,
            settingHash,
            actor,
            reused ? [predated ? "Re-used window: the block's model look was already taken, so the look cannot count toward a pass." : "Not pre-dated: the model was registered on or after the start of this block (or after its first look), so the look cannot count toward a pass."] : [],
            predated ? null : HoldOutHorizon.NotPredated);
        return new StoredUnlock(key, FamilyVersion, ModelKind, mode, actor, now, settingHash, reused, block.Start, LookId(model.Model), view, predated ? null : HoldOutHorizon.NotPredated);
    }
}
