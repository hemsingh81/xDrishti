namespace XDrishti.Api.IntegrationTests.Ai;

/// <summary>
/// The static rules of the <c>xd-mcp</c> service in <c>deploy/compose.yaml</c> (spec P9-02 AC 14, ADR 0137). Same method as <see cref="AiComposeRules"/>: each rule has a name, <see cref="Violations"/> lists the names that fail, the file is read with a YAML parser. The tool server holds
/// exactly three secrets (its two database passwords and its token), no volume, no published port, and none of the application's credentials.
/// </summary>
internal static class McpComposeRules
{
    public static readonly string[] All =
    [
        "profile-ai-only", "no-ports", "network-back-only", "cap-drop-all", "no-new-privileges", "mem-limit", "healthcheck", "read-only-root", "exactly-three-secrets", "no-secret-in-environment", "no-application-role", "no-volumes", "migrate-first", "built-from-target-mcp", "restart-unless-stopped",
    ];

    public static List<string> Violations(string composeText)
    {
        var root = AiComposeRules.Parse(composeText);
        var service = AiComposeRules.Map(AiComposeRules.Map(root, "services"), "xd-mcp");
        var found = new List<string>();

        void Check(string rule, bool ok)
        {
            if (!ok)
            {
                found.Add(rule);
            }
        }

        Check("profile-ai-only", AiComposeRules.List(service, "profiles") is ["ai"]);
        Check("no-ports", !service.ContainsKey("ports") && !service.ContainsKey("expose"));
        Check("network-back-only", AiComposeRules.NetworkNames(service) is ["back"]);
        Check("cap-drop-all", AiComposeRules.List(service, "cap_drop") is ["ALL"]);
        Check("no-new-privileges", AiComposeRules.List(service, "security_opt").Contains("no-new-privileges:true"));
        Check("mem-limit", AiComposeRules.Text(service, "mem_limit") == "512m");
        Check("healthcheck", service.ContainsKey("healthcheck") && AiComposeRules.Map(service, "healthcheck").ContainsKey("test"));
        Check("read-only-root", AiComposeRules.Text(service, "read_only") == "true");
        Check("exactly-three-secrets", Secrets(service).Order(StringComparer.Ordinal).SequenceEqual(["db_mcp_read_password", "db_mcp_sql_password", "mcp_token"]));
        Check("no-secret-in-environment", !AiComposeRules.EnvironmentLooksSecret(service));
        var environment = AiComposeRules.Map(service, "environment");
        Check("no-application-role", !environment.Values.Any(v => v?.ToString() is { } text && (text.Contains("xd_app", StringComparison.Ordinal) || text.Contains("xd_owner", StringComparison.Ordinal))) && !environment.ContainsKey("Database__ConnectionString"));
        Check("no-volumes", !service.ContainsKey("volumes"));
        Check("migrate-first", AiComposeRules.Map(AiComposeRules.Map(service, "depends_on"), "xd-migrate").TryGetValue("condition", out var condition) && condition?.ToString() == "service_completed_successfully");
        Check("built-from-target-mcp", AiComposeRules.Map(service, "build").TryGetValue("target", out var target) && target?.ToString() == "mcp");
        Check("restart-unless-stopped", AiComposeRules.Text(service, "restart") == "unless-stopped");
        return found;
    }

    /// <summary>The names of the secrets the service mounts (short or long syntax).</summary>
    internal static List<string> Secrets(Dictionary<string, object> service) =>
        service.TryGetValue("secrets", out var value) && value is List<object> list
            ? [.. list.Select(item => item is Dictionary<object, object> map ? map["source"].ToString()! : item.ToString()!)]
            : [];
}
