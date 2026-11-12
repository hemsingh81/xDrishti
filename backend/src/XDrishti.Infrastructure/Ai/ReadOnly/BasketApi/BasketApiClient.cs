using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XDrishti.Domain.Assistant;
using XDrishti.Domain.Common;

namespace XDrishti.Infrastructure.Ai.ReadOnly.BasketApi;

/// <summary>
/// The tool server's one way to the API (BK-12b, ADR 0267): a GET of a basket report with the read-only service token, answered as a <see cref="Result{T}"/>. It fails closed: no address or token, a transport error, a timeout, a status other than 200, 404 or 422, a body that is too large or does not
/// deserialize, all give <c>tool.unavailable</c> with one fixed text and never a partial body; 404 is <c>basket.unknown</c> and 422 is <c>tool.invalid_arguments</c> with the problem's detail as untrusted text. No retry (it could pass the tool timeout). Nothing of the request, a name or the token is logged.
/// </summary>
internal sealed class BasketApiClient(HttpClient http, IOptions<BasketApiOptions> options, TimeProvider time, ILogger<BasketApiClient> logger)
{
    public const string UnavailableText = "basket reports are not reachable now";

    private const int MaxProblemBytes = 16 * 1024;
    private const int MaxDetailChars = 200;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict };

    private static Error Unavailable => new(ToolErrorCodes.Unavailable, UnavailableText);

    public async ValueTask<Result<TView>> GetAsync<TView>(string route, string pathAndQuery, CancellationToken cancellationToken)
        where TView : class
    {
        var started = time.GetTimestamp();
        var settings = options.Value;
        var status = 0;
        try
        {
            if (!settings.IsConfigured)
            {
                return Result.Failure<TView>(Unavailable);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(settings.BaseUrl, UriKind.Absolute), pathAndQuery));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            status = (int)response.StatusCode;
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var body = await CappedBody.ReadAsync(response.Content, settings.MaxResponseBytes, cancellationToken);
                    return body is null || JsonSerializer.Deserialize<TView>(body, Json) is not { } view ? Result.Failure<TView>(Unavailable) : Result.Success(view);
                case HttpStatusCode.NotFound:
                    return Result.Failure<TView>(Error.NotFound("basket.unknown", "No basket has that name or id."));
                case HttpStatusCode.UnprocessableEntity:
                    return Result.Failure<TView>(Error.Validation(ToolErrorCodes.InvalidArguments, await InvalidTextAsync(response.Content, cancellationToken)));
                default:
                    return Result.Failure<TView>(Unavailable);
            }
        }
        catch (Exception ex) when ((ex is HttpRequestException or InvalidOperationException or JsonException or IOException or UriFormatException or TaskCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            // never the message: a transport error can carry the address
            return Result.Failure<TView>(Unavailable);
        }
        finally
        {
            var elapsed = (long)time.GetElapsedTime(started).TotalMilliseconds;
            logger.ApiCall(route, status, elapsed);
        }
    }

    /// <summary>The refusal of the API, with its detail as untrusted text (cleaned and cut like any text from outside); a body that cannot be read gives the fixed refusal.</summary>
    private static async Task<string> InvalidTextAsync(HttpContent content, CancellationToken cancellationToken)
    {
        const string Prefix = "The report query was refused";
        var body = await CappedBody.ReadAsync(content, MaxProblemBytes, cancellationToken);
        if (body is null)
        {
            return Prefix + ".";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            // the API's problem puts the reason in "title" (and "detail" when a layer adds one)
            if (document.RootElement.ValueKind == JsonValueKind.Object && ProblemText(document.RootElement) is { Length: > 0 } text)
            {
                var cleaned = (string?)CellCleaner.Clean(text.Length > MaxDetailChars ? text[..MaxDetailChars] : text, true, new CleanCounters());
                return $"{Prefix} (untrusted text): {cleaned}";
            }
        }
        catch (JsonException)
        {
            // a body that is not a problem: the fixed text below
        }

        return Prefix + ".";
    }

    private static string? ProblemText(JsonElement problem) =>
        problem.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String ? detail.GetString()
        : problem.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String ? title.GetString() : null;
}
