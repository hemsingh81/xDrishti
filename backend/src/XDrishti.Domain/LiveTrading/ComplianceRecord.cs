using System.Globalization;
using System.Text.RegularExpressions;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.LiveTrading;

/// <summary>What the owner wrote, before the rules of the record are applied (spec P11-05). Dates are India dates.</summary>
public sealed record ComplianceDraft(
    string? Kind,
    string? Outcome,
    DateOnly DecidedOn,
    string? Summary,
    IReadOnlyList<string>? Sources,
    string? StaticIpState,
    string? Isp,
    decimal? MonthlyCost,
    string? IpVersion,
    DateOnly? WhitelistedAt,
    DateOnly NextReviewOn);

/// <summary>
/// One dated note of the owner's enquiry about the static IP and the SEBI rules (spec P11-05, ADR 0147): what was decided, from which sources, and when to look again. It is the owner's note of what they were told; nothing is checked at Dhan or the ISP.
/// It never holds an address, a client id or a token (<see cref="ComplianceScreens"/> on <c>summary</c> and <c>isp</c>). Created only through <see cref="Create"/>, which applies every rule; the lock end is derived here, never taken from a body.
/// </summary>
public sealed partial class ComplianceRecord
{
    public const string InvalidCode = "live.invalid";

    public const int MaxSummary = 1000;
    public const int MaxIsp = 80;
    public const int MaxSources = 10;
    public const int MaxSourceLength = 300;
    public const int MaxReviewDays = 400;
    public const int LockDays = 7;
    public const decimal MaxMonthlyCost = 1_000_000m;

