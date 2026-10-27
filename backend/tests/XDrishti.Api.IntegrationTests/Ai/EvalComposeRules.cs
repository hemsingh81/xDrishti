namespace XDrishti.Api.IntegrationTests.Ai;

/// <summary>
/// The static rules of the four eval services in <c>deploy/compose.yaml</c> (spec P9-09 AC 13, ADR 0143), in the manner of <see cref="McpComposeRules"/>: on demand only, no port, the internal network only, hardened; the eval tool server reads the fixture database as the two read-only roles
/// with its own token; the seeder and the migration name the fixture database; the runner is the only holder of the eval token (with the tool server), and the production API holds none and has no address for the eval's tool server.
/// </summary>
internal static class EvalComposeRules
{
    public static readonly string[] All =
    [
        "profile-eval-only", "no-ports", "network-back-only", "hardening", "mcp-eval-database", "mcp-eval-secrets", "mcp-eval-image", "database-of-the-one-shots", "runner-runtime-role", "runner-reads-eval-files-read-only", "eval-token-only-where-designed", "api-has-no-eval-token",
    ];

    private static readonly string[] Services = ["xd-migrate-eval", "xd-mcp-eval", "xd-eval-seed", "xd-eval"];

    public static List<string> Violations(string composeText)
    {
        var root = AiComposeRules.Parse(composeText);
        var services = AiComposeRules.Map(root, "services");
        var found = new List<string>();

        void Check(string rule, bool ok)
        {
            if (!ok)
            {
                found.Add(rule);
            }
        }

        Dictionary<string, object> Of(string name) => AiComposeRules.Map(services, name);
        string Env(string service, string key) => AiComposeRules.Text(AiComposeRules.Map(Of(service), "environment"), key);

        Check("profile-eval-only", Services.All(s => AiComposeRules.List(Of(s), "profiles") is ["eval"]));
        Check("no-ports", Services.All(s => !Of(s).ContainsKey("ports") && !Of(s).ContainsKey("expose")));
        Check("network-back-only", Services.All(s => AiComposeRules.NetworkNames(Of(s)) is ["back"]));
        Check("hardening", Services.All(s => AiComposeRules.Text(Of(s), "read_only") == "true" && AiComposeRules.List(Of(s), "cap_drop") is ["ALL"] && AiComposeRules.List(Of(s), "security_opt").Contains("no-new-privileges:true")));

        var mcp = Of("xd-mcp-eval");
        var read = Env("xd-mcp-eval", "Database__ReadConnectionString");
        var sql = Env("xd-mcp-eval", "Database__SqlConnectionString");
        Check("mcp-eval-database", read.Contains("Database=xdrishti_eval;", StringComparison.Ordinal) && read.EndsWith("Username=xd_mcp_read", StringComparison.Ordinal) && sql.Contains("Database=xdrishti_eval;", StringComparison.Ordinal) && sql.EndsWith("Username=xd_mcp_sql", StringComparison.Ordinal));
        Check("mcp-eval-secrets", McpComposeRules.Secrets(mcp).Order(StringComparer.Ordinal).SequenceEqual(["db_mcp_read_password", "db_mcp_sql_password", "eval_mcp_token"]) && composeText.Contains("target: Mcp__Tokens__Eval", StringComparison.Ordinal) && !AiComposeRules.EnvironmentLooksSecret(mcp));
        Check("mcp-eval-image", AiComposeRules.Text(mcp, "image").StartsWith("localhost/xdrishti/mcp:", StringComparison.Ordinal) && AiComposeRules.Map(mcp, "build").TryGetValue("target", out var target) && target?.ToString() == "mcp" && AiComposeRules.Text(mcp, "mem_limit") == "512m" && mcp.ContainsKey("healthcheck"));

        Check(
            "database-of-the-one-shots",
            Env("xd-migrate-eval", "Database__ConnectionString") == "Host=xd-db;Port=5432;Database=xdrishti_eval;Username=xd_owner" && Env("xd-migrate-eval", "Migrate__SkipOwner") == "true"
            && Env("xd-eval-seed", "Database__ConnectionString") == "Host=xd-db;Port=5432;Database=xdrishti_eval;Username=xd_owner" && Env("xd-eval-seed", "Eval__SeedConnection") == "Host=xd-db;Port=5432;Database=xdrishti_eval;Username=xd_owner"
            && AiComposeRules.Text(Of("xd-migrate-eval"), "restart") == "no" && AiComposeRules.Text(Of("xd-eval-seed"), "restart") == "no");

        Check(
            "runner-runtime-role",
            Env("xd-eval", "Database__ConnectionString") == "Host=xd-db;Port=5432;Database=xdrishti;Username=xd_app" && Env("xd-eval", "Eval__Mcp__Url") == "http://xd-mcp-eval:8080/mcp" && !AiComposeRules.Map(Of("xd-eval"), "environment").ContainsKey("Assistant__Mcp__Url")
            && McpComposeRules.Secrets(Of("xd-eval")).Order(StringComparer.Ordinal).SequenceEqual(["db_app_password", "eval_mcp_token", "llm_api_key"]) && AiComposeRules.Text(Of("xd-eval"), "restart") == "no");
        Check("runner-reads-eval-files-read-only", AiComposeRules.List(Of("xd-eval"), "volumes") is ["../eval:/data/eval-files:ro"] && AiComposeRules.List(Of("xd-eval-seed"), "volumes") is ["../eval:/data/eval-files:ro"]);

        var holders = services.Keys.Where(s => McpComposeRules.Secrets(AiComposeRules.Map(services, s)).Contains("eval_mcp_token")).Order(StringComparer.Ordinal).ToList();
        Check("eval-token-only-where-designed", holders.SequenceEqual(["xd-eval", "xd-mcp-eval"]));
        var api = Of("xd-api");
        Check("api-has-no-eval-token", !McpComposeRules.Secrets(api).Contains("eval_mcp_token") && !AiComposeRules.Map(api, "environment").Keys.Any(k => k.StartsWith("Eval__Mcp", StringComparison.Ordinal)));
        return found;
    }
}
