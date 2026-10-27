namespace XDrishti.Api.IntegrationTests.Ai;

/// <summary>Spec P9-10 AC 1, 2, 4, 5, 6 (ADR 0144): the real PoC definition passes every rule, a planted violation of each rule fails exactly that rule, and the stack scripts never start the profile. Nothing here starts a container or contacts a network.</summary>
public sealed class HermesComposeRulesTests
{
    private static readonly string[] StackScripts = ["deploy/xd-up.sh", "deploy/xd-down.sh", "scripts/xd-ai.sh", "scripts/xd-eval.sh", "scripts/eval-db.sh", "scripts/check.sh"];

    private static string Read(string path) => File.ReadAllText(FindUp(path.Split('/')));

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

    private static HermesFiles Real() => new(Read("deploy/compose.yaml"), Read("deploy/hermes/Dockerfile"), Read("deploy/hermes/config.yaml"), Read("deploy/hermes/entrypoint.sh"), StackScripts.ToDictionary(s => s, Read));

    private static string Replace(string text, string from, string to)
    {
        text.ShouldContain(from);
        return text.Replace(from, to, StringComparison.Ordinal);
    }

    /// <summary>Replaces <paramref name="from"/> inside the block of <paramref name="service"/> only.</summary>
    private static string PlantCompose(string compose, string service, string from, string to)
    {
        var start = compose.IndexOf($"\n  {service}:\n", StringComparison.Ordinal);
        var end = compose.IndexOf("\n\n", start + 1, StringComparison.Ordinal);
        if (end < 0 || end > compose.IndexOf("\nnetworks:", start, StringComparison.Ordinal))
        {
            end = compose.IndexOf("\nnetworks:", start, StringComparison.Ordinal);
        }

        var block = compose[start..end];
        block.ShouldContain(from);
        return compose[..start] + block.Replace(from, to, StringComparison.Ordinal) + compose[end..];
    }

    [Fact]
    public void The_real_definition_passes_every_rule() => HermesComposeRules.Violations(Real()).ShouldBeEmpty();

    /// <summary>rule, service block, text to replace, replacement. Service "-" replaces in the whole file.</summary>
    public static TheoryData<string, string, string, string> Plants => new()
    {
        { "profile-hermes-only", "xd-hermes", "profiles: [hermes]", "profiles: [hermes, ai]" },
        { "no-ports", "xd-hermes", "restart: \"no\"", "restart: \"no\"\n    ports: [\"127.0.0.1:9119:9119\"]" },
        { "network-ai-edge-only", "xd-hermes", "networks: [ai-edge]", "networks: [ai-edge, back]" },
        { "ai-edge-internal", "-", "  ai-edge:\n    internal: true", "  ai-edge: {}" },
        { "ai-edge-members", "xd-seq", "networks: [front, back]", "networks: [front, back, ai-edge]" },
        { "read-only-root", "xd-hermes", "    read_only: true\n", string.Empty },
        { "cap-drop-all", "xd-hermes", "    cap_drop: [ALL]\n", string.Empty },
        { "no-cap-add", "xd-hermes", "    cpus: 2\n", "    cpus: 2\n    cap_add: [NET_RAW]\n" },
        { "no-new-privileges", "xd-hermes", "security_opt: [no-new-privileges:true]", "security_opt: []" },
        { "no-escape-hatches", "xd-hermes", "    cpus: 2\n", "    cpus: 2\n    env_file: [x.env]\n" },
        { "no-escape-hatches", "xd-hermes", "    cpus: 2\n", "    cpus: 2\n    network_mode: host\n" },
        { "no-escape-hatches", "xd-hermes", "    cpus: 2\n", "    cpus: 2\n    pid: host\n" },
        { "no-escape-hatches", "xd-hermes", "    cpus: 2\n", "    cpus: 2\n    ipc: host\n" },
        { "no-escape-hatches", "xd-hermes", "    cpus: 2\n", "    cpus: 2\n    extra_hosts: [\"a:1.2.3.4\"]\n" },
        { "no-escape-hatches", "xd-hermes", "    cpus: 2\n", "    cpus: 2\n    dns: [1.1.1.1]\n" },
        { "no-escape-hatches", "xd-hermes", "    cpus: 2\n", "    cpus: 2\n    sysctls: {net.ipv4.ip_forward: 1}\n" },
        { "no-escape-hatches", "xd-hermes", "    cpus: 2\n", "    cpus: 2\n    volumes_from: [xd-db]\n" },
        { "no-escape-hatches", "xd-hermes", "    cpus: 2\n", "    cpus: 2\n    group_add: [0]\n" },
        { "no-escape-hatches", "xd-hermes", "    cpus: 2\n", "    cpus: 2\n    userns_mode: host\n" },
        { "no-escape-hatches", "xd-hermes", "    cpus: 2\n", "    cpus: 2\n    depends_on:\n      xd-db: { condition: service_healthy }\n" },
        { "no-escape-hatches", "xd-hermes", "    cpus: 2\n", "    cpus: 2\n    links: [xd-db]\n" },
        { "no-new-privileges", "xd-hermes", "security_opt: [no-new-privileges:true]", "security_opt: [no-new-privileges:true, seccomp=unconfined]" },
        { "tmpfs-tmp-and-run", "xd-hermes", "tmpfs: [/tmp, /run]", "tmpfs: [/tmp]" },
        { "one-named-volume", "xd-hermes", "      - xd-hermes-data:/opt/data", "      - xd-hermes-data:/opt/data\n      - ../data:/host:ro" },
        { "limits", "xd-hermes", "    pids_limit: 256\n", string.Empty },
        { "healthcheck", "xd-hermes", "      test: [CMD, hermes, --version]", "      interval: 30s" },
        { "exactly-two-secrets", "xd-hermes", "secrets: [llm_api_key, mcp_token_hermes]", "secrets: [llm_api_key, mcp_token_hermes, db_app_password]" },
        { "no-secret-in-environment", "xd-hermes", "    environment:\n", "    environment:\n      HERMES_API_TOKEN: abcdefghijklmnopqrstuvwxyz0123456789\n" },
        { "no-host-mount", "xd-hermes", "      - xd-hermes-data:/opt/data", "      - /var/run/podman/podman.sock:/opt/data" },
        { "no-messaging-or-dashboard-variable", "xd-hermes", "    environment:\n", "    environment:\n      TELEGRAM_BOT_PLATFORM: \"on\"\n" },
        { "not-restarting", "xd-hermes", "restart: \"no\"", "restart: unless-stopped" },
        { "non-root-user", "xd-hermes", "user: \"10000:10000\"", "user: \"0:0\"" },
    };

