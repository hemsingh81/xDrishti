using System.Globalization;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Lab;
using XDrishti.Application.Planning;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Replay.HoldOut;

/// <summary>
/// The bell notices of the system replay (SR-03 D5), derived on read: (1) the block in force is sealed and its newest registration has no look yet because a stored input drifted (a claimed look is not checked again); (2) the block after the one in force has no registration, before it
/// begins, while the auto registration is off or unavailable or no charge sheet is checked (it cannot act); (3) the block after the one in force began with no registration (the seal evening passed without one: it takes no system look). Words and dates only; nothing is stored or
/// changed by reading. A read that fails shows nothing.
/// </summary>
internal sealed class SystemReplayNotices(HoldOutEpochs epochs, IHoldOutRegistrationStore store, IHoldOutStore blocks, IHoldOutAutoSettings settings, HoldOutInputCheck inputs, ICostSheetStore sheets, HoldOutOptions options) : IBellNoticeSource
{
    public const string Kind = "system_replay";

    public async ValueTask<IReadOnlyList<PlanNotice>> ListAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await epochs.InForceAsync(cancellationToken) is not { } current || current.State is not (HoldOutBlockState.Sealed or HoldOutBlockState.Looked))
        {
            return [];
        }

        var notices = new List<PlanNotice>();
        var start = current.Block.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (await store.NewestForBlockAsync(current.Block.Start, cancellationToken) is { } newest
            && await store.GetResultAsync(newest.Id, cancellationToken) is null
            && await blocks.GetUnlockAsync(HoldOutRegistrationIds.LookKey(newest.Id), HoldOutRegistrationIds.LookVersion, HoldOutHorizon.SystemKind, current.Block.Start, cancellationToken) is null
            && ReplayJson.Read<HoldOutRegistrationConfig>(newest.ConfigJson) is { } config
            && await inputs.DriftAsync(config, cancellationToken) is { } drift)
        {
            notices.Add(new PlanNotice($"system_replay:drift:{newest.Id:N}", Kind, "warning", $"The registered system replay of the block that starts {start} cannot run: {drift}. Nothing was claimed.", "/simulation/replay", null, null, null));
        }

        var next = HoldOutChain.Next(current.End, options.BlockMonths);
        if (await store.NewestForBlockAsync(next.Start, cancellationToken) is not null)
        {
            return notices;
        }

        var nextText = next.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (next.Start <= IndiaTime.Today(now))
        {
            notices.Add(new PlanNotice($"system_replay:missed:{nextText}", Kind, "warning", $"The block that starts {nextText} began with no system replay registered, so it takes no system look.", "/simulation/replay", null, null, null));
        }
        else if ((await settings.ReadAsync(cancellationToken)).AutoRegister != true)
        {
            notices.Add(new PlanNotice($"system_replay:not_registering:{nextText}", Kind, "warning", $"No system replay is registered for the block that starts {nextText} and the automatic registration is off or unreadable. Register one yourself before it begins.", "/simulation/replay", null, null, null));
        }
        else if ((await sheets.ListCheckedAsync(cancellationToken)).Count == 0)
        {
            notices.Add(new PlanNotice($"system_replay:no_sheet:{nextText}", Kind, "warning", $"No system replay is registered for the block that starts {nextText} and no charge sheet is checked, so none can be (ISS-25).", "/simulation/replay", null, null, null));
        }

        return notices;
    }
}
