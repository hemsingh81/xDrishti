using System.Text.RegularExpressions;
using XDrishti.Api.IntegrationTests.Ai;

namespace XDrishti.Api.IntegrationTests.Execution;

/// <summary>
/// The static rules of <c>xd-exec</c> in <c>deploy/compose.yaml</c> (spec P12-01b, ADR 0156; ADR 0171): on demand only, no port, its own internal network (shared with the database alone), no route out, hardened, exactly two secrets (the role's password and the token
/// hand-over key), the database role xd_exec and no other; the password file reaches only xd-migrate (which sets the login) and xd-exec, the hand-over key only xd-api (which seals) and xd-exec, never xd-feed or xd-worker; nothing else joins the network or names the role;
/// the live readiness switch is off by default.
/// ADR 0172: the one route out is <c>xd-exec-proxy</c>, defined only in <c>deploy/compose.exec-live.yaml</c> (read by xd-up.sh only with <c>--exec</c> AND <c>XD_EXEC_READINESS_LIVE=true</c>): profile live, the only member of both <c>exec</c> and <c>egress</c>, no
/// secret, no database, no port, hardened, built from a digest-pinned base with a pinned package; xd-exec gains only the proxy variable there and never joins egress; the allow-list file holds exactly the accepted hosts.
/// </summary>
internal static partial class ExecComposeRules
{
    public static readonly string[] All =
    [
        "profile-live-only", "no-ports", "network-exec-only", "exec-network-internal", "hardening", "role-xd-exec-only", "two-secrets", "secret-holders", "handover-key-not-in-feed-or-worker", "network-members", "environment-none-by-default", "readiness-live-off-by-default",
        "image-and-limits", "no-proxy-in-base", "proxy-profile-live", "proxy-networks", "one-bridge", "proxy-no-secret-db-or-port", "proxy-hardening", "proxy-image-pinned", "exec-gets-only-the-proxy-variable", "allow-list-accepted-hosts",
    ];

    /// <summary>The live hosts named by an accepted ADR (ADR 0172: the two readiness reads). A new line needs its own accepted ADR (ADR 0133 rule) and a change here in the same commit.</summary>
    public static readonly string[] AcceptedHosts = ["api.dhan.co:443"];

    public const string ProxyService = "xd-exec-proxy";

    public const string ProxyAddress = "http://xd-exec-proxy:8888";

    // The services that must never hold the hand-over key (ADR 0171): the feed holds the vault key, the worker nothing of the broker.
    private static readonly string[] KeyLess = ["xd-feed", "xd-worker"];

