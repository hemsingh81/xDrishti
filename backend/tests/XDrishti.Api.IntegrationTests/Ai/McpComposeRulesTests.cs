namespace XDrishti.Api.IntegrationTests.Ai;

/// <summary>Spec P9-02 AC 14: the real <c>xd-mcp</c> block passes every rule, a planted violation of each rule fails exactly that rule, and the secrets are shared only as the design says.</summary>
public sealed class McpComposeRulesTests
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

    /// <summary>Replaces <paramref name="from"/> by <paramref name="to"/> inside the xd-mcp block only.</summary>
    private static string Plant(string from, string to)
    {
        var start = Compose.IndexOf("\n  xd-mcp:", StringComparison.Ordinal);
        var end = Compose.IndexOf("\nnetworks:", start, StringComparison.Ordinal);
        var block = Compose[start..end];
        block.ShouldContain(from);
        return Compose[..start] + block.Replace(from, to, StringComparison.Ordinal) + Compose[end..];
    }

    [Fact]
    public void The_real_block_passes_every_rule() => McpComposeRules.Violations(Compose).ShouldBeEmpty();

    public static TheoryData<string, string, string> Plants => new()
    {
        { "profile-ai-only", "profiles: [ai]", "profiles: [ai, ops]" },
        { "no-ports", "restart: unless-stopped\n    environment:\n      DOTNET_ENVIRONMENT", "restart: unless-stopped\n    ports: [\"127.0.0.1:8081:8080\"]\n    environment:\n      DOTNET_ENVIRONMENT" },
        { "network-back-only", "networks: [back, ai-edge]\n    mem_limit: 512m", "networks: [back, egress]\n    mem_limit: 512m" },
        { "cap-drop-all", "<<: *hardening", "read_only: true\n    security_opt: [no-new-privileges:true]" },
        { "no-new-privileges", "<<: *hardening", "read_only: true\n    cap_drop: [ALL]" },
        { "mem-limit", "mem_limit: 512m", "mem_limit: 4g" },
        { "healthcheck", "    healthcheck:\n      test: [CMD, dotnet, XDrishti.Mcp.dll", "    xhealthcheck:\n      test: [CMD, dotnet, XDrishti.Mcp.dll" },
        { "read-only-root", "<<: *hardening", "cap_drop: [ALL]\n    security_opt: [no-new-privileges:true]" },
        { "exactly-four-secrets", "      - source: mcp_token\n        target: Mcp__Tokens__Api", "      - source: mcp_token\n        target: Mcp__Tokens__Api\n      - source: db_app_password\n        target: Database__Password" },
        { "no-secret-in-environment", "ASPNETCORE_ENVIRONMENT: Production", "ASPNETCORE_ENVIRONMENT: Production\n      Mcp__Tokens__Api: abcdefghijklmnopqrstuvwxyz0123456789" },
        { "no-application-role", "Username=xd_mcp_sql\n", "Username=xd_app\n" },
        { "no-volumes", "    networks: [back, ai-edge]\n    mem_limit: 512m", "    volumes: [\"../data:/data\"]\n    networks: [back, ai-edge]\n    mem_limit: 512m" },
        { "migrate-first", "xd-migrate: { condition: service_completed_successfully }\n    healthcheck:\n      test: [CMD, dotnet, XDrishti.Mcp.dll", "xd-migrate: { condition: service_started }\n    healthcheck:\n      test: [CMD, dotnet, XDrishti.Mcp.dll" },
        { "built-from-target-mcp", "target: mcp", "target: api" },
        { "restart-unless-stopped", "restart: unless-stopped\n    environment:\n      DOTNET_ENVIRONMENT", "restart: \"no\"\n    environment:\n      DOTNET_ENVIRONMENT" },
    };

    [Theory]
    [MemberData(nameof(Plants))]
    public void A_planted_violation_fails_its_rule(string rule, string from, string to)
    {
        McpComposeRules.Violations(Plant(from, to)).ShouldContain(rule);
    }

    [Fact]
    public void Every_rule_has_a_planted_test() => Plants.Select(p => p.Data.Item1).Order().ShouldBe(McpComposeRules.All.Order());

    [Fact]
    public void The_token_and_the_two_passwords_are_mounted_only_where_the_design_says()
    {
        var services = Compose[..Compose.IndexOf("\nnetworks:\n", StringComparison.Ordinal)];
        string[] Users(string secret) => [.. services.Split("\n  xd-", StringSplitOptions.None).Where(block => block.Contains($"source: {secret}\n", StringComparison.Ordinal)).Select(b => b.Split('\n')[0].TrimEnd(':'))];

        Users("mcp_token").ShouldBe(["api", "mcp"], ignoreOrder: true, customMessage: "the token is read by the tool server and sent by the API");
        Users("mcp_token_hermes").ShouldBe(["mcp", "mcp-eval"], ignoreOrder: true, customMessage: "Mcp:Tokens:Hermes is accepted by both tool servers; xd-hermes holds it as a plain short-syntax secret (P9-10)");
        Users("db_mcp_read_password").ShouldBe(["migrate", "mcp", "migrate-eval", "mcp-eval"], ignoreOrder: true, customMessage: "migrate sets the login, the tool server uses it, and so do the migration and the tool server of the eval (P9-09)");
        Users("db_mcp_sql_password").ShouldBe(["migrate", "mcp", "migrate-eval", "mcp-eval"], ignoreOrder: true);
        Compose.ShouldContain("target: AiRoles__ReadPassword");
        Compose.ShouldContain("target: AiRoles__SqlPassword");
        Compose.ShouldContain("target: Assistant__Mcp__Token");
        Compose.ShouldContain("Assistant__Mcp__Url: http://xd-mcp:8080/mcp");
    }

    [Fact]
    public void The_up_script_writes_the_three_secret_files_from_the_keychain_and_the_image_has_a_target_and_a_health_check()
    {
        var up = File.ReadAllText(FindUp("deploy", "xd-up.sh"));
        var dockerfile = File.ReadAllText(FindUp("backend", "Dockerfile"));
        var ai = File.ReadAllText(FindUp("scripts", "xd-ai.sh"));

        up.ShouldContain("write_secret db_mcp_read_password \"xdrishti/db/mcp-read\"");
        up.ShouldContain("write_secret db_mcp_sql_password \"xdrishti/db/mcp-sql\"");
        up.ShouldContain("write_secret mcp_token \"xdrishti/mcp/token\"");
        dockerfile.ShouldContain("AS mcp");
        dockerfile.ShouldContain("XDrishti.Mcp.dll\", \"--healthcheck\"");
        dockerfile.ShouldContain("COPY src/XDrishti.Mcp/XDrishti.Mcp.csproj");
        ai.ShouldContain("--tools-only");
        ai.ShouldContain("xdrishti-xd-mcp-1");
        up.ShouldNotContain("up -d --wait --wait-timeout 300 xd-mcp");
    }
}
