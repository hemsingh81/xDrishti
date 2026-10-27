using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Infrastructure.Ai.Llm;

/// <summary>
/// Looks at the local llama.cpp server over HTTP only (spec P9-01): <c>GET /health</c> without the key (503 while loading, 200 when ready), then <c>GET /v1/models</c> with it (401 = key rejected; the expected alias must be listed), then <c>GET /props</c>
/// for the file name and build. The answer is cached a few seconds and its text comes from <see cref="LlmDetails"/>, so no exception message, address or key can reach a caller. Nothing here is called at start-up or by a health check.
/// </summary>
internal sealed class HttpLlmHealth(IHttpClientFactory clients, IOptions<LlmOptions> options, TimeProvider time, ILogger<HttpLlmHealth> logger) : ILlmHealth, IDisposable
{
    public const string ClientName = "llm-status";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private LlmStatus? _cached;

    public async Task<LlmStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            return Make(LlmState.Down, LlmDetails.SwitchedOff);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = time.GetUtcNow();
            if (_cached is { } cached && now - cached.CheckedAt < TimeSpan.FromSeconds(settings.StatusCacheSeconds) && now >= cached.CheckedAt)
            {
                return cached;
            }

            var fresh = await LookAsync(settings, cancellationToken);
            _cached = fresh;
            logger.Status(fresh.State);
            return fresh;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private LlmStatus Make(LlmState state, string detail, string? model = null, string? file = null, string? sha = null, string? build = null) =>
        new(state, model, file, sha, build, time.GetUtcNow(), detail);

    private async Task<LlmStatus> LookAsync(LlmOptions settings, CancellationToken cancellationToken)
    {
        if (!settings.HasValidBaseUrl())
        {
            return Make(LlmState.Misconfigured, LlmDetails.Unexpected);
        }

        if (string.IsNullOrEmpty(settings.ApiKey))
        {
            return Make(LlmState.Misconfigured, LlmDetails.KeyNotSet);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.HealthTimeoutSeconds));
        try
        {
            var client = clients.CreateClient(ClientName);
            var root = new Uri(new Uri(settings.BaseUrl), "/");

            using (var health = await SendAsync(client, new Uri(root, "health"), null, timeout.Token))
            {
                if (health.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    return Make(LlmState.Loading, LlmDetails.Loading);
                }

                if (health.StatusCode != HttpStatusCode.OK)
                {
                    return Make(LlmState.Down, LlmDetails.NoAnswer);
                }
            }

            string? served;
            using (var models = await SendAsync(client, new Uri(settings.BaseUrl.TrimEnd('/') + "/models"), settings.ApiKey, timeout.Token))
            {
                if (models.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    return Make(LlmState.Misconfigured, LlmDetails.KeyRejected);
                }

                if (models.StatusCode != HttpStatusCode.OK)
                {
                    return Make(LlmState.Misconfigured, LlmDetails.Unexpected);
                }

                served = ReadModelIds(await models.Content.ReadAsStringAsync(timeout.Token)) is { } ids && ids.Contains(settings.Model, StringComparer.Ordinal) ? settings.Model : null;
                if (served is null)
                {
                    return Make(LlmState.Misconfigured, LlmDetails.OtherModel);
                }
            }

            var (file, build) = await ReadPropsAsync(client, new Uri(root, "props"), settings.ApiKey, timeout.Token);
            var sha = file is not null && settings.KnownModels.TryGetValue(file, out var known) ? known : null;
            return Make(LlmState.Ready, LlmDetails.Ready, served, file, sha, build);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Make(LlmState.Down, LlmDetails.NoAnswer);
        }
        catch (HttpRequestException)
        {
            return Make(LlmState.Down, LlmDetails.NoAnswer);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException)
        {
            logger.StatusFailed(ex.GetType().Name);
            return Make(LlmState.Down, LlmDetails.Unexpected);
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, Uri uri, string? key, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (key is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }

        return await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
    }

    private static List<string>? ReadModelIds(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return [.. data.EnumerateArray().Select(m => m.ValueKind == JsonValueKind.Object && m.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString()! : string.Empty).Where(id => id.Length > 0)];
    }

    /// <summary>The loaded file's base name and the build text from <c>/props</c>; either may be missing, and a failure here never changes the state.</summary>
    private static async Task<(string? File, string? Build)> ReadPropsAsync(HttpClient client, Uri uri, string key, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendAsync(client, uri, key, cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return (null, null);
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            string? file = null, build = null;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("model_path", out var path) && path.ValueKind == JsonValueKind.String && path.GetString() is { Length: > 0 } text)
                {
                    var name = text[(text.LastIndexOfAny(['/', '\\']) + 1)..];
                    file = name.Length is > 0 and <= 200 ? name : null;
                }

                if (root.TryGetProperty("build_info", out var info) && info.ValueKind == JsonValueKind.String)
                {
                    var value = info.GetString();
                    build = value is { Length: > 0 and <= 100 } ? value : null;
                }
            }

            return (file, build);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException)
        {
            return (null, null);
        }
    }
}
