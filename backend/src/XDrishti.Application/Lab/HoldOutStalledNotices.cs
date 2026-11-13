using System.Globalization;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Lab;

/// <summary>
/// The bell notice of a stalled hold-out block (HO-01 D4), derived on read like the other notices (P8-15): the block in force has been sealed for <see cref="HoldOutStall.Sessions"/> sessions or more without a release, and the notice names what keeps the auto release
/// from acting (<see cref="HoldOutReleaser.WhatHoldsAsync"/>). It is present while the cause stands; it carries words and a date, no figure of the evidence. A read that fails shows nothing (the next read tries again). Nothing is stored or changed by reading it.
/// </summary>
internal sealed class HoldOutStalledNotices(HoldOutEpochs epochs, HoldOutReleaser releaser) : IBellNoticeSource
{
    public const string Kind = "holdout_stalled";

    public async ValueTask<IReadOnlyList<PlanNotice>> ListAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await epochs.InForceAsync(cancellationToken) is not { } current || current.State is not (HoldOutBlockState.Sealed or HoldOutBlockState.Looked))
        {
            return [];
        }

        var sessions = await epochs.SealedSessionsAsync(current.End, cancellationToken);
        if (!HoldOutStall.IsStalled(sessions))
        {
            return [];
        }

        var (reason, _) = await releaser.WhatHoldsAsync(current, cancellationToken, needKinds: false);
        if (reason is null)
        {
            return [];
        }

        var start = current.Block.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return
        [
            new PlanNotice(
                $"holdout:stalled:{start}:{reason}",
                Kind,
                "warning",
                $"The hold-out block that starts {start} sealed {sessions.ToString(CultureInfo.InvariantCulture)} sessions ago and is not released, so no new session reaches learning. {Why(reason)}",
                "/lab",
                null,
                null,
                null),
        ];
    }

    private static string Why(string reason) => reason switch
    {
        "unlooked" => "No fresh look was taken: take one, or release the block yourself and forfeit its looks.",
        "look_available" => "A look is still available (a strategy version, a model or a system run could take it); take it, or release the block yourself.",
        "unavailable" => "A read failed, so the automatic release keeps the block; release it yourself or check the services.",
        "no_strategy_slot" => "The strategy look is not registered, so the automatic release cannot decide; release it yourself.",
        "auto_release_off" => "The automatic release is off; release the block yourself.",
        _ => "Release the block yourself.",
    };
}
