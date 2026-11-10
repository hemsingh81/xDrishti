using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Baskets;
using XDrishti.Domain.Market.Baskets.Lists;

namespace XDrishti.Application.Market.Baskets;

/// <summary>
/// Refreshes the four lists read from the Dhan master (F&amp;O stocks, ASM/GSM, MTF-eligible, SME and trade-for-trade; BK-05) after a master refresh: <paramref name="MasterOk"/> true builds the sets from the stored master and applies the changes dated that India day
/// (provenance <c>derived</c>, source <c>refresh</c> = <paramref name="RefreshId"/>); false records a failed check and keeps the previous membership. No network, no order; a failure here never touches the master.
/// </summary>
public sealed record RefreshFlagListsCommand(bool MasterOk, Guid? RefreshId, string Actor) : ICommand;

/// <summary>One outcome per list basket that was checked.</summary>
public sealed record FlagListRun(IReadOnlyList<FetchOutcomeView> Outcomes);

internal sealed class RefreshFlagListsHandler(
    IBasketSourceStore sources,
    IListMasterReader master,
    IBasketStore baskets,
    IBasketMembershipApplier applier,
    TimeProvider clock,
    ILogger<RefreshFlagListsHandler> logger) : ICommandHandler<RefreshFlagListsCommand, FlagListRun>
{
    public async ValueTask<Result<FlagListRun>> HandleAsync(RefreshFlagListsCommand command, CancellationToken cancellationToken)
    {
        var started = clock.GetTimestamp();
        var now = clock.GetUtcNow();
        var today = IndiaTime.Today(now);
        var catalogue = (await sources.ListAsync([.. ListBaskets.FromMaster.Select(d => d.Id)], cancellationToken)).ToDictionary(s => s.BasketId);
        var outcomes = new List<FetchOutcomeView>();

        IReadOnlyDictionary<string, IReadOnlyList<ListMember>>? sets = null;
        if (command.MasterOk)
        {
            sets = FlagListBuilder.Build((await master.ReadSharesAsync(today, cancellationToken)).Select(s => s.Flags));
        }

        foreach (var definition in ListBaskets.FromMaster)
        {
            if (!catalogue.TryGetValue(definition.Id, out var source))
            {
                continue;
            }

            var record = sets is null
                ? await FailAsync(source, command, now, cancellationToken)
                : await ApplyAsync(source, definition, sets[definition.Key], command, now, today, cancellationToken);
            var milliseconds = (long)clock.GetElapsedTime(started).TotalMilliseconds;
            BasketLog.ListRefreshed(logger, definition.Id, record.Outcome, record.Rows, record.Unmatched, milliseconds);
            outcomes.Add(new FetchOutcomeView(record.BasketId, record.FetchedAt, record.Outcome, null, record.Rows, record.Added, record.Removed, 0, record.Error, false));
        }

        return Result.Success(new FlagListRun(outcomes));
    }

    private async ValueTask<FetchRecord> ApplyAsync(
        BasketSourceInfo source, ListBasketDefinition definition, IReadOnlyList<ListMember> incoming, RefreshFlagListsCommand command, DateTimeOffset now, DateOnly today, CancellationToken cancellationToken)
    {
        var current = (await baskets.ListActiveMembersAsync(source.BasketId, cancellationToken)).Select(m => m.InstrumentId).ToList();
        var order = incoming.Select(m => m.InstrumentId).ToList();
        var change = MembershipDiff.Compute(current, order);
        var fetch = Record(source, now, command.Actor, FetchOutcomes.Applied, order.Count, change.Added.Count, change.Removed.Count, null);
        var update = Updated(source, now, ok: true);
        if (change.Added.Count == 0 && change.Removed.Count == 0)
        {
            var same = fetch with { Outcome = FetchOutcomes.Unchanged };
            await sources.RecordAsync(update, same, cancellationToken);
            return same;
        }

        var effective = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).ToUniversalTime();
        var origin = new ListOrigin(BasketProvenance.Derived, "refresh", incoming.ToDictionary(m => m.InstrumentId, m => m.Note), command.RefreshId ?? fetch.Id);
        var done = await applier.ApplyAsync(new MembershipApplication(source.BasketId, definition.Name, change, order, effective, now, update, fetch, origin), cancellationToken);
        return done ? fetch : fetch with { Outcome = FetchOutcomes.Failed, Error = "concurrent_change", Added = 0, Removed = 0 };
    }

    private async ValueTask<FetchRecord> FailAsync(BasketSourceInfo source, RefreshFlagListsCommand command, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var failed = Record(source, now, command.Actor, FetchOutcomes.Failed, source.CurrentMembers, 0, 0, "master_failed");
        await sources.RecordAsync(Updated(source, now, ok: false), failed, cancellationToken);
        return failed;
    }

    private static FetchRecord Record(BasketSourceInfo source, DateTimeOffset now, string actor, string outcome, int rows, int added, int removed, string? error) =>
        new(Guid.CreateVersion7(now), source.BasketId, now, null, null, null, null, rows, outcome, added, removed, 0, "[]", error, false, actor, null);

    private static SourceUpdate Updated(BasketSourceInfo source, DateTimeOffset now, bool ok) =>
        new(source.BasketId, source.Enabled, ok ? BasketSourceStates.Ok : BasketSourceStates.Failed, now, ok ? now : source.LastOkAt, null, null, null, null, ok ? 0 : source.FailCount + 1);
}
