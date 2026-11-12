using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XDrishti.Domain.Assistant;
using XDrishti.Domain.Common;
using XDrishti.Infrastructure.Ai.ReadOnly.BasketApi;

namespace XDrishti.Infrastructure.Ai.ReadOnly.EvidenceApi;

/// <summary>
/// The tool server's way to the evidence GETs (AI-01, ADR 0280): one GET with the read-only service token, answered as the API's JSON body unchanged (labels and states are the API's, never rebuilt here). It fails closed like <see cref="BasketApiClient"/>: no address or token, a transport error,
/// a timeout, a status other than 200, 404 or 422, a body that is too large or not JSON all give <c>tool.unavailable</c> with one fixed text. No retry; nothing of the request or the token is logged.
/// </summary>
internal sealed class EvidenceApiClient(HttpClient http, IOptions<BasketApiOptions> options, TimeProvider time, ILogger<EvidenceApiClient> logger)
{
    public const string UnavailableText = "stored results are not reachable now";
    public const string NotFoundCode = "evidence.not_found";

    private static Error Unavailable => new(ToolErrorCodes.Unavailable, UnavailableText);

    public async ValueTask<Result<JsonNode>> GetAsync(string route, string path, CancellationToken cancellationToken)
    {
        var started = time.GetTimestamp();
        var settings = options.Value;
        var status = 0;
        try
        {
            if (!settings.IsConfigured)
            {
                return Result.Failure<JsonNode>(Unavailable);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(settings.BaseUrl, UriKind.Absolute), path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            status = (int)response.StatusCode;
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var body = await ReadCappedAsync(response.Content, settings.MaxResponseBytes, cancellationToken);
                    return body is null || JsonNode.Parse(body) is not { } node ? Result.Failure<JsonNode>(Unavailable) : Result.Success(node);
                case HttpStatusCode.NotFound:
                    return Result.Failure<JsonNode>(Error.NotFound(NotFoundCode, "No stored result has that id."));
                case HttpStatusCode.UnprocessableEntity:
                    return Result.Failure<JsonNode>(Error.Validation(ToolErrorCodes.InvalidArguments, "The stored result query was refused."));
                default:
                    return Result.Failure<JsonNode>(Unavailable);
            }
        }
        catch (Exception ex) when ((ex is HttpRequestException or InvalidOperationException or JsonException or IOException or UriFormatException or TaskCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            // never the message: a transport error can carry the address
            return Result.Failure<JsonNode>(Unavailable);
        }
        finally
        {
            var elapsed = (long)time.GetElapsedTime(started).TotalMilliseconds;
            logger.ApiCall(route, status, elapsed);
        }
    }

    private static async Task<byte[]?> ReadCappedAsync(HttpContent content, int limit, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > limit)
        {
            return null;
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > limit)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
