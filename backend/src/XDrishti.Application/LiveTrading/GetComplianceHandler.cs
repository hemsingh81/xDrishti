using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Market;

namespace XDrishti.Application.LiveTrading;

/// <summary>Reads the compliance records, newest first, with the newest of each kind (the one the P11 phase check reads). Read only.</summary>
internal sealed class GetComplianceHandler(IComplianceStore store, ILiveWritesGate gate, TimeProvider clock) : IQueryHandler<GetComplianceQuery, Result<ComplianceView>>
{
    public const int MaxLimit = 200;

    public async ValueTask<Result<ComplianceView>> HandleAsync(GetComplianceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Kind is not null && !ComplianceKinds.All.Contains(query.Kind))
        {
            return Result.Failure<ComplianceView>(Error.Validation(ComplianceRecord.InvalidCode, "kind is not one of static_ip, sebi_recheck, dhan_ip_read_policy"));
        }

        if (query.Limit is < 1 or > MaxLimit)
        {
            return Result.Failure<ComplianceView>(Error.Validation(ComplianceRecord.InvalidCode, $"limit is 1 to {MaxLimit}"));
        }

        var today = IndiaTime.Today(clock.GetUtcNow());
        var latest = await store.LatestPerKindAsync(cancellationToken);
        var items = await store.ListAsync(query.Kind, query.Limit, cancellationToken);
        var newest = latest.ToDictionary(s => s.Record.Kind, StringComparer.Ordinal);
        ComplianceRecordView? Of(string kind) => newest.TryGetValue(kind, out var s) ? View(s, today, true) : null;
        var latestView = new ComplianceLatest(Of(ComplianceKinds.StaticIp), Of(ComplianceKinds.SebiRecheck), Of(ComplianceKinds.DhanIpReadPolicy));
        return Result.Success(new ComplianceView([.. items.Select(s => View(s, today, newest.TryGetValue(s.Record.Kind, out var n) && n.Id == s.Id))], latestView, gate.Enabled));
    }

    /// <summary>Overdue is shown on the newest record of its kind only: an older record has been superseded.</summary>
    internal static ComplianceRecordView View(StoredCompliance s, DateOnly today, bool isNewestOfKind)
    {
        var r = s.Record;
        return new ComplianceRecordView(s.Id, r.Kind, r.Outcome, r.DecidedOn, r.Summary, r.Sources, r.StaticIpState, r.Isp, r.MonthlyCost, r.IpVersion, r.WhitelistedAt, r.LockEndsOn, r.NextReviewOn, isNewestOfKind && ComplianceRules.Overdue(r, today), s.Actor, s.CreatedAt);
    }
}
