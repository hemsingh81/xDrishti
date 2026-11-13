using System.Globalization;
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
/// The bell notices of the system replay (SR-03 D5), derived on read: (1) the block in force is sealed and its newest registration has no result because a stored input drifted; (2) the block in force is sealed, nothing targets the block after it, and no checked charge sheet exists
/// (so the auto registration cannot act). Words and dates only; nothing is stored or changed by reading. A read that fails shows nothing.
/// </summary>
internal sealed class SystemReplayNotices(HoldOutEpochs epochs, IHoldOutRegistrationStore store, HoldOutInputCheck inputs, ICostSheetStore sheets, HoldOutOptions options) : IBellNoticeSource
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
            && ReplayJson.Read<HoldOutRegistrationConfig>(newest.ConfigJson) is { } config
            && await inputs.DriftAsync(config, cancellationToken) is { } drift)
        {
            notices.Add(new PlanNotice($"system_replay:drift:{newest.Id:N}", Kind, "warning", $"The registered system replay of the block that starts {start} cannot run: {drift}. Nothing was claimed.", "/replay", null, null, null));
        }

        var next = HoldOutChain.Next(current.End, options.BlockMonths);
        if (next.Start > IndiaTime.Today(now) && (await store.ForBlockAsync(next.Start, cancellationToken)).Count == 0 && (await sheets.ListCheckedAsync(cancellationToken)).Count == 0)
        {
            notices.Add(new PlanNotice($"system_replay:no_sheet:{next.Start:yyyy-MM-dd}", Kind, "warning", $"No system replay is registered for the block that starts {next.Start:yyyy-MM-dd} and no charge sheet is checked, so none can be (ISS-25).", "/replay", null, null, null));
        }

        return notices;
    }
}
