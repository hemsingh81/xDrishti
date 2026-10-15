using System.Security.Cryptography;
using System.Text;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Portfolio.Reports;

/// <summary>
/// The client code (UCC) a Dhan report prints in its header is not the Dhan client id of the account, so it cannot be compared with it. The code is remembered one way per account instead (ADR 0029 section 8): the first report that is imported with a code leaves its hash
/// (<see cref="Hash"/>: SHA-256 of the trimmed upper-case code with a fixed application salt - never the code); a later report whose hash differs is for another client code. An account with no remembered hash, and a report without a code, never warn. The code itself is never
/// stored, reported, audited or logged. The use cases call <see cref="RememberAsync"/> inside their unit of work, just before the save, so only a successful import remembers.
/// </summary>
internal sealed class ReportClientCodes(IReportClientCodes store)
{
    /// <summary>Makes the hash specific to this application, so it is not the plain SHA-256 of a code that anyone could look up.</summary>
    private const string Salt = "xdrishti.report-client-code.v1";

    /// <summary>The salted SHA-256 of the code, trimmed and in upper case, as 64 lower-case hexadecimal characters.</summary>
    public static string Hash(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Salt + "|" + code.Trim().ToUpperInvariant())));
    }

    /// <summary>True when the report has a client code, the account remembers one, and they differ. The codes themselves are never reported.</summary>
    public async ValueTask<bool> IsOtherAccountAsync(Guid accountId, string? reportCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reportCode))
        {
            return false;
        }

        var remembered = await store.FindHashAsync(accountId, cancellationToken);
        return remembered is not null && !string.Equals(remembered, Hash(reportCode), StringComparison.Ordinal);
    }

    /// <summary>
    /// Collects the hash of the report's code when the account remembers none yet (the caller's unit of work saves it). A remembered hash is never replaced: a report that the owner ticked as being for another client code leaves it as it is.
    /// </summary>
    public async ValueTask RememberAsync(Guid accountId, string? reportCode, DateTimeOffset at, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reportCode) || await store.FindHashAsync(accountId, cancellationToken) is not null)
        {
            return;
        }

        store.Add(ReportClientCode.Remember(accountId, Hash(reportCode), at));
    }
}
