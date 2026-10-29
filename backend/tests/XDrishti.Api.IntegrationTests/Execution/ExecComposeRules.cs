using XDrishti.Api.IntegrationTests.Ai;

namespace XDrishti.Api.IntegrationTests.Execution;

/// <summary>
/// The static rules of <c>xd-exec</c> in <c>deploy/compose.yaml</c> (spec P12-01b, ADR 0156): on demand only, no port, its own internal network (shared with the database alone), no route out, hardened, one secret, the database role xd_exec and no other; the password file
/// reaches only xd-migrate (which sets the login) and xd-exec; nothing else joins the network or names the role.
/// </summary>
internal static class ExecComposeRules
{
    public static readonly string[] All =
    [
        "profile-live-only", "no-ports", "network-exec-only", "exec-network-internal", "hardening", "role-xd-exec-only", "one-secret", "secret-holders", "network-members", "environment-none-by-default", "image-and-limits",
    ];

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
        Check("one-secret", McpComposeRules.Secrets(exec) is ["db_exec_password"] && !AiComposeRules.EnvironmentLooksSecret(exec));
        Check("secret-holders", services.Keys.Where(s => McpComposeRules.Secrets(AiComposeRules.Map(services, s)).Contains("db_exec_password")).Order(StringComparer.Ordinal).SequenceEqual(["xd-exec", "xd-migrate"]));
        Check("network-members", services.Keys.Where(s => AiComposeRules.NetworkNames(AiComposeRules.Map(services, s)).Contains("exec")).Order(StringComparer.Ordinal).SequenceEqual(["xd-db", "xd-exec"]));
        Check("environment-none-by-default", AiComposeRules.Text(env, "Execution__Environment") == "${XD_EXEC_ENVIRONMENT:-none}");
        Check(
            "image-and-limits",
            AiComposeRules.Text(exec, "image").StartsWith("localhost/xdrishti/exec:", StringComparison.Ordinal) && AiComposeRules.Text(AiComposeRules.Map(exec, "build"), "target") == "exec" && AiComposeRules.Text(exec, "mem_limit") == "256m" && exec.ContainsKey("healthcheck"));
        return found;
    }

    private static bool AnyText(Dictionary<string, object> service, string needle) => AiComposeRules.Map(service, "environment").Values.Any(v => v?.ToString()?.Contains(needle, StringComparison.Ordinal) == true);
}
