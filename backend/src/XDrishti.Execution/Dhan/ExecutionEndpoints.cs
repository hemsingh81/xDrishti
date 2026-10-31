using System.Text.RegularExpressions;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Dhan;

/// <summary>The calls built so far: six order calls (the super-order entry, UNVERIFIED, is the one added in P12-04), four read-only GETs (order book, trade book, positions, funds) and the two readiness GETs (profile, configured static IP; P12-11). Everything not named here is refused (super-order changes, slicing, the kill switch and the rest of the spec's table are later slices).</summary>
public enum DhanCall
{
    OrdersPost,
    SuperOrdersPost,
    OrderPut,
    OrderDelete,
    OrderGet,
    OrderByCorrelationGet,
    OrdersGet,
    TradesGet,
    PositionsGet,
    FundsGet,
    ProfileGet,
    StaticIpGet,
}

/// <summary>
/// The single guard table and the only file with order paths and the only file with a host name (P12-02, ADR 0157). A request passes only when: the environment is sandbox, the scheme is https, the host is exactly the sandbox host, the port is the
/// default, there is no user info, no query, no fragment, the method and the path match one row, and the id is a plain token. The live environment passes nothing in this slice and no live host string exists in this project.
/// </summary>
public static partial class ExecutionEndpoints
{
    /// <summary>Taken from a third-party description (marketcalls.in, 2025-05-19) and not verified by Dhan's own pages: UNVERIFIED until the owner's first sandbox smoke.</summary>
    public const string SandboxHost = "sandbox.dhan.co";

    public const string AccessTokenHeader = "access-token";

    private static readonly (DhanCall Call, HttpMethod Method, string Prefix, bool HasId)[] Table =
    [
        (DhanCall.OrdersPost, HttpMethod.Post, "/v2/orders", false),
        (DhanCall.SuperOrdersPost, HttpMethod.Post, "/v2/super/orders", false),
        (DhanCall.OrderPut, HttpMethod.Put, "/v2/orders/", true),
        (DhanCall.OrderDelete, HttpMethod.Delete, "/v2/orders/", true),
        (DhanCall.OrderGet, HttpMethod.Get, "/v2/orders/", true),
        (DhanCall.OrderByCorrelationGet, HttpMethod.Get, "/v2/orders/external/", true),
        (DhanCall.OrdersGet, HttpMethod.Get, "/v2/orders", false),
        (DhanCall.TradesGet, HttpMethod.Get, "/v2/trades", false),
        (DhanCall.PositionsGet, HttpMethod.Get, "/v2/positions", false),
        (DhanCall.FundsGet, HttpMethod.Get, "/v2/fundlimit", false),
        (DhanCall.ProfileGet, HttpMethod.Get, "/v2/profile", false),
        (DhanCall.StaticIpGet, HttpMethod.Get, "/v2/ip/getIP", false),
    ];

    [GeneratedRegex("^[A-Za-z0-9_-]{1,40}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex IdPattern();

    public static HttpMethod MethodOf(DhanCall call) => Table.First(t => t.Call == call).Method;

    /// <summary>The path template of a call for the audit log: the fixed prefix, with <c>{id}</c> in place of any id value. Never a host, a query or an id.</summary>
    public static string TemplateOf(DhanCall call)
    {
        var row = Table.First(t => t.Call == call);
        return row.HasId ? row.Prefix + "{id}" : row.Prefix;
    }

    /// <summary>The absolute URI for a call, or null when the environment does not allow it or the id is not a plain token.</summary>
    public static Uri? Build(ExecEnvironment environment, DhanCall call, string? id = null)
    {
        if (environment != ExecEnvironment.Sandbox)
        {
            return null;
        }

        var row = Table.First(t => t.Call == call);
        if (row.HasId != (id is not null) || (id is not null && !IdPattern().IsMatch(id)))
        {
            return null;
        }

        return new Uri($"https://{SandboxHost}{row.Prefix}{id}", UriKind.Absolute);
    }

    /// <summary>Whether this exact request (method and URI) is one of the rows for the environment. Used by the handler on every outgoing request.</summary>
    public static bool IsAllowed(ExecEnvironment environment, HttpMethod method, Uri? uri)
    {
        if (environment != ExecEnvironment.Sandbox || uri is null || !uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || !string.Equals(uri.Host, SandboxHost, StringComparison.Ordinal)
            || !uri.IsDefaultPort || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
        {
            return false;
        }

        var path = uri.AbsolutePath;
        foreach (var row in Table.Where(t => t.Method == method))
        {
            if (!row.HasId)
            {
                if (path == row.Prefix)
                {
                    return true;
                }

                continue;
            }

            if (path.StartsWith(row.Prefix, StringComparison.Ordinal) && IdPattern().IsMatch(path[row.Prefix.Length..]))
            {
                return true;
            }
        }

        return false;
    }
}