    [Theory]
    [MemberData(nameof(Plants))]
    public void A_planted_compose_violation_fails_its_rule(string rule, string service, string from, string to)
    {
        var real = Real();
        var compose = service == "-" ? Replace(real.Compose, from, to) : PlantCompose(real.Compose, service, from, to);

        HermesComposeRules.Violations(real with { Compose = compose }).ShouldContain(rule);
    }

    public static TheoryData<string, string, string, string> FilePlants => new()
    {
        { "image-pinned-by-tag-and-digest", "dockerfile", "hermes-agent:0.22.5@sha256:0000000000000000000000000000000000000000000000000000000000000000", "hermes-agent:latest" },
        { "image-pinned-by-tag-and-digest", "dockerfile", "@sha256:0000000000000000000000000000000000000000000000000000000000000000", string.Empty },
        { "image-pinned-by-tag-and-digest", "dockerfile", "0.22.5@", "stable@" },
        { "no-installer", "dockerfile", "USER 10000:10000", "RUN curl -fsSL https://example.test/install.sh | bash\nUSER 10000:10000" },
        { "no-installer", "dockerfile", "USER 10000:10000", "RUN pip install hermes-agent\nUSER 10000:10000" },
        { "no-installer", "dockerfile", "USER 10000:10000", "RUN apt-get update\nUSER 10000:10000" },
        { "one-mcp-server-read-only-tools", "config", "      resources: false", "      resources: true" },
        { "one-mcp-server-read-only-tools", "config", "calc]", "calc, search_notes]" },
        { "one-mcp-server-read-only-tools", "config", "toolsets: []", "toolsets: [terminal]" },
        { "config-cannot-drift", "config", "memory_enabled: false", "memory_enabled: true" },
        { "config-cannot-drift", "config", "write_approval: true", "write_approval: false" },
        { "config-cannot-drift", "config", "mode: manual", "mode: off" },
        { "config-cannot-drift", "config", "context_length: 65536", "context_length: 16384" },
        { "config-cannot-drift", "config", "  allowlists: {}", "  allowlists: {telegram: [1]}" },
        { "entrypoint-keeps-secrets-out-of-data", "entrypoint", "export MCP_TOKEN", "echo \"$MCP_TOKEN\" > \"$DATA/token\"\nexport MCP_TOKEN" },
        { "entrypoint-keeps-secrets-out-of-data", "entrypoint", "install -m 0600 \"$BAKED/config.yaml\" \"$DATA/config.yaml\"", "true" },
    };