    public static List<string> Violations(string composeText, string overlayText, string allowList, string proxyDockerfile)
    {
        var root = AiComposeRules.Parse(composeText);
        var services = AiComposeRules.Map(root, "services");
        var exec = AiComposeRules.Map(services, "xd-exec");
        var overlay = AiComposeRules.Map(AiComposeRules.Parse(overlayText), "services");
        var proxy = AiComposeRules.Map(overlay, ProxyService);
        var overlayExec = AiComposeRules.Map(overlay, "xd-exec");
        var found = new List<string>();

        void Check(string rule, bool ok)
        {
            if (!ok)
            {
                found.Add(rule);
            }
        }

        var env = AiComposeRules.Map(exec, "environment");
        Check("profile-live-only", AiComposeRules.List(exec, "profiles") is ["live"]);
        Check("no-ports", !exec.ContainsKey("ports") && !exec.ContainsKey("expose"));
        Check("network-exec-only", AiComposeRules.NetworkNames(exec) is ["exec"]);
        Check("exec-network-internal", AiComposeRules.Text(AiComposeRules.Map(AiComposeRules.Map(root, "networks"), "exec"), "internal") == "true");
        Check("hardening", AiComposeRules.Text(exec, "read_only") == "true" && AiComposeRules.List(exec, "cap_drop") is ["ALL"] && AiComposeRules.List(exec, "security_opt").Contains("no-new-privileges:true"));
        Check("role-xd-exec-only", AiComposeRules.Text(env, "Database__ConnectionString").EndsWith("Username=xd_exec", StringComparison.Ordinal) && services.Keys.Where(s => AnyText(AiComposeRules.Map(services, s), "Username=xd_exec")).SequenceEqual(["xd-exec"]));
        Check("two-secrets", McpComposeRules.Secrets(exec).Order(StringComparer.Ordinal).SequenceEqual(["db_exec_password", "exec_handover_key"]) && !AiComposeRules.EnvironmentLooksSecret(exec));
        Check("secret-holders", Holders(services, "db_exec_password").SequenceEqual(["xd-exec", "xd-migrate"]) && Holders(services, "exec_handover_key").SequenceEqual(["xd-api", "xd-exec"]));
        Check("handover-key-not-in-feed-or-worker", KeyLess.All(s => services.ContainsKey(s) && !McpComposeRules.Secrets(AiComposeRules.Map(services, s)).Contains("exec_handover_key")));
        Check("network-members", services.Keys.Where(s => AiComposeRules.NetworkNames(AiComposeRules.Map(services, s)).Contains("exec")).Order(StringComparer.Ordinal).SequenceEqual(["xd-db", "xd-exec"]));
        Check("environment-none-by-default", AiComposeRules.Text(env, "Execution__Environment") == "${XD_EXEC_ENVIRONMENT:-none}");
        Check("readiness-live-off-by-default", AiComposeRules.Text(env, "Execution__ReadinessLiveEnabled") == "${XD_EXEC_READINESS_LIVE:-false}");
        Check(
            "image-and-limits",
            AiComposeRules.Text(exec, "image").StartsWith("localhost/xdrishti/exec:", StringComparison.Ordinal) && AiComposeRules.Text(AiComposeRules.Map(exec, "build"), "target") == "exec" && AiComposeRules.Text(exec, "mem_limit") == "256m" && exec.ContainsKey("healthcheck"));

        // ADR 0172. The base file (every deploy) has no proxy service and gives no service a proxy variable: a plain deploy or --exec alone has no route out.
        Check("no-proxy-in-base", !services.ContainsKey(ProxyService) && services.Keys.All(s => !AiComposeRules.Map(AiComposeRules.Map(services, s), "environment").Keys.Any(IsProxyVariable)));
        Check("proxy-profile-live", AiComposeRules.List(proxy, "profiles") is ["live"]);
        Check("proxy-networks", AiComposeRules.NetworkNames(proxy).Order(StringComparer.Ordinal).SequenceEqual(["egress", "exec"]));
        Check("one-bridge", overlay.Keys.Order(StringComparer.Ordinal).SequenceEqual(["xd-exec", ProxyService]) && !overlayExec.ContainsKey("networks") && Bridges(services, overlay).SequenceEqual([ProxyService]));
        Check(
            "proxy-no-secret-db-or-port",
            !proxy.ContainsKey("secrets") && !proxy.ContainsKey("ports") && !proxy.ContainsKey("expose") && !proxy.ContainsKey("environment") && !proxy.ContainsKey("env_file") && !proxy.ContainsKey("depends_on")
            && AiComposeRules.List(proxy, "volumes") is ["./exec-proxy/allowlist.txt:/etc/xd/allowlist.txt:ro"]);
        Check(
            "proxy-hardening",
            AiComposeRules.Text(proxy, "read_only") == "true" && AiComposeRules.List(proxy, "cap_drop") is ["ALL"] && AiComposeRules.List(proxy, "security_opt").Contains("no-new-privileges:true") && AiComposeRules.Text(proxy, "mem_limit") == "32m"
            && AiComposeRules.Map(proxy, "healthcheck").ContainsKey("test") && AiComposeRules.Text(proxy, "restart") == "unless-stopped");
        Check(
            "proxy-image-pinned",
            AiComposeRules.Text(proxy, "image").StartsWith("localhost/xdrishti/exec-proxy:", StringComparison.Ordinal) && AiComposeRules.Text(AiComposeRules.Map(proxy, "build"), "context") == "./exec-proxy" && DockerfilePinned(proxyDockerfile));
        Check(
            "exec-gets-only-the-proxy-variable",
            overlayExec.Keys.Order(StringComparer.Ordinal).SequenceEqual(["depends_on", "environment"]) && AiComposeRules.Map(overlayExec, "environment") is { Count: 1 } e && AiComposeRules.Text(e, "HTTPS_PROXY") == ProxyAddress
            && AiComposeRules.Map(overlayExec, "depends_on").Keys.SequenceEqual([ProxyService]));
        Check("allow-list-accepted-hosts", AllowListEntries(allowList).SequenceEqual(AcceptedHosts));
        return found;
    }

    /// <summary>The entries of the allow-list file: every line that is not blank and not a comment, trimmed.</summary>
    public static List<string> AllowListEntries(string allowList) =>
        [.. allowList.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'))];

    private static bool IsProxyVariable(string name) => name.Contains("proxy", StringComparison.OrdinalIgnoreCase);

    // The services on both exec and egress once the overlay is merged in (network lists of a service in both files are unioned, as compose does).
    private static List<string> Bridges(Dictionary<string, object> services, Dictionary<string, object> overlay) =>
        [.. services.Keys.Union(overlay.Keys).Where(s =>
        {
            var networks = AiComposeRules.NetworkNames(AiComposeRules.Map(services, s)).Union(AiComposeRules.NetworkNames(AiComposeRules.Map(overlay, s))).ToList();
            return networks.Contains("exec") && networks.Contains("egress");
        }).Order(StringComparer.Ordinal)];

    // Exactly one FROM, by tag AND a real digest; the one package pinned to an exact version; nothing downloaded otherwise.
    private static bool DockerfilePinned(string dockerfile)
    {
        var lines = dockerfile.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
        var from = lines.Where(l => l.StartsWith("FROM ", StringComparison.Ordinal)).ToList();
        return from.Count == 1 && FromPinned().IsMatch(from[0]) && !from[0].Contains(new string('0', 64), StringComparison.Ordinal)
            && lines.Where(l => l.StartsWith("RUN ", StringComparison.Ordinal)).SequenceEqual(lines.Where(l => ApkPinned().IsMatch(l)))
            && !lines.Any(l => l.Contains("curl", StringComparison.Ordinal) || l.Contains("wget", StringComparison.Ordinal) || l.StartsWith("ADD ", StringComparison.Ordinal));
    }

    [GeneratedRegex(@"^FROM docker\.io/library/alpine:\d+\.\d+\.\d+@sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex FromPinned();

    [GeneratedRegex(@"^RUN apk add --no-cache tinyproxy=[0-9][0-9.]*-r[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ApkPinned();

    private static List<string> Holders(Dictionary<string, object> services, string secret) =>
        [.. services.Keys.Where(s => McpComposeRules.Secrets(AiComposeRules.Map(services, s)).Contains(secret)).Order(StringComparer.Ordinal)];

    private static bool AnyText(Dictionary<string, object> service, string needle) => AiComposeRules.Map(service, "environment").Values.Any(v => v?.ToString()?.Contains(needle, StringComparison.Ordinal) == true);
}
