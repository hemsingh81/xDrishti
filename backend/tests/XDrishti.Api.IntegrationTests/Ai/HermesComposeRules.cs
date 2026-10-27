using System.Text.RegularExpressions;

namespace XDrishti.Api.IntegrationTests.Ai;

/// <summary>The files of the Hermes PoC that the static rules read.</summary>
internal sealed record HermesFiles(string Compose, string Dockerfile, string Config, string Entrypoint, IReadOnlyDictionary<string, string> Scripts);

/// <summary>
/// The static rules of the Hermes Agent PoC (spec P9-10 AC 1, 2, 4, 5, 6; ADR 0144): the compose service <c>xd-hermes</c>, the network <c>ai-edge</c>, the Dockerfile, the baked configuration, the entrypoint and the stack scripts that must never start the profile.
/// Same method as <see cref="McpComposeRules"/>: each rule has a name, <see cref="Violations"/> lists the names that fail, YAML is parsed (anchors expanded).
/// </summary>
internal static partial class HermesComposeRules
{
    public static readonly string[] All =
    [
        "profile-hermes-only", "no-ports", "network-ai-edge-only", "ai-edge-internal", "ai-edge-members", "read-only-root", "cap-drop-all", "no-cap-add", "no-new-privileges", "tmpfs-tmp-and-run", "one-named-volume", "limits", "healthcheck", "exactly-two-secrets",
        "no-secret-in-environment", "no-host-mount", "no-messaging-or-dashboard-variable", "not-restarting", "non-root-user", "image-pinned-by-tag-and-digest", "no-installer", "one-mcp-server-read-only-tools", "config-cannot-drift", "entrypoint-keeps-secrets-out-of-data", "profile-never-started-by-scripts", "no-escape-hatches",
    ];

    public static readonly string[] EscapeKeys = ["env_file", "network_mode", "pid", "ipc", "extra_hosts", "dns", "sysctls", "volumes_from", "group_add", "userns_mode", "depends_on", "links", "privileged", "devices", "cap_add"];

    public static readonly string[] IncludeList =
    [
        "list_tables", "describe_table", "run_sql", "lab_validate", "get_report", "list_runs", "get_plan", "get_cell", "get_portfolio", "get_basket", "get_instrument", "get_learning", "get_system_status", "calc",
    ];

