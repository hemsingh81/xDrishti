using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>
/// The automatic dismissal (BK-10b, default OFF, ADR 0091 and 0219): after a plan is published and only with <c>plans.loosening_banner.auto_dismiss</c> ON, the loosenings that a published plan already showed are dismissed with the actor <c>auto</c>, the id of the setting row in force and its hash
/// (never as the owner). It reaches only the dismissal store: no limit, no plan and no setting is changed. A failure is logged and leaves the banner shown, the safe side. Nothing here places an order.
/// </summary>
internal sealed class LooseningAutoDismissal(ILooseningStore store, IAccountEventLog events, IAccountUnitOfWork unitOfWork, TimeProvider clock, ILogger<LooseningAutoDismissal> logger)
{
    /// <summary>Dismisses what is due at the build of <paramref name="planId"/>; returns how many were dismissed.</summary>
    public async ValueTask<int> RunAsync(long planId, CancellationToken cancellationToken)
    {
        try
        {
            var setting = await store.CurrentSettingAsync(cancellationToken);
            if (!setting.AutoDismiss || setting.Id is not { } settingId)
            {
                return 0;
            }

            var due = LooseningNotices.AutoDismissible(await store.ListUndismissedAsync(cancellationToken), true, await store.PreviousPlanCreatedAtAsync(planId, cancellationToken));
            var count = 0;
            foreach (var notice in due)
            {
                if (await store.AddDismissalAsync(new NewLooseningDismissal(notice.Source, notice.Id, LooseningNotices.AutoActor, settingId, setting.Hash), cancellationToken))
                {
                    count++;
                    await LooseningAudit.RecordAsync(events, unitOfWork, clock, logger, AccountEventType.LooseningDismissed, string.Create(CultureInfo.InvariantCulture, $"notice={notice.NoticeId} source={notice.Source} key={notice.Key} setting={settingId} hash={setting.Hash}"), LooseningNotices.AutoActor);
                }
            }

            if (count > 0)
            {
                LooseningLog.AutoDismissed(logger, count, planId);
            }

            return count;
        }
#pragma warning disable CA1031 // the plan is published; a failed automatic dismissal leaves the banner shown, the safe side
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            LooseningLog.AutoDismissFailed(logger, planId, failure.GetType().Name);
            return 0;
        }
    }
}
