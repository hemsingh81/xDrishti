using XDrishti.Api.IntegrationTests.Ai;

namespace XDrishti.Api.IntegrationTests.Execution;

/// <summary>
/// The static rules of <c>xd-exec</c> in <c>deploy/compose.yaml</c> (spec P12-01b, ADR 0156; ADR 0171): on demand only, no port, its own internal network (shared with the database alone), no route out, hardened, exactly two secrets (the role's password and the token
/// hand-over key), the database role xd_exec and no other; the password file reaches only xd-migrate (which sets the login) and xd-exec, the hand-over key only xd-api (which seals) and xd-exec, never xd-feed or xd-worker; nothing else joins the network or names the role;
/// the live readiness switch is off by default.
/// </summary>
internal static class ExecComposeRules
{
    public static readonly string[] All =
    [
        "profile-live-only", "no-ports", "network-exec-only", "exec-network-internal", "hardening", "role-xd-exec-only", "two-secrets", "secret-holders", "handover-key-not-in-feed-or-worker", "network-members", "environment-none-by-default", "readiness-live-off-by-default",
        "image-and-limits",
    ];

    // The services that must never hold the hand-over key (ADR 0171): the feed holds the vault key, the worker nothing of the broker.
    private static readonly string[] KeyLess = ["xd-feed", "xd-worker"];

    public static List<string> Violations(string composeText)
    {
        var root = AiComposeRules.Parse(composeText);
        var services = AiComposeRules.Map(root, "services");
        var exec = AiComposeRules.Map(services, "xd-exec");
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
        return found;
    }

    private static List<string> Holders(Dictionary<string, object> services, string secret) =>
        [.. services.Keys.Where(s => McpComposeRules.Secrets(AiComposeRules.Map(services, s)).Contains(secret)).Order(StringComparer.Ordinal)];

    private static bool AnyText(Dictionary<string, object> service, string needle) => AiComposeRules.Map(service, "environment").Values.Any(v => v?.ToString()?.Contains(needle, StringComparison.Ordinal) == true);
}