    [Theory]
    [MemberData(nameof(FilePlants))]
    public void A_planted_file_violation_fails_its_rule(string rule, string file, string from, string to)
    {
        var real = Real();
        var planted = file switch
        {
            "dockerfile" => real with { Dockerfile = Replace(real.Dockerfile, from, to) },
            "config" => real with { Config = Replace(real.Config, from, to) },
            _ => real with { Entrypoint = Replace(real.Entrypoint, from, to) },
        };

        HermesComposeRules.Violations(planted).ShouldContain(rule);
    }

    [Fact]
    public void A_stack_script_that_starts_the_profile_fails()
    {
        var real = Real();
        var scripts = real.Scripts.ToDictionary(s => s.Key, s => s.Value);
        scripts["deploy/xd-up.sh"] += "\npodman compose --profile hermes up -d\n";

        HermesComposeRules.Violations(real with { Scripts = scripts }).ShouldContain("profile-never-started-by-scripts");
    }

    [Fact]
    public void Every_rule_has_a_planted_test() =>
        Plants.Select(p => p.Data.Item1).Concat(FilePlants.Select(p => p.Data.Item1)).Append("profile-never-started-by-scripts").Distinct().Order(StringComparer.Ordinal).ShouldBe(HermesComposeRules.All.Order(StringComparer.Ordinal));

    [Fact]
    public void The_up_script_writes_the_hermes_token_and_the_pin_stays_a_placeholder_until_the_owner_pins_it()
    {
        Read("deploy/xd-up.sh").ShouldContain("write_secret mcp_token_hermes \"xdrishti/mcp/hermes-token\"");
        var script = Read("scripts/hermes-poc.sh");
        script.ShouldContain("pin)");
        script.ShouldContain("read -r -p");
        script.ShouldContain("0000000000000000000000000000000000000000000000000000000000000000");
    }

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void The_entrypoint_can_run_twice_restores_the_config_clears_the_code_places_and_writes_no_secret_into_the_data_folder()
    {
        var root = Directory.CreateTempSubdirectory("hermes-entry-").FullName;
        try
        {
            var (secrets, baked, data, bin) = (Path.Combine(root, "secrets"), Path.Combine(root, "baked"), Path.Combine(root, "data"), Path.Combine(root, "bin"));
            foreach (var d in new[] { secrets, baked, data, bin })
            {
                Directory.CreateDirectory(d);
            }

            File.WriteAllText(Path.Combine(secrets, "mcp_token_hermes"), "token-value-123");
            File.WriteAllText(Path.Combine(secrets, "llm_api_key"), "key-value-456");
            var config = Path.Combine(baked, "config.yaml");
            File.WriteAllText(config, "model: {}\n");
            File.SetUnixFileMode(config, UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            var fake = Path.Combine(bin, "hermes");
            File.WriteAllText(fake, "#!/bin/sh\necho \"$MCP_TOKEN\" > /dev/null\nexit 0\n");
            File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var entry = FindUp("deploy", "hermes", "entrypoint.sh");

            int Run()
            {
                var start = new System.Diagnostics.ProcessStartInfo("sh") { RedirectStandardError = true };
                start.ArgumentList.Add(entry);
                start.Environment["XD_SECRETS_DIR"] = secrets;
                start.Environment["XD_BAKED_DIR"] = baked;
                start.Environment["XD_DATA_DIR"] = data;
                start.Environment["PATH"] = bin + ":" + Environment.GetEnvironmentVariable("PATH");
                using var process = System.Diagnostics.Process.Start(start)!;
                process.StandardError.ReadToEnd();
                process.WaitForExit();
                return process.ExitCode;
            }

            Run().ShouldBe(0);
            File.WriteAllText(Path.Combine(data, ".env"), "X=1");
            Directory.CreateDirectory(Path.Combine(data, "plugins"));
            File.WriteAllText(Path.Combine(data, "plugins", "p.py"), "x");
            Directory.CreateDirectory(Path.Combine(data, "skills"));
            File.WriteAllText(Path.Combine(data, "config.yaml"), "tampered\n");
            File.SetUnixFileMode(Path.Combine(data, "config.yaml"), UnixFileMode.UserRead);

            Run().ShouldBe(0, "the second start must work although the first copy is read-only");

            File.ReadAllText(Path.Combine(data, "config.yaml")).ShouldBe("model: {}\n");
            File.Exists(Path.Combine(data, ".env")).ShouldBeFalse();
            Directory.Exists(Path.Combine(data, "plugins")).ShouldBeFalse();
            Directory.Exists(Path.Combine(data, "skills")).ShouldBeTrue();
            Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories).Select(File.ReadAllText).ShouldAllBe(t => !t.Contains("token-value-123") && !t.Contains("key-value-456"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
