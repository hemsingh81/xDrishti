using System.Globalization;
using System.Text.RegularExpressions;

namespace XDrishti.Application.Portfolio.Attention;

/// <summary>
/// One command target a notice may carry (ADR 0027, ADR 0030): the HTTP <paramref name="Method"/>, the path <paramref name="Template"/> with its placeholders (<c>{account}</c>, a guid; <c>{service}</c>, a service id) and the whole-path <paramref name="Pattern"/> (a regular
/// expression valid in .NET and in JavaScript) the template stands for. The pattern lets letters, digits and hyphens through in the placeholders only, so encoded dots or slashes, backslashes, dot-segments, double slashes, queries and fragments cannot match.
/// </summary>
public sealed record AttentionCommandTarget(string Name, string Method, string Template, string Pattern);

/// <summary>
/// The one list of the commands a notice may carry (ADR 0030 §6): the deriver builds its targets only through this class, the contract document of the API publishes the list (<c>x-xdrishti-attention-commands</c>), and <c>scripts/gen-api.sh</c> writes the client's allow-list
/// (<c>attentionCommands.generated.ts</c>) from it. A new kind of command is one line in <see cref="Allowed"/> and a builder method; nothing is copied by hand on the client.
/// </summary>
public static class AttentionCommands
{
    private const string GuidPattern = "[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}";

    private const string ServiceIdPattern = "[A-Za-z0-9][A-Za-z0-9-]{0,63}";

    private const string AccountToken = "{account}";

    private const string ServiceToken = "{service}";

    private static readonly Regex ServiceIdOnly = new("^" + ServiceIdPattern + "\\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>The holdings sync of an account (<c>POST /api/accounts/{id}/holdings/sync</c>).</summary>
    public static AttentionCommandTarget HoldingsSync { get; } = Create("holdings_sync", "POST", "/api/accounts/" + AccountToken + "/holdings/sync");

    /// <summary>The trade and ledger read of an account (<c>POST /api/accounts/{id}/sync/broker-history</c>).</summary>
    public static AttentionCommandTarget BrokerHistorySync { get; } = Create("broker_history_sync", "POST", "/api/accounts/" + AccountToken + "/sync/broker-history");

    /// <summary>A run of a service now (<c>POST /api/services/{id}/run</c>).</summary>
    public static AttentionCommandTarget ServiceRun { get; } = Create("service_run", "POST", "/api/services/" + ServiceToken + "/run");

    /// <summary>Every command target a notice may carry, in a stable order.</summary>
    public static IReadOnlyList<AttentionCommandTarget> Allowed { get; } = [HoldingsSync, BrokerHistorySync, ServiceRun];

    /// <summary>The target text of the holdings sync of an account.</summary>
    public static string HoldingsSyncOf(Guid accountId) => Target(HoldingsSync, AccountToken, accountId.ToString("D", CultureInfo.InvariantCulture));

    /// <summary>The target text of the trade and ledger read of an account.</summary>
    public static string BrokerHistorySyncOf(Guid accountId) => Target(BrokerHistorySync, AccountToken, accountId.ToString("D", CultureInfo.InvariantCulture));

    /// <summary>The target text of the run of a service; the id must be letters, digits and hyphens.</summary>
    public static string ServiceRunOf(string serviceId)
    {
        ArgumentNullException.ThrowIfNull(serviceId);
        if (!ServiceIdOnly.IsMatch(serviceId))
        {
            throw new ArgumentException("A service id is letters, digits and hyphens only.", nameof(serviceId));
        }

        return Target(ServiceRun, ServiceToken, serviceId);
    }

    /// <summary>True when <paramref name="target"/> (<c>METHOD /path</c>) is one of the <see cref="Allowed"/> commands, whole.</summary>
    public static bool IsAllowed(string target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Contains('\n', StringComparison.Ordinal) || target.Contains('\r', StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var command in Allowed)
        {
            var prefix = command.Method + " ";
            if (target.StartsWith(prefix, StringComparison.Ordinal) && Regex.IsMatch(target[prefix.Length..], command.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            {
                return true;
            }
        }

        return false;
    }

    private static string Target(AttentionCommandTarget command, string token, string value) => command.Method + " " + command.Template.Replace(token, value, StringComparison.Ordinal);

    private static AttentionCommandTarget Create(string name, string method, string template) =>
        new(name, method, template, "^" + template.Replace(AccountToken, GuidPattern, StringComparison.Ordinal).Replace(ServiceToken, ServiceIdPattern, StringComparison.Ordinal) + "$");
}
