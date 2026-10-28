using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;

namespace XDrishti.Application.LiveTrading;

/// <summary>The words, limits and small helpers the two report handlers share (spec P11-03).</summary>
public static class LiveReportRules
{
    public const int MaxRangeDays = 366;
    public const int DefaultRangeDays = 84;
    public const int MaxPageSize = 200;
    public const string PaperAccountRequired = "live.paper_account_required";

    public const string LiveOnly = "live_only";
    public const string PaperOnly = "paper_only";
    public const string LiveOpen = "live_open";
    public const string PaperOpen = "paper_open";
    public const string NoPlanLevels = "no_plan_levels";
    public const string PlanNotGraded = "plan_not_graded";
    public const string Unknown = "unknown";

    public const string PairRule = "Same plan item on the live account and one paper account of the current epoch; R per share on the plan's own risk per share; a ticket on one side only is listed with its reason, never dropped.";

    internal static readonly IReadOnlyList<string> Limits =
    [
        "Paired tickets are a selected sample: those the owner took and paper filled. The report counts and lists what was left out and why.",
        "The paper fill is itself modelled (official 1-minute bars with the standard slippage), so live minus paper is live minus a model, not live minus a market.",
        "R per share removes a difference in quantity, not a difference in the minute of entry. Gross and net are never mixed in one mean.",
        "Tens of pairs give an interval of roughly +-0.05 R on a sd of 0.15; a narrower claim is not available. Below the minimum pairs there is no verdict.",
        "Live charges are Dhan's recorded lines (delivery fills only today); paper charges are the stored sheet's model.",
    ];

    internal static Result<(DateOnly From, DateOnly To)> Range(DateOnly? from, DateOnly? to, DateOnly today)
    {
        var end = to ?? today;
        var start = from ?? end.AddDays(-DefaultRangeDays);
        if (start > end)
        {
            return Result.Failure<(DateOnly, DateOnly)>(Error.Validation(LiveErrors.Invalid, "from must not be after to"));
        }

        return end.DayNumber - start.DayNumber > MaxRangeDays
            ? Result.Failure<(DateOnly, DateOnly)>(Error.Validation(LiveErrors.Invalid, $"the range must be at most {MaxRangeDays} days"))
            : Result.Success((start, end));
    }

    internal static MetricView Value(double v) => new("value", v, null);

    internal static MetricView Insufficient(string reason) => new("insufficient", null, reason);

    internal static MetricView Unavailable(string reason) => new("unavailable", null, reason);

    internal static LiveReportEvidenceView Evidence(string basis, int n, DateOnly? first, DateOnly? last, IReadOnlyList<string> kinds, Guid? paperAccount, long? epoch, IReadOnlyDictionary<string, int> exclusions) =>
        new(basis, n, first, last, kinds, paperAccount, epoch, PairRule, [.. exclusions.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => new ExclusionCountView(e.Key, e.Value))], Limits);
}
