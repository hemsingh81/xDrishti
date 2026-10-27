using System.Text.RegularExpressions;

namespace XDrishti.Api.IntegrationTests.Live;

/// <summary>
/// The reverse proxy's configuration keeps the hub reachable (ADR 0024 §7, P2-10a criterion 1): <c>/hubs/*</c> goes to <c>xd-api</c> through <c>reverse_proxy</c> (which upgrades WebSocket connections by itself), in a handler of its own that
/// comes before the single-page-app fallback, and the Origin and Host headers are not rewritten (the API refuses other origins). The file is read as text; <c>scripts/check-proxy.sh</c> additionally lets Caddy validate it.
/// </summary>
public sealed partial class CaddyfileTests
{
    private static readonly string Caddyfile = File.ReadAllText(Path.Combine(RepositoryRoot(), "deploy", "caddy", "Caddyfile"));

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "deploy", "caddy", "Caddyfile")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("deploy/caddy/Caddyfile was not found above the test assembly.");
    }

    /// <summary>The text of the block that opens at <paramref name="opening"/> (a line such as <c>handle @hubs {</c>), up to its closing brace.</summary>
    private static string Block(string opening)
    {
        var start = Caddyfile.IndexOf(opening, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, $"'{opening}' is not in the Caddyfile");
        var depth = 0;
        for (var i = start; i < Caddyfile.Length; i++)
        {
            depth += Caddyfile[i] switch { '{' => 1, '}' => -1, _ => 0 };
            if (depth == 0 && Caddyfile[i] == '}')
            {
                return Caddyfile[start..(i + 1)];
            }
        }

        throw new InvalidOperationException($"'{opening}' is not closed.");
    }

    [Fact]
    public void The_hub_path_is_matched_and_proxied_to_the_api()
    {
        HubsMatcher().IsMatch(Caddyfile).ShouldBeTrue("the Caddyfile has no '@hubs path /hubs/*' matcher");

        var handler = Block("handle @hubs");

        handler.ShouldContain("reverse_proxy xd-api:8080");
        handler.ShouldNotContain("file_server");
        handler.ShouldNotContain("try_files");
    }

    [Fact]
    public void The_hub_handler_comes_before_the_single_page_app_fallback()
    {
        var hubs = Caddyfile.IndexOf("handle @hubs", StringComparison.Ordinal);
        var spa = Caddyfile.IndexOf("try_files {path} /index.html", StringComparison.Ordinal);

        hubs.ShouldBeGreaterThanOrEqualTo(0);
        spa.ShouldBeGreaterThan(hubs);
    }

    [Fact]
    public void The_hub_connection_is_streamed_and_the_origin_and_host_reach_the_api_unchanged()
    {
        var handler = Block("handle @hubs");

        handler.ShouldContain("flush_interval -1"); // a long-lived connection is not buffered
        handler.ShouldNotContain("header_up"); // no rewriting of Host or Origin: xd-api checks the origin
    }

    [Fact]
    public void The_hub_is_not_served_from_a_path_the_proxy_answers_itself()
    {
        // the SPA fallback and the unguarded-docs refusal must not catch /hubs: the documents guard only matches the prototype and docs paths
        var guard = Block("handle @unguardedDocs");

        guard.ShouldNotContain("hubs");
        Caddyfile.ShouldContain("(prototype|docs)");
    }

    [Fact]
    public void The_api_is_no_store_the_manifest_has_its_type_and_there_is_still_one_listener()
    {
        // P8-21 (ADR 0132): no-store on the API unless the response says otherwise; the manifest has its type; one listener only.
        Block("handle @api").ShouldContain("header ?Cache-Control \"no-store\"");
        Caddyfile.ShouldContain("header /manifest.webmanifest Content-Type \"application/manifest+json\"");
        Regex.Count(Caddyfile, @"^:\d+ \{", RegexOptions.Multiline).ShouldBe(1);
        Caddyfile.ShouldContain(":8080 {");
        Caddyfile.ShouldNotContain("worker-src"); // the CSP's script-src 'self' already covers /sw.js
        Caddyfile.ShouldNotContain("0.0.0.0");
    }

    [Fact]
    public void The_assistant_chat_stream_is_flushed_at_once_and_is_not_compressed()
    {
        // P9-03 AC 14: a text/event-stream is not compressed (a compressor holds it back) and is not buffered; everything else is still compressed. scripts/check-proxy.sh proves it end to end with a 2 s gap.
        Caddyfile.ShouldContain("@compressible not path /api/assistant/chat");
        Caddyfile.ShouldContain("encode @compressible zstd gzip");
        Regex.IsMatch(Caddyfile, @"^\s*encode\s+zstd", RegexOptions.Multiline).ShouldBeFalse("a bare 'encode' would compress the chat stream");

        var handler = Block("handle @chat");
        handler.ShouldContain("reverse_proxy xd-api:8080");
        handler.ShouldContain("flush_interval -1");
        handler.ShouldNotContain("encode");
        handler.ShouldNotContain("file_server");
        Caddyfile.ShouldContain("@chat path /api/assistant/chat");
        Caddyfile.IndexOf("handle @chat", StringComparison.Ordinal).ShouldBeLessThan(Caddyfile.IndexOf("handle @api", StringComparison.Ordinal));
    }

    [GeneratedRegex(@"^\s*@hubs\s+path\s+/hubs/\*\s*$", RegexOptions.Multiline)]
    private static partial Regex HubsMatcher();
}
