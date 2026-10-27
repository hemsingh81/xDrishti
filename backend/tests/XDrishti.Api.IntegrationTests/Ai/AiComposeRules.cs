using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace XDrishti.Api.IntegrationTests.Ai;

/// <summary>
/// The static rules of the <c>xd-llm</c> service in <c>deploy/compose.yaml</c> (spec P9-01 AC 2, ADR 0136). Each rule has a name; <see cref="Violations"/> lists the names that fail. It reads the file with a YAML parser (anchors and merge keys expanded), so a rule is about
/// the effective service and not about its spelling.
/// </summary>
internal static partial class AiComposeRules
{
    public const string Digest = "sha256:d60dfda3113e127071d2b2679921c763d0eb7a0733596f68bf93a4ef1ca1216e";

    public static readonly string[] All =
    [
        "profile-ai-only", "no-ports", "network-back-only", "image-tag-and-digest", "cap-drop-all", "no-new-privileges", "mem-limit", "healthcheck", "read-only-root", "models-mount-read-only", "one-secret-only", "no-secret-in-environment", "back-is-internal", "not-restarting",
    ];

    public static List<string> Violations(string composeText)
    {
        var root = Parse(composeText);
        var service = Map(Map(root, "services"), "xd-llm");
        var found = new List<string>();

        void Check(string rule, bool ok)
        {
            if (!ok)
            {
                found.Add(rule);
            }
        }

        Check("profile-ai-only", List(service, "profiles") is ["ai"]);
        Check("no-ports", !service.ContainsKey("ports"));
        Check("network-back-only", NetworkNames(service) is ["back"]);
        var image = Text(service, "image");
        Check("image-tag-and-digest", image.StartsWith("quay.io/ramalama/ramalama:0.25.0@" + Digest, StringComparison.Ordinal));
        Check("cap-drop-all", List(service, "cap_drop") is ["ALL"]);
        Check("no-new-privileges", List(service, "security_opt").Contains("no-new-privileges:true"));
        Check("mem-limit", service.ContainsKey("mem_limit") && Text(service, "mem_limit").Length > 0);
        Check("healthcheck", service.ContainsKey("healthcheck") && Map(service, "healthcheck").ContainsKey("test"));
        Check("read-only-root", Text(service, "read_only") == "true");
        var volumes = List(service, "volumes");
        Check("models-mount-read-only", volumes.Count == 1 && volumes[0].EndsWith(":/mnt/models:ro", StringComparison.Ordinal));
        Check("one-secret-only", List(service, "secrets") is ["llm_api_key"]);
        Check("no-secret-in-environment", !EnvironmentLooksSecret(service));
        Check("back-is-internal", Text(Map(root, "networks").TryGetValue("back", out var back) && back is Dictionary<string, object> b ? b : [], "internal") == "true");
        Check("not-restarting", Text(service, "restart") == "no");
        return found;
    }

    internal static bool EnvironmentLooksSecret(Dictionary<string, object> service)
    {
        // Only file paths and settings are allowed in the environment: a key, token or password by name, or a long random-looking value, is a secret in the wrong place.
        foreach (var (name, value) in Map(service, "environment"))
        {
            var text = value?.ToString() ?? string.Empty;
            var nameOk = !Regex.IsMatch(name, "(KEY|TOKEN|PASSWORD|SECRET)$", RegexOptions.IgnoreCase) || name.EndsWith("_FILE", StringComparison.Ordinal);
            if (!nameOk || RandomLooking().IsMatch(text))
            {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex("^[A-Za-z0-9+=_-]{24,}$")]
    private static partial Regex RandomLooking();

    internal static Dictionary<string, object> Parse(string text)
    {
        using var reader = new StringReader(text);
        var parsed = new DeserializerBuilder().Build().Deserialize<Dictionary<object, object>>(new MergingParser(new Parser(reader)));
        return Convert(parsed);
    }

    internal static Dictionary<string, object> Convert(Dictionary<object, object> source) =>
        source.ToDictionary(p => p.Key.ToString()!, p => p.Value is Dictionary<object, object> d ? (object)Convert(d) : p.Value, StringComparer.Ordinal);

    internal static Dictionary<string, object> Map(Dictionary<string, object> parent, string key) => parent.TryGetValue(key, out var v) && v is Dictionary<string, object> d ? d : [];

    internal static string Text(Dictionary<string, object> parent, string key) => parent.TryGetValue(key, out var v) ? v?.ToString() ?? string.Empty : string.Empty;

    internal static List<string> List(Dictionary<string, object> parent, string key) => parent.TryGetValue(key, out var v) && v is List<object> l ? [.. l.Select(x => x?.ToString() ?? string.Empty)] : [];

    internal static List<string> NetworkNames(Dictionary<string, object> service) =>
        service.TryGetValue("networks", out var v) ? v switch { List<object> l => [.. l.Select(x => x.ToString()!)], Dictionary<string, object> d => [.. d.Keys], _ => [] } : [];
}
