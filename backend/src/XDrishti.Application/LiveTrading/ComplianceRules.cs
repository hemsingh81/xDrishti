using System.Globalization;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Planning;
using XDrishti.Domain.LiveTrading;

namespace XDrishti.Application.LiveTrading;

/// <summary>The derived parts of the compliance record, pure (spec P11-05).</summary>
internal static class ComplianceRules
{
    public const string OverdueKind = "compliance_recheck_overdue";
    public const string Route = "/live?tab=compliance";

    /// <summary>A record is overdue the day after its review date passes while it is the newest of its kind; a newer record of the kind resolves it.</summary>
    public static bool Overdue(ComplianceRecord latest, DateOnly today) => latest.NextReviewOn < today;

    /// <summary>One bell notice per kind whose newest record is overdue. A kind with no record has none (nothing is recorded yet). Words and dates only; route only, no command.</summary>
    public static IReadOnlyList<PlanNotice> Notices(IReadOnlyList<StoredCompliance> latestPerKind, DateOnly today) =>
    [
        .. latestPerKind
            .Where(s => Overdue(s.Record, today))
            .OrderBy(s => s.Record.NextReviewOn)
            .Select(s => new PlanNotice(
                $"compliance:overdue:{s.Record.Kind}:{s.Record.NextReviewOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}",
                OverdueKind,
                "warning",
                $"Compliance re-check overdue: {ComplianceKinds.Label(s.Record.Kind)} was due on {s.Record.NextReviewOn.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}.",
                Route,
                null,
                null,
                null)),
    ];
}
