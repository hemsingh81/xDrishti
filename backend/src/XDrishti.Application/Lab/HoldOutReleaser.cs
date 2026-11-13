using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Domain.Accounts;
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
internal sealed class HoldOutReleaser(HoldOutEpochs epochs, IHoldOutStore store, IEnumerable<IHoldOutLookSlot> slots, HoldOutOptions options, TimeProvider time, IAccountEventLog events, IAccountUnitOfWork unitOfWork, ILogger<HoldOutReleaser> logger)
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

        // fail closed: the strategy look is the one kind HO-01b registers first; without its slot the rule cannot know whether a strategy look is still available
        if (!slots.Any(s => string.Equals(s.Kind, "strategy", StringComparison.Ordinal)))
        {
            return new AutoReleaseResult(false, "no_strategy_slot");
        }

        var kinds = (await ReadSlotsAsync(current.Block.Start, cancellationToken)).Values.ToList();

        if (!HoldOutAutoRelease.Eligible(current.State, kinds, out var reason))
        {
            return new AutoReleaseResult(false, reason);
        }

        var released = await ReleaseAsync(current, AutoMode, HoldOutAutoRelease.Actor, cancellationToken);
        return new AutoReleaseResult(released.IsSuccess, released.IsSuccess ? string.Empty : released.Error.Code);
    }

    /// <summary>What each kind can still do at the block (HO-01 D4): a registered slot answers, a kind with no slot has no subject, and a slot that fails answers <see cref="LookAvailability.Unavailable"/>, which keeps the block.</summary>
    public async ValueTask<IReadOnlyDictionary<string, LookAvailability>> ReadSlotsAsync(DateOnly blockStart, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, LookAvailability>(StringComparer.Ordinal)
        {
            ["strategy"] = LookAvailability.NoSubject,
            ["model"] = LookAvailability.NoSubject,
            [HoldOutHorizon.SystemKind] = LookAvailability.NoSubject,
        };
        foreach (var slot in slots)
        {
            try
            {
                result[slot.Kind] = await slot.ReadAsync(blockStart, cancellationToken);
            }
            catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
            {
                LabRunLog.HoldOutSlotFailed(logger, slot.Kind, failure.GetType().Name);
                result[slot.Kind] = LookAvailability.Unavailable;
            }
        }

        return result;
    }

    /// <summary>What keeps the auto release from acting on the block in force, as a word (<c>auto_release_off</c>, <c>no_strategy_slot</c>, <c>filling</c>, <c>unlooked</c>, <c>look_available</c>, <c>unavailable</c>), or null when it would release now. Read only.</summary>
    public async ValueTask<(string? Reason, IReadOnlyDictionary<string, LookAvailability> Kinds)> WhatHoldsAsync(BlockStatus current, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);
        // a filling or released block is answered without reading any slot (the card and the bell read this often)
        if (current.State is HoldOutBlockState.Filling or HoldOutBlockState.Released)
        {
            return (HoldOutChain.Word(current.State), new Dictionary<string, LookAvailability>(StringComparer.Ordinal));
        }

        var kinds = await ReadSlotsAsync(current.Block.Start, cancellationToken);
        if (!slots.Any(s => string.Equals(s.Kind, "strategy", StringComparison.Ordinal)))
        {
            return ("no_strategy_slot", kinds);
        }

        if (!HoldOutAutoRelease.Eligible(current.State, [.. kinds.Values], out var reason))
        {
            return (reason, kinds);
        }

        return (options.AutoRelease ? null : "auto_release_off", kinds);
    }

    private async ValueTask<Result<HoldOutBlockView>> ReleaseAsync(BlockStatus target, string mode, string actor, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var hash = options.SnapshotHash();
        var (nextStart, nextEnd) = HoldOutChain.Next(target.End, options.BlockMonths);
        var next = new HoldOutBlock(nextStart, target.Block.HistoryFirst, nextEnd, options.BlockMonths, hash, now, target.Block.Start);
        var release = new StoredRelease(target.Block.Start, now, mode, actor, hash, target.State == HoldOutBlockState.Looked, nextStart);
        var outcome = await store.ReleaseAsync(release, next, cancellationToken);
        if (outcome == ReleaseOutcome.Released)
        {
            await AuditAsync(release, now);
        }

        return outcome switch
        {
            ReleaseOutcome.Released => Result.Success(BlockViews.Of(new BlockStatus(target.Stored with { Release = release }, HoldOutBlockState.Released, target.FreshLooks))),
            ReleaseOutcome.AlreadyReleased => Result.Failure<HoldOutBlockView>(LabErrors.HoldOutReleased(target.Block.Start)),
            _ => Result.Failure<HoldOutBlockView>(LabErrors.HoldOutUnavailable("The next block could not be stored, so nothing was released.")),
        };
    }

    /// <summary>The audit cross-index row, as the unlock writes it: ids and state words only. A failure of this second write is logged and does not undo the release, which is insert-only and already committed.</summary>
    private async ValueTask AuditAsync(StoredRelease release, DateTimeOffset now)
    {
        try
        {
            events.Record(AccountEvent.Record(AccountEventType.LabHoldOutReleased, null, string.Create(CultureInfo.InvariantCulture, $"block={release.BlockStart:yyyy-MM-dd} mode={release.Mode} looked={(release.Looked ? "yes" : "no")} next={release.NextBlockStart:yyyy-MM-dd}"), release.Actor, now));
            await unitOfWork.TrySaveChangesAsync(CancellationToken.None);
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
        {
            LabRunLog.HoldOutReleaseAuditFailed(logger, release.BlockStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), failure.GetType().Name);
        }
    }
}
