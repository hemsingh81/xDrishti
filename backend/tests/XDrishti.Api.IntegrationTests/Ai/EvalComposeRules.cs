namespace XDrishti.Api.IntegrationTests.Ai;

/// <summary>
/// The static rules of the five eval services (BK-12b added the API over the fixture) in <c>deploy/compose.yaml</c> (spec P9-09 AC 13, ADR 0143), in the manner of <see cref="McpComposeRules"/>: on demand only, no port, the internal network only, hardened; the eval tool server reads the fixture database as the two read-only roles
/// with its own token; the seeder and the migration name the fixture database; the runner is the only holder of the eval token (with the tool server), and the production API holds none and has no address for the eval's tool server.
/// </summary>
internal static partial class EvalComposeRules
{
    /// <summary>The host name of the production API as a whole word (xd-api-eval is another host).</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"(?<![\w-])xd-api(?![\w-])", System.Text.RegularExpressions.RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial System.Text.RegularExpressions.Regex ProductionApi();

    public static readonly string[] All =
    [
        "profile-eval-only", "no-ports", "network-back-only", "hardening", "mcp-eval-database", "mcp-eval-secrets", "mcp-eval-image", "database-of-the-one-shots", "runner-runtime-role", "runner-reads-eval-files-read-only", "eval-token-only-where-designed", "api-has-no-eval-token", "mcp-eval-reads-the-eval-api", "api-eval-isolated", "api-eval-token-only-where-designed",
    ];

    private static readonly string[] Services = ["xd-migrate-eval", "xd-mcp-eval", "xd-api-eval", "xd-eval-seed", "xd-eval"];

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
        Check("network-back-only", Services.All(s => AiComposeRules.NetworkNames(Of(s)) is ["back"] || (s == "xd-mcp-eval" && AiComposeRules.NetworkNames(Of(s)) is ["back", "ai-edge"])));
        Check("hardening", Services.All(s => AiComposeRules.Text(Of(s), "read_only") == "true" && AiComposeRules.List(Of(s), "cap_drop") is ["ALL"] && AiComposeRules.List(Of(s), "security_opt").Contains("no-new-privileges:true")));

        var mcp = Of("xd-mcp-eval");
        var read = Env("xd-mcp-eval", "Database__ReadConnectionString");
        var sql = Env("xd-mcp-eval", "Database__SqlConnectionString");
        Check("mcp-eval-database", read.Contains("Database=xdrishti_eval;", StringComparison.Ordinal) && read.EndsWith("Username=xd_mcp_read", StringComparison.Ordinal) && sql.Contains("Database=xdrishti_eval;", StringComparison.Ordinal) && sql.EndsWith("Username=xd_mcp_sql", StringComparison.Ordinal));
        Check("mcp-eval-secrets", McpComposeRules.Secrets(mcp).Order(StringComparer.Ordinal).SequenceEqual(["db_mcp_read_password", "db_mcp_sql_password", "eval_mcp_token", "mcp_api_token_eval", "mcp_token_hermes"]) && composeText.Contains("target: Mcp__Tokens__Eval", StringComparison.Ordinal) && !AiComposeRules.EnvironmentLooksSecret(mcp));
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

        // BK-12b AC 12: the eval tool server reads the eval's own API over the fixture, never the production API; that API has no egress, no port and no broker secret.
        var mcpEnvironment = AiComposeRules.Map(mcp, "environment");
        Check("mcp-eval-reads-the-eval-api", Env("xd-mcp-eval", "Mcp__Api__BaseUrl") == "http://xd-api-eval:8080" && !mcpEnvironment.Values.Any(v => ProductionApi().IsMatch(v?.ToString() ?? string.Empty)) && !McpComposeRules.Secrets(mcp).Contains("mcp_api_token"));
        var apiEval = Of("xd-api-eval");
        var apiEvalSecrets = McpComposeRules.Secrets(apiEval).Order(StringComparer.Ordinal).ToList();
        Check(
            "api-eval-isolated",
            AiComposeRules.NetworkNames(apiEval) is ["back"] && !apiEval.ContainsKey("ports") && !apiEval.ContainsKey("expose") && apiEvalSecrets.SequenceEqual(["db_app_password", "mcp_api_token_eval"])
            && Env("xd-api-eval", "Database__ConnectionString") == "Host=xd-db;Port=5432;Database=xdrishti_eval;Username=xd_app" && Env("xd-api-eval", "Api__ReportsOnly") == "true"
            && !AiComposeRules.Map(apiEval, "environment").Keys.Any(k => k.Contains("Dhan", StringComparison.OrdinalIgnoreCase) || k.StartsWith("Brokers__", StringComparison.Ordinal) || k.StartsWith("BrokerVault", StringComparison.Ordinal)) && !AiComposeRules.EnvironmentLooksSecret(apiEval));
        Check("api-eval-token-only-where-designed", services.Keys.Where(s => McpComposeRules.Secrets(AiComposeRules.Map(services, s)).Contains("mcp_api_token_eval")).Order(StringComparer.Ordinal).SequenceEqual(["xd-api-eval", "xd-mcp-eval"]));
        return found;
    }
}
