namespace XDrishti.Api.IntegrationTests.Execution;

/// <summary>Spec P12-01b criterion 16 (static part) and ADR 0172: the real files pass every rule, a planted violation of each rule fails exactly that rule, the up script starts the service and creates its Keychain item only with <c>--exec</c>, and reads the live-route
/// overlay (the proxy and the proxy variable) only with <c>--exec</c> AND <c>XD_EXEC_READINESS_LIVE=true</c>.</summary>
public sealed class ExecComposeRulesTests
{
    private static readonly string Compose = File.ReadAllText(FindUp("deploy", "compose.yaml"));

    private static readonly string Overlay = File.ReadAllText(FindUp("deploy", "compose.exec-live.yaml"));

    private static readonly string AllowList = File.ReadAllText(FindUp("deploy", "exec-proxy", "allowlist.txt"));

    private static readonly string Dockerfile = File.ReadAllText(FindUp("deploy", "exec-proxy", "Dockerfile"));

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

    private static string Plant(string text, string service, string from, string to)
    {
        var start = text.IndexOf($"\n  {service}:\n", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        var end = text.IndexOf("\n\n", start + 1, StringComparison.Ordinal);
        end = end < 0 ? text.Length : end;
        var block = text[start..end];
        block.ShouldContain(from);
        var at = block.IndexOf(from, StringComparison.Ordinal);
        return text[..start] + block[..at] + to + block[(at + from.Length)..] + text[end..];
    }

    private static string Replace(string text, string from, string to)
    {
        text.ShouldContain(from);
        return text.Replace(from, to, StringComparison.Ordinal);
    }

    private static List<string> Violations(string? compose = null, string? overlay = null, string? allowList = null, string? dockerfile = null) =>
        ExecComposeRules.Violations(compose ?? Compose, overlay ?? Overlay, allowList ?? AllowList, dockerfile ?? Dockerfile);

    [Fact]
    public void The_real_files_pass_every_rule() => Violations().ShouldBeEmpty();

    public static TheoryData<string, string, string, string, string> Plants => new()
    {
        { "profile-live-only", "compose", "xd-exec", "profiles: [live]", "profiles: [live, ops]" },
        { "no-ports", "compose", "xd-exec", "restart: unless-stopped\n    environment:", "restart: unless-stopped\n    ports: [\"127.0.0.1:8099:8080\"]\n    environment:" },
        { "network-exec-only", "compose", "xd-exec", "networks: [exec]", "networks: [exec, egress]" },
        { "exec-network-internal", "compose", "xd-db", "networks: [back, exec]", "networks: [back, exec]\n    x-planted: true" },
        { "hardening", "compose", "xd-exec", "<<: *hardening", "cap_drop: [ALL]\n    security_opt: [no-new-privileges:true]" },
        { "role-xd-exec-only", "compose", "xd-exec", "Username=xd_exec", "Username=xd_app" },
        { "two-secrets", "compose", "xd-exec", "      - source: exec_handover_key\n        target: ExecHandover__Key", "      - source: exec_handover_key\n        target: ExecHandover__Key\n      - source: broker_vault_key\n        target: BrokerVault__Key" },
        { "secret-holders", "compose", "xd-api", "      - source: mcp_token\n        target: Assistant__Mcp__Token", "      - source: mcp_token\n        target: Assistant__Mcp__Token\n      - source: db_exec_password\n        target: Database__Password" },
        { "secret-holders", "compose", "xd-api", "      - source: exec_handover_key\n        target: ExecHandover__Key\n", "" },
        { "handover-key-not-in-feed-or-worker", "compose", "xd-feed", "      - source: broker_vault_key\n        target: BrokerVault__Key", "      - source: broker_vault_key\n        target: BrokerVault__Key\n      - source: exec_handover_key\n        target: ExecHandover__Key" },
        { "handover-key-not-in-feed-or-worker", "compose", "xd-worker", "      - source: db_app_password\n        target: Database__Password", "      - source: db_app_password\n        target: Database__Password\n      - source: exec_handover_key\n        target: ExecHandover__Key" },
        { "network-members", "compose", "xd-worker", "networks: [back]", "networks: [back, exec]" },
        { "environment-none-by-default", "compose", "xd-exec", "${XD_EXEC_ENVIRONMENT:-none}", "${XD_EXEC_ENVIRONMENT:-live}" },
        { "readiness-live-off-by-default", "compose", "xd-exec", "${XD_EXEC_READINESS_LIVE:-false}", "${XD_EXEC_READINESS_LIVE:-true}" },
        { "image-and-limits", "compose", "xd-exec", "mem_limit: 256m", "mem_limit: 2g" },
        // ADR 0172: the live route.
        { "no-proxy-in-base", "compose", "xd-exec", "      Execution__ReadinessLiveEnabled: ${XD_EXEC_READINESS_LIVE:-false}", "      Execution__ReadinessLiveEnabled: ${XD_EXEC_READINESS_LIVE:-false}\n      HTTPS_PROXY: http://xd-exec-proxy:8888" },
        { "no-proxy-in-base", "compose", "xd-feed", "networks: [back, egress]", "networks: [back, egress]\n    environment:\n      https_proxy: http://elsewhere:3128" },
        { "proxy-profile-live", "overlay", "xd-exec-proxy", "profiles: [live]", "profiles: [ops]" },
        { "proxy-profile-live", "overlay", "xd-exec-proxy", "profiles: [live]", "profiles: [live, ops]" },
        { "proxy-networks", "overlay", "xd-exec-proxy", "networks: [exec, egress]", "networks: [exec, egress, back]" },
        { "proxy-networks", "overlay", "xd-exec-proxy", "networks: [exec, egress]", "networks: [egress]" },
        { "one-bridge", "overlay", "xd-exec", "    depends_on:", "    networks: [egress]\n    depends_on:" },
        { "one-bridge", "compose", "xd-feed", "networks: [back, egress]", "networks: [back, egress, exec]" },
        { "one-bridge", "overlay", "xd-exec", "      xd-exec-proxy: { condition: service_healthy }", "      xd-exec-proxy: { condition: service_healthy }\n\n  xd-api:\n    networks: [exec]" },
        { "proxy-no-secret-db-or-port", "overlay", "xd-exec-proxy", "    restart: unless-stopped", "    restart: unless-stopped\n    secrets: [db_app_password]" },
        { "proxy-no-secret-db-or-port", "overlay", "xd-exec-proxy", "    restart: unless-stopped", "    restart: unless-stopped\n    ports: [\"127.0.0.1:8888:8888\"]" },
        { "proxy-no-secret-db-or-port", "overlay", "xd-exec-proxy", "    restart: unless-stopped", "    restart: unless-stopped\n    environment:\n      Database__ConnectionString: Host=xd-db;Username=xd_app" },
        { "proxy-no-secret-db-or-port", "overlay", "xd-exec-proxy", "allowlist.txt:ro", "allowlist.txt:rw" },
        { "proxy-hardening", "overlay", "xd-exec-proxy", "read_only: true", "read_only: false" },
        { "proxy-hardening", "overlay", "xd-exec-proxy", "mem_limit: 32m", "mem_limit: 1g" },
        { "proxy-image-pinned", "dockerfile", "", "@sha256:", ":latest@sha256:" },
        { "proxy-image-pinned", "dockerfile", "", "tinyproxy=1.11.2-r0", "tinyproxy" },
        { "proxy-image-pinned", "dockerfile", "", "RUN apk add --no-cache tinyproxy=1.11.2-r0", "RUN apk add --no-cache tinyproxy=1.11.2-r0\nRUN apk add --no-cache curl" },
        { "exec-gets-only-the-proxy-variable", "overlay", "xd-exec", "      HTTPS_PROXY: http://xd-exec-proxy:8888", "      HTTPS_PROXY: http://xd-exec-proxy:8888\n      ALL_PROXY: http://elsewhere:3128" },
        { "exec-gets-only-the-proxy-variable", "overlay", "xd-exec", "http://xd-exec-proxy:8888", "http://elsewhere:3128" },
        { "allow-list-accepted-hosts", "allowlist", "", "api.dhan.co:443", "api.dhan.co:443\napi-order-update.dhan.co:443" },
        { "allow-list-accepted-hosts", "allowlist", "", "api.dhan.co:443", "api.dhan.co:8443" },
        { "allow-list-accepted-hosts", "allowlist", "", "api.dhan.co:443", "*.dhan.co:443" },
    };

    [Theory]
    [MemberData(nameof(Plants))]
    public void A_planted_violation_fails_its_rule(string rule, string file, string service, string from, string to)
    {
        var found = file switch
        {
            // exec-network-internal is planted on the network block itself.
            "compose" when rule == "exec-network-internal" => Violations(compose: Replace(Compose, "  exec:\n    internal: true", "  exec: {}")),
            "compose" => Violations(compose: Plant(Compose, service, from, to)),
            "overlay" => Violations(overlay: Plant(Overlay, service, from, to)),
            "allowlist" => Violations(allowList: Replace(AllowList, from, to)),
            "dockerfile" => Violations(dockerfile: Replace(Dockerfile, from, to)),
            _ => throw new ArgumentOutOfRangeException(nameof(file)),
        };

        found.ShouldContain(rule);
    }

    [Fact]
    public void Every_rule_has_a_planted_test() => Plants.Select(p => p.Data.Item1).Distinct().Order().ShouldBe(ExecComposeRules.All.Order());

    [Fact]
    public void The_up_script_creates_the_hand_over_key_like_the_vault_key_on_every_start_and_the_dev_script_reads_it()
    {
        var up = File.ReadAllText(FindUp("deploy", "xd-up.sh"));

        up.ShouldContain("local service=\"xdrishti/exec/handover-key\" value");
        up.ShouldContain("printf '%s' \"$value\" > \"$XD_SECRETS_DIR/exec_handover_key\"");
        up.ShouldContain("\nwrite_handover_key\n");
        up.Split("> \"$XD_SECRETS_DIR/exec_handover_key\"").Length.ShouldBe(2, "written in exactly one place");
        up.IndexOf("write_handover_key\n", StringComparison.Ordinal).ShouldBeLessThan(up.IndexOf("if [ \"$EXEC\" = 1 ]; then", StringComparison.Ordinal), "on every start, not behind --exec");
        File.ReadAllText(FindUp("scripts", "dev-api.sh")).ShouldContain("xdrishti/exec/handover-key");
    }

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

    [Fact]
    public void The_up_script_reads_the_live_route_overlay_only_with_the_exec_flag_and_the_readiness_switch_and_removes_it_otherwise()
    {
        var up = File.ReadAllText(FindUp("deploy", "xd-up.sh"));

        up.ShouldContain("readiness_live=\"$(printf '%s' \"${XD_EXEC_READINESS_LIVE:-false}\" | tr '[:upper:]' '[:lower:]')\"");
        up.ShouldContain("if [ \"$EXEC\" = 1 ] && [ \"$readiness_live\" = true ]; then\n  files+=(-f compose.exec-live.yaml)");
        up.Split("compose.exec-live.yaml").Length.ShouldBe(2, "added in exactly one place");
        up.ShouldNotContain("--remove-orphans", Case.Sensitive, "it could remove the profile-gated Hermes and eval containers");
        up.ShouldContain("podman rm -f xdrishti-xd-exec-proxy-1", Case.Sensitive, "a deploy without the overlay removes a proxy left from an earlier start, by its exact name");
        File.ReadAllText(FindUp("deploy", "xd-down.sh")).ShouldContain("-f compose.exec-live.yaml --profile ops --profile ai --profile live down");

        var scripts = Path.GetDirectoryName(FindUp("scripts", "check.sh"))!;
        Directory.GetFiles(scripts, "*.sh").Where(f => File.ReadAllText(f).Contains("compose.exec-live.yaml", StringComparison.Ordinal)).ShouldBeEmpty("only xd-up.sh starts the live route");
    }

    [Fact]
    public void The_allow_list_entries_skip_comments_and_blanks_and_the_real_file_names_only_the_accepted_host()
    {
        ExecComposeRules.AllowListEntries("# a\n\n api.dhan.co:443 \r\n").ShouldBe(["api.dhan.co:443"]);
        ExecComposeRules.AllowListEntries(AllowList).ShouldBe(["api.dhan.co:443"]);
        ExecComposeRules.AcceptedHosts.ShouldBe(["api.dhan.co:443"]);
    }
}
