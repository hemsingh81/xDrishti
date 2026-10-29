
namespace XDrishti.Api.IntegrationTests.Execution;

/// <summary>Spec P12-01b criterion 16 (static part): the real service passes every rule, a planted violation of each rule fails exactly that rule, and the up script starts the service and creates its Keychain item only with <c>--exec</c>.</summary>
public sealed class ExecComposeRulesTests
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
    public void The_real_service_passes_every_rule() => ExecComposeRules.Violations(Compose).ShouldBeEmpty();

    public static TheoryData<string, string, string, string> Plants => new()
    {
        { "profile-live-only", "xd-exec", "profiles: [live]", "profiles: [live, ops]" },
        { "no-ports", "xd-exec", "restart: unless-stopped\n    environment:", "restart: unless-stopped\n    ports: [\"127.0.0.1:8099:8080\"]\n    environment:" },
        { "network-exec-only", "xd-exec", "networks: [exec]", "networks: [exec, egress]" },
        { "exec-network-internal", "xd-db", "networks: [back, exec]", "networks: [back, exec]\n    x-planted: true" },
        { "hardening", "xd-exec", "<<: *hardening", "cap_drop: [ALL]\n    security_opt: [no-new-privileges:true]" },
        { "role-xd-exec-only", "xd-exec", "Username=xd_exec", "Username=xd_app" },
        { "one-secret", "xd-exec", "      - source: db_exec_password\n        target: Database__Password", "      - source: db_exec_password\n        target: Database__Password\n      - source: broker_vault_key\n        target: BrokerVault__Key" },
        { "secret-holders", "xd-api", "      - source: mcp_token\n        target: Assistant__Mcp__Token", "      - source: mcp_token\n        target: Assistant__Mcp__Token\n      - source: db_exec_password\n        target: Database__Password" },
        { "network-members", "xd-worker", "networks: [back]", "networks: [back, exec]" },
        { "environment-none-by-default", "xd-exec", "${XD_EXEC_ENVIRONMENT:-none}", "${XD_EXEC_ENVIRONMENT:-live}" },
        { "image-and-limits", "xd-exec", "mem_limit: 256m", "mem_limit: 2g" },
    };

    [Theory]
    [MemberData(nameof(Plants))]
    public void A_planted_violation_fails_its_rule(string rule, string service, string from, string to)
    {
        // exec-network-internal is planted on the network block itself.
        var planted = rule == "exec-network-internal" ? Compose.Replace("  exec:\n    internal: true", "  exec: {}", StringComparison.Ordinal) : Plant(service, from, to);

        ExecComposeRules.Violations(planted).ShouldContain(rule);
    }

    [Fact]
    public void Every_rule_has_a_planted_test() => Plants.Select(p => p.Data.Item1).Distinct().Order().ShouldBe(ExecComposeRules.All.Order());

    [Fact]
    public void The_up_script_creates_the_keychain_item_and_starts_the_service_only_with_the_exec_flag()
    {
        var up = File.ReadAllText(FindUp("deploy", "xd-up.sh"));

        up.ShouldContain("if [ \"$EXEC\" = 1 ]; then\n  write_secret db_exec_password \"xdrishti/db/exec\"");
        up.Split("write_secret db_exec_password").Length.ShouldBe(2, "created in exactly one place");
        up.ShouldContain("profiles=(--profile ops); [ \"$EXEC\" = 1 ] && profiles+=(--profile live)");
        up.ShouldNotContain("up -d --wait --wait-timeout 300 xd-exec");
        File.ReadAllText(FindUp("deploy", "xd-down.sh")).ShouldContain("--profile live down");
    }
}