    public static List<string> Violations(HermesFiles files)
    {
        var root = AiComposeRules.Parse(files.Compose);
        var services = AiComposeRules.Map(root, "services");
        var service = AiComposeRules.Map(services, "xd-hermes");
        var networks = AiComposeRules.Map(root, "networks");
        var found = new List<string>();

        void Check(string rule, bool ok)
        {
            if (!ok)
            {
                found.Add(rule);
            }
        }

        var volumes = AiComposeRules.List(service, "volumes");
        Check("profile-hermes-only", AiComposeRules.List(service, "profiles") is ["hermes"]);
        Check("no-ports", !service.ContainsKey("ports") && !service.ContainsKey("expose"));
        Check("network-ai-edge-only", AiComposeRules.NetworkNames(service) is ["ai-edge"]);
        Check("ai-edge-internal", AiComposeRules.Text(AiComposeRules.Map(networks, "ai-edge"), "internal") == "true");
        var members = services.Keys.Where(n => AiComposeRules.NetworkNames(AiComposeRules.Map(services, n)).Contains("ai-edge")).Order(StringComparer.Ordinal).ToList();
        Check("ai-edge-members", members.SequenceEqual(["xd-hermes", "xd-llm", "xd-mcp", "xd-mcp-eval"]));
        Check("read-only-root", AiComposeRules.Text(service, "read_only") == "true");
        Check("cap-drop-all", AiComposeRules.List(service, "cap_drop") is ["ALL"]);
        Check("no-cap-add", !service.ContainsKey("cap_add"));
        Check("no-new-privileges", AiComposeRules.List(service, "security_opt") is ["no-new-privileges:true"]);
        Check("no-escape-hatches", !EscapeKeys.Any(service.ContainsKey));
        Check("tmpfs-tmp-and-run", AiComposeRules.List(service, "tmpfs").Order(StringComparer.Ordinal).SequenceEqual(["/run", "/tmp"]));
        Check("one-named-volume", volumes is ["xd-hermes-data:/opt/data"]);
        Check("limits", AiComposeRules.Text(service, "mem_limit").Length > 0 && AiComposeRules.Text(service, "pids_limit") == "256" && AiComposeRules.Text(service, "cpus").Length > 0);
        Check("healthcheck", service.ContainsKey("healthcheck") && AiComposeRules.Map(service, "healthcheck").ContainsKey("test"));
        Check("exactly-two-secrets", McpComposeRules.Secrets(service).Order(StringComparer.Ordinal).SequenceEqual(["llm_api_key", "mcp_token_hermes"]));
        Check("no-secret-in-environment", !AiComposeRules.EnvironmentLooksSecret(service));
        Check("no-host-mount", !volumes.Any(v => v.StartsWith('/') || v.StartsWith('.') || v.StartsWith('~') || v.Contains("podman.sock", StringComparison.Ordinal)) && !service.ContainsKey("devices") && !service.ContainsKey("privileged"));
        Check("no-messaging-or-dashboard-variable", !AiComposeRules.Map(service, "environment").Keys.Any(k => Forbidden().IsMatch(k)));
        Check("not-restarting", AiComposeRules.Text(service, "restart") == "no");
        Check("non-root-user", AiComposeRules.Text(service, "user") == "10000:10000");

        var lines = files.Dockerfile.Split('\n');
        var froms = lines.Where(l => l.StartsWith("FROM ", StringComparison.Ordinal)).ToList();
        Check("image-pinned-by-tag-and-digest", froms.Count == 1 && Pinned().IsMatch(froms[0]));
        Check("no-installer", !lines.Where(l => !l.TrimStart().StartsWith('#')).Any(l => Installer().IsMatch(l)));

        var config = AiComposeRules.Parse(files.Config);
        var mcp = AiComposeRules.Map(config, "mcp_servers");
        var xd = AiComposeRules.Map(mcp, "xd");
        var tools = AiComposeRules.Map(xd, "tools");
        Check(
            "one-mcp-server-read-only-tools",
            mcp.Keys.SequenceEqual(["xd"]) && AiComposeRules.List(tools, "include").SequenceEqual(IncludeList) && AiComposeRules.Text(tools, "resources") == "false" && AiComposeRules.Text(tools, "prompts") == "false"
            && AiComposeRules.Text(xd, "url") == "${XD_MCP_URL}" && config.ContainsKey("toolsets") && AiComposeRules.List(config, "toolsets").Count == 0);
        var model = AiComposeRules.Map(config, "model");
        var skills = AiComposeRules.Map(config, "skills");
        Check(
            "config-cannot-drift",
            AiComposeRules.Text(AiComposeRules.Map(config, "memory"), "memory_enabled") == "false" && AiComposeRules.Text(skills, "write_approval") == "true" && AiComposeRules.Text(skills, "guard_agent_created") == "true"
            && AiComposeRules.Text(AiComposeRules.Map(config, "approvals"), "mode") == "manual" && AiComposeRules.Text(model, "provider") == "custom" && AiComposeRules.Text(model, "base_url") == "http://xd-llm:8080/v1" && AiComposeRules.Text(model, "context_length") == "65536"
            && AiComposeRules.Map(AiComposeRules.Map(config, "gateway"), "allowlists").Count == 0 && AiComposeRules.Text(AiComposeRules.Map(config, "auxiliary"), "base_url") == "http://xd-llm:8080/v1");

        // The entrypoint reads the two files into its own environment, copies the baked config over the data volume and writes nothing else there.
        var code = files.Entrypoint.Split('\n').Where(l => !l.TrimStart().StartsWith('#')).ToList();
        bool Mentions(string line) => line.Contains("MCP_TOKEN", StringComparison.Ordinal) || line.Contains("OPENAI_API_KEY", StringComparison.Ordinal);
        Check(
            "entrypoint-keeps-secrets-out-of-data",
            !code.Any(l => (l.Contains("$DATA", StringComparison.Ordinal) || l.Contains("/opt/data", StringComparison.Ordinal)) && (Mentions(l) || l.Contains("SECRETS", StringComparison.Ordinal) || l.Contains("/run/secrets", StringComparison.Ordinal))) && !code.Any(l => l.Contains("echo", StringComparison.Ordinal) && Mentions(l))
            && !code.Any(l => l.Contains("MCP_TOKEN", StringComparison.Ordinal) && l.Contains('>'))
            && code.Any(l => l.Contains("install -m 0600 \"$BAKED/config.yaml\" \"$DATA/config.yaml\"", StringComparison.Ordinal)));

        // Nothing but scripts/hermes-poc.sh may start the profile.
        Check("profile-never-started-by-scripts", !files.Scripts.Any(s => s.Value.Split('\n').Where(l => !l.TrimStart().StartsWith('#')).Any(l => StartsProfile().IsMatch(l))));
        return found;
    }

    [GeneratedRegex("^(HERMES_DASHBOARD|API_SERVER_ENABLED|TELEGRAM|WHATSAPP|DISCORD|SLACK|SIGNAL|EMAIL|SMTP|MATRIX)", RegexOptions.IgnoreCase)]
    private static partial Regex Forbidden();

    [GeneratedRegex(@"^FROM docker\.io/nousresearch/hermes-agent:\d+\.\d+\.\d+@sha256:[0-9a-f]{64}\s*$")]
    private static partial Regex Pinned();

    [GeneratedRegex(@"\b(curl|wget|pip3?|apt|apt-get|apk|npm|npx|yarn|uv)\b|git\s+clone|\|\s*(ba)?sh\b|\bADD\s+https?:", RegexOptions.IgnoreCase)]
    private static partial Regex Installer();

    [GeneratedRegex(@"--profile[ =]hermes|COMPOSE_PROFILES[^\n]*hermes|\b(up|start|run|create)\b[^\n]*xd-hermes", RegexOptions.IgnoreCase)]
    private static partial Regex StartsProfile();
}
