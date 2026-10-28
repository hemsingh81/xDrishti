using System.Text.Json.Serialization;
using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.LiveTrading;

/// <summary>Reads the compliance records (spec P11-05): <paramref name="Kind"/> narrows the list (null = all kinds), <paramref name="Limit"/> is 1 to 200.</summary>
public sealed record GetComplianceQuery(string? Kind, int Limit) : IQuery<Domain.Common.Result<ComplianceView>>;

/// <summary>
/// Writes one compliance record as the owner. The actor is <c>owner:...</c> from the signed-in identity after a fresh password (the endpoint), never from a body. The fields are the owner's note of what they were told; nothing is checked at Dhan or the ISP.
/// </summary>
public sealed record RecordComplianceCommand(
    string Actor,
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
    DateOnly NextReviewOn) : ICommand;

/// <summary>One stored record as the API shows it.</summary>
public sealed record ComplianceRecordView(
    long Id,
    string Kind,
    string Outcome,
    DateOnly DecidedOn,
    string Summary,
    IReadOnlyList<string> Sources,
    string? StaticIpState,
    string? Isp,
    decimal? MonthlyCost,
    string? IpVersion,
    DateOnly? WhitelistedAt,
    DateOnly? LockEndsOn,
    DateOnly NextReviewOn,
    bool Overdue,
    string Actor,
    DateTimeOffset CreatedAt);

/// <summary>The newest record of each kind; null = nothing recorded for that kind (empty, not "no change").</summary>
public sealed record ComplianceLatest(
    [property: JsonPropertyName("static_ip")] ComplianceRecordView? StaticIp,
    [property: JsonPropertyName("sebi_recheck")] ComplianceRecordView? SebiRecheck,
    [property: JsonPropertyName("dhan_ip_read_policy")] ComplianceRecordView? DhanIpReadPolicy);

/// <summary>The list (newest first), the latest per kind and whether the web may write now (the capability gate Live:Writes:Enabled).</summary>
public sealed record ComplianceView(IReadOnlyList<ComplianceRecordView> Items, ComplianceLatest Latest, bool WritesEnabled);
