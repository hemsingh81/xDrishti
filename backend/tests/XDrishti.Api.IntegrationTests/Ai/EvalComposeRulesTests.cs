namespace XDrishti.Api.IntegrationTests.Ai;

/// <summary>Spec P9-09 AC 13 (ADR 0143): the real eval services pass every rule, a planted violation of each rule fails exactly that rule, the up script writes the eval token and the scripts never set a switch.</summary>
public sealed class EvalComposeRulesTests
{
    private static readonly string Compose = File.ReadAllText(FindUp("deploy", "compose.yaml"));

    private static string FindUp(params string[] parts)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine([d.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(string.Join('/', parts));
    }

    /// <summary>Replaces the first <paramref name="from"/> found inside the block of <paramref name="service"/> only.</summary>
    private static string Plant(string service, string from, string to)
    {
        var start = Compose.IndexOf($"\n  {service}:\n", StringComparison.Ordinal);
        var end = Compose.IndexOf("\n\n", start + 1, StringComparison.Ordinal);
        var block = Compose[start..end];
        block.ShouldContain(from);
        var at = block.IndexOf(from, StringComparison.Ordinal);
        return Compose[..start] + block[..at] + to + block[(at + from.Length)..] + Compose[end..];
    }

    [Fact]
    public void The_real_services_pass_every_rule() => EvalComposeRules.Violations(Compose).ShouldBeEmpty();

    public static TheoryData<string, string, string, string> Plants => new()
    {
        { "profile-eval-only", "xd-eval-seed", "profiles: [eval]", "profiles: [eval, ai]" },
        { "no-ports", "xd-mcp-eval", "restart: unless-stopped\n    environment:", "restart: unless-stopped\n    ports: [\"127.0.0.1:8099:8080\"]\n    environment:" },
        { "network-back-only", "xd-eval", "networks: [back]", "networks: [back, egress]" },
        { "hardening", "xd-migrate-eval", "<<: *hardening", "cap_drop: [ALL]\n    security_opt: [no-new-privileges:true]" },
        { "mcp-eval-database", "xd-mcp-eval", "Database__SqlConnectionString: Host=xd-db;Port=5432;Database=xdrishti_eval;", "Database__SqlConnectionString: Host=xd-db;Port=5432;Database=xdrishti;" },
        { "mcp-eval-secrets", "xd-mcp-eval", "      - source: eval_mcp_token\n        target: Mcp__Tokens__Eval", "      - source: eval_mcp_token\n        target: Mcp__Tokens__Eval\n      - source: db_app_password\n        target: Database__Password" },
        { "mcp-eval-image", "xd-mcp-eval", "target: mcp", "target: api" },
        { "database-of-the-one-shots", "xd-eval-seed", "Eval__SeedConnection: Host=xd-db;Port=5432;Database=xdrishti_eval;", "Eval__SeedConnection: Host=xd-db;Port=5432;Database=xdrishti;" },
        { "runner-runtime-role", "xd-eval", "Username=xd_app", "Username=xd_owner" },
        { "runner-reads-eval-files-read-only", "xd-eval", "- ../eval:/data/eval-files:ro", "- ../eval:/data/eval-files" },
        { "eval-token-only-where-designed", "xd-eval-seed", "    secrets:\n      - source: db_owner_password\n        target: Database__Password", "    secrets:\n      - source: db_owner_password\n        target: Database__Password\n      - source: eval_mcp_token\n        target: Eval__Mcp__Token" },
        { "api-has-no-eval-token", "xd-api", "      - source: mcp_token\n        target: Assistant__Mcp__Token", "      - source: mcp_token\n        target: Assistant__Mcp__Token\n      - source: eval_mcp_token\n        target: Eval__Mcp__Token" },
    };

    [Theory]
    [MemberData(nameof(Plants))]
    public void A_planted_violation_fails_its_rule(string rule, string service, string from, string to) =>
        EvalComposeRules.Violations(Plant(service, from, to)).ShouldContain(rule);

    [Fact]
    public void Every_rule_has_a_planted_test() => Plants.Select(p => p.Data.Item1).Distinct().Order().ShouldBe(EvalComposeRules.All.Order());

    [Fact]
    public void The_up_script_writes_the_eval_token_the_eval_scripts_set_no_switch_and_the_production_services_are_not_started_by_the_eval()
    {
        var up = File.ReadAllText(FindUp("deploy", "xd-up.sh"));
        var run = File.ReadAllText(FindUp("scripts", "xd-eval.sh"));
        var db = File.ReadAllText(FindUp("scripts", "eval-db.sh"));

        up.ShouldContain("write_secret eval_mcp_token \"xdrishti/mcp/eval-token\"");
        up.ShouldNotContain("--profile eval");
        foreach (var script in new[] { run, db })
        {
            script.ShouldNotContain("ai.settings");
            script.ShouldNotContain("DROP DATABASE IF EXISTS xdrishti ");
        }

        db.ShouldContain("NAME=\"xdrishti_eval\"");
        run.ShouldContain("--no-deps");
        run.ShouldContain("xdrishti-xd-mcp-eval-1");
    }

    [Fact]
    public void The_production_tool_server_and_its_roles_are_unchanged_by_the_eval()
    {
        McpComposeRules.Violations(Compose).ShouldBeEmpty();
        var production = Compose[Compose.IndexOf("\n  xd-mcp:\n", StringComparison.Ordinal)..];
        production.ShouldContain("Database=xdrishti;Username=xd_mcp_read");
        production.Split("\n\n")[0].ShouldNotContain("eval");
    }
}