    /// <summary>A source is an https URL (a host with a name, not an address) or a circular id.</summary>
    [GeneratedRegex(@"^(https://[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}(:443)?(/\S*)?|(SEBI|NSE|BSE)/[A-Za-z0-9/._-]{3,60})$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex SourcePattern();

    private ComplianceRecord()
    {
    }

    public string Kind { get; private init; } = string.Empty;
    public string Outcome { get; private init; } = string.Empty;
    public DateOnly DecidedOn { get; private init; }
    public string Summary { get; private init; } = string.Empty;
    public IReadOnlyList<string> Sources { get; private init; } = [];
    public string? StaticIpState { get; private init; }
    public string? Isp { get; private init; }
    public decimal? MonthlyCost { get; private init; }
    public string? IpVersion { get; private init; }
    public DateOnly? WhitelistedAt { get; private init; }

    /// <summary>Dhan's lock after an IP is set: the whitelisting date plus seven days. Derived here.</summary>
    public DateOnly? LockEndsOn { get; private init; }
    public DateOnly NextReviewOn { get; private init; }

    /// <summary>Applies every rule of the record. <paramref name="today"/> is the India date now. The failure code is <see cref="InvalidCode"/> and its message names the field.</summary>
    public static Result<ComplianceRecord> Create(ComplianceDraft draft, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var outcomes = ComplianceKinds.OutcomesOf(draft.Kind);
        if (outcomes.Count == 0)
        {
            return Invalid("kind", "is not one of static_ip, sebi_recheck, dhan_ip_read_policy");
        }

        if (draft.Outcome is null || !outcomes.Contains(draft.Outcome))
        {
            return Invalid("outcome", "is not allowed for this kind: " + string.Join(", ", outcomes));
        }

        if (draft.DecidedOn > today)
        {
            return Invalid("decidedOn", "is in the future");
        }

        if (draft.NextReviewOn <= draft.DecidedOn || draft.NextReviewOn > draft.DecidedOn.AddDays(MaxReviewDays))
        {
            return Invalid("nextReviewOn", $"must be after decidedOn and at most {MaxReviewDays.ToString(CultureInfo.InvariantCulture)} days ahead");
        }

        var summary = draft.Summary?.Trim() ?? string.Empty;
        if (summary.Length is 0 or > MaxSummary)
        {
            return Invalid("summary", $"needs 1 to {MaxSummary.ToString(CultureInfo.InvariantCulture)} characters");
        }

        if (ComplianceScreens.Reject(summary) is { } summaryReason)
        {
            return Invalid("summary", summaryReason + "; the record holds no address, client id or token");
        }

        var sources = draft.Sources ?? [];
        if (sources.Count > MaxSources)
        {
            return Invalid("sources", $"at most {MaxSources.ToString(CultureInfo.InvariantCulture)} items");
        }

        foreach (var source in sources)
        {
            if (source is null || source.Length > MaxSourceLength || !SourcePattern().IsMatch(source))
            {
                return Invalid("sources", $"each item is an https:// URL or a circular id such as NSE/INVG/73992, at most {MaxSourceLength.ToString(CultureInfo.InvariantCulture)} characters");
            }
        }

        var isStatic = draft.Kind == ComplianceKinds.StaticIp;
        var isp = string.IsNullOrWhiteSpace(draft.Isp) ? null : draft.Isp.Trim();
        if (!isStatic && (draft.StaticIpState is not null || isp is not null || draft.MonthlyCost is not null || draft.IpVersion is not null || draft.WhitelistedAt is not null))
        {
            return Invalid("staticIpState", "the static IP fields belong to the static_ip kind only");
        }

        if (isStatic)
        {
            var refused = StaticIpFields(draft, isp);
            if (refused is not null)
            {
                return refused;
            }
        }

        return Result.Success(new ComplianceRecord
        {
            Kind = draft.Kind!,
            Outcome = draft.Outcome,
            DecidedOn = draft.DecidedOn,
            Summary = summary,
            Sources = [.. sources],
            StaticIpState = isStatic ? draft.StaticIpState : null,
            Isp = isStatic ? isp : null,
            MonthlyCost = isStatic ? draft.MonthlyCost : null,
            IpVersion = isStatic ? draft.IpVersion : null,
            WhitelistedAt = isStatic && draft.StaticIpState == StaticIpStates.Whitelisted ? draft.WhitelistedAt : null,
            LockEndsOn = isStatic && draft.StaticIpState == StaticIpStates.Whitelisted ? draft.WhitelistedAt!.Value.AddDays(LockDays) : null,
            NextReviewOn = draft.NextReviewOn,
        });
    }

    /// <summary>Rebuilds a stored record (the database has already applied the same rules).</summary>
    public static ComplianceRecord Restore(string kind, string outcome, DateOnly decidedOn, string summary, IReadOnlyList<string> sources, string? staticIpState, string? isp, decimal? monthlyCost, string? ipVersion, DateOnly? whitelistedAt, DateOnly? lockEndsOn, DateOnly nextReviewOn) =>
        new()
        {
            Kind = kind,
            Outcome = outcome,
            DecidedOn = decidedOn,
            Summary = summary,
            Sources = sources,
            StaticIpState = staticIpState,
            Isp = isp,
            MonthlyCost = monthlyCost,
            IpVersion = ipVersion,
            WhitelistedAt = whitelistedAt,
            LockEndsOn = lockEndsOn,
            NextReviewOn = nextReviewOn,
        };

    private static Result<ComplianceRecord>? StaticIpFields(ComplianceDraft draft, string? isp)
    {
        if (draft.StaticIpState is null || !StaticIpStates.All.Contains(draft.StaticIpState))
        {
            return Invalid("staticIpState", "is required for a static_ip record: " + string.Join(", ", StaticIpStates.All));
        }

        if (isp is not null && (isp.Length > MaxIsp || ComplianceScreens.Reject(isp) is not null))
        {
            return Invalid("isp", ComplianceScreens.Reject(isp) is { } reason ? reason + "; the record holds no address, client id or token" : $"at most {MaxIsp.ToString(CultureInfo.InvariantCulture)} characters");
        }

        if (draft.MonthlyCost is < 0 or > MaxMonthlyCost)
        {
            return Invalid("monthlyCost", "is out of range");
        }

        if (draft.IpVersion is not null and not ("v4" or "v6"))
        {
            return Invalid("ipVersion", "is v4 or v6");
        }

        if (draft.StaticIpState == StaticIpStates.Whitelisted)
        {
            if (draft.WhitelistedAt is not { } at)
            {
                return Invalid("whitelistedAt", "is required when the state is whitelisted");
            }

            if (at > draft.DecidedOn)
            {
                return Invalid("whitelistedAt", "is after decidedOn");
            }
        }
        else if (draft.WhitelistedAt is not null)
        {
            return Invalid("whitelistedAt", "is allowed only when the state is whitelisted");
        }

        return null;
    }

    private static Result<ComplianceRecord> Invalid(string field, string message) => Result.Failure<ComplianceRecord>(Error.Validation(InvalidCode, $"{field} {message}"));
}
