using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Domain.Assistant;

namespace XDrishti.Infrastructure.Ai.Assistant;

/// <summary>
/// The tool server as a port (spec P9-03, ADR 0137): a Streamable HTTP client with the bearer token. The server is stateless, so a client is opened for each operation and closed after it (one handshake; nothing to keep alive or to hijack). The list of tools is cached for a minute and a failed list
/// for a few seconds. This is the only place that references the MCP client library. An unreachable server is a state, never an exception; the arguments the model sent are passed on untouched: the server validates them.
/// </summary>
internal sealed class McpToolCatalog(IOptions<AssistantMcpOptions> options, TimeProvider time, ILoggerFactory loggerFactory) : IToolCatalog, IDisposable
{
    private static readonly TimeSpan FailureCache = TimeSpan.FromSeconds(5);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ILogger _logger = loggerFactory.CreateLogger<McpToolCatalog>();
    private ToolCatalogSnapshot? _cached;
    private DateTimeOffset _expires;

    public async ValueTask<ToolCatalogSnapshot> ListAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.IsConfigured)
        {
            return ToolCatalogSnapshot.Unavailable(ToolCatalogState.Disabled);
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cached is not null && time.GetUtcNow() < _expires)
            {
                return _cached;
            }

            ToolCatalogSnapshot snapshot;
            var ttl = TimeSpan.FromSeconds(settings.CatalogCacheSeconds);
            try
            {
                await using var client = await ConnectAsync(settings, cancellationToken);
                var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
                snapshot = new ToolCatalogSnapshot(ToolCatalogState.Ready, [.. tools.Select(t => new ToolDescriptor(t.Name, t.Description ?? string.Empty, t.JsonSchema.GetRawText()))]);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.ToolServerUnreachable("list", ex.GetType().Name);
                snapshot = ToolCatalogSnapshot.Unavailable(ToolCatalogState.Down);
                ttl = FailureCache;
            }

            _cached = snapshot;
            _expires = time.GetUtcNow() + ttl;
            return snapshot;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask<ToolInvocation> CallAsync(string name, string argumentsJson, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var settings = options.Value;
        if (!settings.IsConfigured)
        {
            return new ToolInvocation(false, true, string.Empty);
        }

        if (!TryArguments(argumentsJson, out var arguments))
        {
            return new ToolInvocation(true, true, ToolEnvelope.Failure(name, time.GetUtcNow(), ToolErrorCodes.InvalidArguments, "The arguments must be a JSON object.").ToJson());
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.CallTimeoutSeconds));
        try
        {
            await using var client = await ConnectAsync(settings, timeout.Token);
            var result = await client.CallToolAsync(name, arguments, cancellationToken: timeout.Token);
            var text = new StringBuilder();
            foreach (var block in result.Content.OfType<TextContentBlock>())
            {
                text.Append(block.Text);
            }

            return new ToolInvocation(true, result.IsError == true, text.ToString());
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.ToolServerUnreachable("call", ex.GetType().Name);
            return new ToolInvocation(false, true, string.Empty);
        }
    }

    public void Dispose() => _lock.Dispose();

    private static bool TryArguments(string json, out Dictionary<string, object?> arguments)
    {
        arguments = [];
        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                arguments[property.Name] = property.Value.Clone();
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<McpClient> ConnectAsync(AssistantMcpOptions settings, CancellationToken cancellationToken)
    {
        // No redirects, no cookies, no proxy: an address that redirects is treated as not understood, and the token is sent to the configured address only.
        var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false });
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(settings.Url),
                Name = "xd-mcp",
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + settings.Token },
            },
            http,
            loggerFactory,
            ownsHttpClient: true);
        return await McpClient.CreateAsync(transport, loggerFactory: loggerFactory, cancellationToken: cancellationToken);
    }
}
