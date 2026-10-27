namespace XDrishti.Api.IntegrationTests.Ai;

/// <summary>Spec P9-01 AC 2: the real <c>xd-llm</c> block passes every rule, and a planted violation of each rule fails exactly that rule.</summary>
public sealed class AiComposeRulesTests
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

    /// <summary>Replaces <paramref name="from"/> by <paramref name="to"/> inside the xd-llm block only.</summary>
    private static string Plant(string from, string to)
    {
        var start = Compose.IndexOf("\n  xd-llm:", StringComparison.Ordinal);
        var end = Compose.IndexOf("\nnetworks:", start, StringComparison.Ordinal);
        var block = Compose[start..end];
        block.ShouldContain(from);
        return Compose[..start] + block.Replace(from, to, StringComparison.Ordinal) + Compose[end..];
    }

    [Fact]
    public void The_real_block_passes_every_rule() => AiComposeRules.Violations(Compose).ShouldBeEmpty();

    public static TheoryData<string, string, string> Plants => new()
    {
        { "profile-ai-only", "profiles: [ai]", "profiles: [ai, ops]" },
        { "no-ports", "restart: \"no\"", "restart: \"no\"\n    ports: [\"127.0.0.1:8080:8080\"]" },
        { "network-back-only", "networks: [back]\n    mem_limit: 8g", "networks: [back, egress]\n    mem_limit: 8g" },
        { "image-tag-and-digest", "ramalama:0.25.0@sha256:d60dfda3113e127071d2b2679921c763d0eb7a0733596f68bf93a4ef1ca1216e", "ramalama:latest" },
        { "cap-drop-all", "<<: *hardening", "read_only: true" },
        { "no-new-privileges", "<<: *hardening", "read_only: true\n    cap_drop: [ALL]" },
        { "mem-limit", "    mem_limit: 8g # PLACEHOLDER until measured (P9-01 AC 10, UNVERIFIED)\n", string.Empty },
        { "healthcheck", "    healthcheck:\n      test: [CMD, curl", "    xhealthcheck:\n      test: [CMD, curl" },
        { "read-only-root", "<<: *hardening", "cap_drop: [ALL]\n    security_opt: [no-new-privileges:true]" },
        { "models-mount-read-only", ":/mnt/models:ro", ":/mnt/models" },
        { "one-secret-only", "secrets: [llm_api_key]", "secrets: [llm_api_key, db_app_password]" },
        { "no-secret-in-environment", "HOME: /tmp", "HOME: /tmp\n      LLAMA_ARG_API_KEY: abcdefghijklmnopqrstuvwxyz0123456789" },
        { "not-restarting", "restart: \"no\"", "restart: unless-stopped" },
    };

    [Theory]
    [MemberData(nameof(Plants))]
    public void A_planted_violation_fails_its_rule(string rule, string from, string to)
    {
        AiComposeRules.Violations(Plant(from, to)).ShouldContain(rule);
    }

    [Fact]
    public void Every_rule_has_a_planted_test() => Plants.Select(p => p.Data.Item1).Append("back-is-internal").Order().ShouldBe(AiComposeRules.All.Order());

    [Fact]
    public void A_back_network_that_is_not_internal_fails()
    {
        var text = Compose.Replace("  back:\n    internal: true", "  back: {}", StringComparison.Ordinal);

        AiComposeRules.Violations(text).ShouldContain("back-is-internal");
    }

    [Fact]
    public void The_api_gets_the_key_as_a_file_secret_and_only_the_llm_service_and_the_api_mount_it()
    {
        var services = Compose[..Compose.IndexOf("\nnetworks:\n", StringComparison.Ordinal)];
        var users = services.Split("\n  xd-", StringSplitOptions.None).Where(block => block.Contains("llm_api_key", StringComparison.Ordinal)).Select(b => b.Split('\n')[0].TrimEnd(':')).ToList();

        users.ShouldBe(["api", "llm", "eval"], ignoreOrder: true, customMessage: "only xd-api, xd-llm and the eval runner (which asks the model, P9-09) may use llm_api_key");
        Compose.ShouldContain("target: Assistant__Llm__ApiKey");
    }
}
