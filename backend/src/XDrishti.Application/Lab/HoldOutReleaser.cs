using XDrishti.Application.Abstractions.Lab;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Lab;

/// <summary>What the auto release did: whether the block in force was released, and what held it when it was not.</summary>
internal sealed record AutoReleaseResult(bool Released, string Reason);

/// <summary>
/// Releases the block in force (HO-01 D4, ADR 0283): by the owner (the password was checked at the endpoint) or by the auto option. One transaction stores the release row and block k+1, <c>[end + 1 day, end + BlockMonths]</c> under the setting read now. A release is permanent
/// and moves every development cut forward. The owner may release an unlooked block only with the explicit forfeit; the auto option never does, and any read that fails keeps the block. Reads no bar.
/// </summary>
internal sealed class HoldOutReleaser(HoldOutEpochs epochs, IHoldOutStore store, IEnumerable<IHoldOutLookSlot> slots, HoldOutOptions options, TimeProvider time)
{
    public const string OwnerMode = "owner";
    public const string AutoMode = "auto";

    /// <summary>The owner's release of the block that starts on <paramref name="start"/> (the actor is the owner after a fresh password).</summary>
    public async ValueTask<Result<HoldOutBlockView>> ReleaseByOwnerAsync(DateOnly start, bool forfeitLook, string actor, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actor))
        {
            return Result.Failure<HoldOutBlockView>(Error.Forbidden("lab.no_owner", "A hold-out block is released by the owner."));
        }

        var chain = await epochs.StatusAsync(cancellationToken);
        if (chain.Count == 0)
        {
            return Result.Failure<HoldOutBlockView>(LabErrors.HoldOutUnavailable("No hold-out block is fixed yet, so there is nothing to release."));
        }

        var target = chain.FirstOrDefault(b => b.Block.Start == start);
        if (target is null)
        {
            return Result.Failure<HoldOutBlockView>(start > chain[^1].Block.Start
                ? LabErrors.HoldOutOrder(start, chain.First(b => b.State != HoldOutBlockState.Released).Block.Start)
                : Error.NotFound(LabErrors.NotFoundCode, $"No hold-out block starts on {start:yyyy-MM-dd}."));
        }

        if (target.State == HoldOutBlockState.Released)
        {
            return Result.Failure<HoldOutBlockView>(LabErrors.HoldOutReleased(start));
        }

        var first = chain.First(b => b.State != HoldOutBlockState.Released);
        if (first != target)
        {
            return Result.Failure<HoldOutBlockView>(LabErrors.HoldOutOrder(start, first.Block.Start));
        }

        if (target.State == HoldOutBlockState.Filling)
        {
            return Result.Failure<HoldOutBlockView>(LabErrors.HoldOutFilling(start, target.End));
        }

        if (target.State == HoldOutBlockState.Sealed && !forfeitLook)
        {
            return Result.Failure<HoldOutBlockView>(LabErrors.HoldOutUnlooked(start));
        }

        return await ReleaseAsync(target, OwnerMode, actor, cancellationToken);
    }

    /// <summary>
    /// The auto release (D4): only when <c>AutoRelease</c> is on, the block in force is sealed and has a fresh look, and no registered kind still has a look available. A kind whose read is <c>Unavailable</c> keeps the block. Never a forfeit. The seal step (HO-01b) calls this
    /// each evening.
    /// </summary>
    public async ValueTask<AutoReleaseResult> TryAutoAsync(CancellationToken cancellationToken)
    {
        if (!options.AutoRelease)
        {
            return new AutoReleaseResult(false, "off");
        }

        if (await epochs.InForceAsync(cancellationToken) is not { } current)
        {
            return new AutoReleaseResult(false, "no_block");
        }

        var kinds = new List<LookAvailability>();
        foreach (var slot in slots)
        {
            kinds.Add(await slot.ReadAsync(current.Block.Start, cancellationToken));
        }

        if (!HoldOutAutoRelease.Eligible(current.State, kinds, out var reason))
        {
            return new AutoReleaseResult(false, reason);
        }

        var released = await ReleaseAsync(current, AutoMode, HoldOutAutoRelease.Actor, cancellationToken);
        return new AutoReleaseResult(released.IsSuccess, released.IsSuccess ? string.Empty : released.Error.Code);
    }

    private async ValueTask<Result<HoldOutBlockView>> ReleaseAsync(BlockStatus target, string mode, string actor, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var hash = options.SnapshotHash();
        var (nextStart, nextEnd) = HoldOutChain.Next(target.End, options.BlockMonths);
        var next = new HoldOutBlock(nextStart, target.Block.HistoryFirst, nextEnd, options.BlockMonths, hash, now, target.Block.Start);
        var release = new StoredRelease(target.Block.Start, now, mode, actor, hash, target.State == HoldOutBlockState.Looked, nextStart);
        var outcome = await store.ReleaseAsync(release, next, cancellationToken);
        return outcome switch
        {
            ReleaseOutcome.Released => Result.Success(BlockViews.Of(new BlockStatus(target.Stored with { Release = release }, HoldOutBlockState.Released, target.FreshLook))),
            ReleaseOutcome.AlreadyReleased => Result.Failure<HoldOutBlockView>(LabErrors.HoldOutReleased(target.Block.Start)),
            _ => Result.Failure<HoldOutBlockView>(LabErrors.HoldOutUnavailable("The next block could not be stored, so nothing was released.")),
        };
    }
}
