using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace XDrishti.DhanTrial;

internal sealed record ApiResponse(int Status, string Body, string? ErrorCode, string? ErrorMessage, TimeSpan Elapsed, int Attempts)
{
    public bool Ok => Status is >= 200 and < 300 && ErrorCode is null;
}

internal sealed record CallRecord(string Endpoint, int Status, double Millis, int Bytes, string? ErrorCode, string? ErrorMessage, int Attempts);

internal interface IDhanApi
{
    Task<ApiResponse> GenerateConsentAsync(string clientId, string apiKey, string apiSecret, CancellationToken cancellationToken);

    Task<ApiResponse> ConsumeConsentAsync(string tokenId, string apiKey, string apiSecret, CancellationToken cancellationToken);

    Task<ApiResponse> GenerateAccessTokenAsync(string clientId, string pin, string totp, CancellationToken cancellationToken);

    Task<ApiResponse> RenewTokenAsync(string accessToken, string clientId, CancellationToken cancellationToken);

    Task<ApiResponse> GetProfileAsync(string accessToken, CancellationToken cancellationToken);

    Task<ApiResponse> GetIntradayAsync(string accessToken, InstrumentKey key, DateRange range, CancellationToken cancellationToken);

    Task<ApiResponse> GetDailyAsync(string accessToken, InstrumentKey key, DateRange range, CancellationToken cancellationToken);

    Task<long> DownloadMasterAsync(string destinationPath, CancellationToken cancellationToken);

    IReadOnlyList<CallRecord> Calls { get; }

    /// <summary>How the intraday "interval" field was accepted by the server: "integer" (as documented) or "string".</summary>
    string IntervalForm { get; }
}

internal readonly record struct InstrumentKey(string SecurityId, string Segment, string Instrument);

internal static class DhanErrors
{
    /// <summary>Extracts a Dhan error code/message from a response body (trading-API and data-API shapes), or HTTP status when none.</summary>
    public static (string? Code, string? Message) Parse(int status, string body)
    {
        if (!string.IsNullOrWhiteSpace(body) && body.TrimStart().StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.TryGetProperty("errorCode", out var code))
                {
                    return (code.ToString(), root.TryGetProperty("errorMessage", out var msg) ? msg.ToString() : null);
                }

                if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("status", out var st) && !string.Equals(st.ToString(), "success", StringComparison.OrdinalIgnoreCase))
                {
                    var first = data.EnumerateObject().FirstOrDefault();
                    if (first.Name is not null)
                    {
                        return (first.Name, first.Value.ToString());
                    }
                }
            }
            catch (JsonException)
            {
                // fall through to the HTTP status
            }
        }

        return status is >= 200 and < 300 ? (null, null) : ($"HTTP {status.ToString(CultureInfo.InvariantCulture)}", Truncate(body));
    }

    public static bool IsThrottle(int status, string? code) => status == 429 || code is "DH-904" or "805";

    private static string Truncate(string text) => text.Length <= 200 ? text : text[..200];
}

/// <summary>Read-only Dhan client. All traffic passes the <see cref="EndpointPolicy"/> allow-list; every call is timed and logged (never with secrets).</summary>
internal sealed class DhanApi(HttpClient http, IRateLimiter limiter, Redactor redactor, TimeProvider clock, int maxAttempts = 4) : IDhanApi
{
    public const string MasterUrl = "https://images.dhan.co/api-data/api-scrip-master.csv";
    private readonly ConcurrentQueue<CallRecord> _calls = new();
    private volatile bool _intervalAsString;

    public IReadOnlyList<CallRecord> Calls => [.. _calls];

    public string IntervalForm => _intervalAsString ? "string" : "integer";

    public Task<ApiResponse> GenerateConsentAsync(string clientId, string apiKey, string apiSecret, CancellationToken cancellationToken)
        => SendAsync("auth/generate-consent", () => Auth(HttpMethod.Post, $"https://auth.dhan.co/app/generate-consent?client_id={Uri.EscapeDataString(clientId)}", apiKey, apiSecret), false, cancellationToken);

    public Task<ApiResponse> ConsumeConsentAsync(string tokenId, string apiKey, string apiSecret, CancellationToken cancellationToken)
        => SendAsync("auth/consumeApp-consent", () => Auth(HttpMethod.Get, $"https://auth.dhan.co/app/consumeApp-consent?tokenId={Uri.EscapeDataString(tokenId)}", apiKey, apiSecret), false, cancellationToken);

    public Task<ApiResponse> GenerateAccessTokenAsync(string clientId, string pin, string totp, CancellationToken cancellationToken)
        => SendAsync(
            "auth/generateAccessToken",
            () => new HttpRequestMessage(HttpMethod.Post, $"https://auth.dhan.co/app/generateAccessToken?dhanClientId={Uri.EscapeDataString(clientId)}&pin={Uri.EscapeDataString(pin)}&totp={Uri.EscapeDataString(totp)}"),
            false,
            cancellationToken);

    public Task<ApiResponse> RenewTokenAsync(string accessToken, string clientId, CancellationToken cancellationToken)
        => SendAsync(
            "v2/RenewToken",
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "https://api.dhan.co/v2/RenewToken");
                request.Headers.Add("access-token", accessToken);
                request.Headers.Add("dhanClientId", clientId);
                return request;
            },
            false,
            cancellationToken);

    public Task<ApiResponse> GetProfileAsync(string accessToken, CancellationToken cancellationToken)
        => SendAsync(
            "v2/profile",
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "https://api.dhan.co/v2/profile");
                request.Headers.Add("access-token", accessToken);
                return request;
            },
            false,
            cancellationToken);

    public async Task<ApiResponse> GetIntradayAsync(string accessToken, InstrumentKey key, DateRange range, CancellationToken cancellationToken)
    {
        Task<ApiResponse> Send() => SendAsync(
            "v2/charts/intraday",
            () => Data("https://api.dhan.co/v2/charts/intraday", accessToken, key, DateChunker.Intraday(range.From, false), DateChunker.Intraday(range.To, true), _intervalAsString ? "1" : 1),
            true,
            cancellationToken);

        var response = await Send();
        if (!response.Ok && !_intervalAsString && response.ErrorCode is "DH-905" or "814")
        {
            // The docs say "integer" but some deployments want a string: try once with the other form and remember what worked.
            _intervalAsString = true;
            var retry = await Send();
            if (retry.Ok)
            {
                return retry;
            }

            _intervalAsString = false;
        }

        return response;
    }

    public Task<ApiResponse> GetDailyAsync(string accessToken, InstrumentKey key, DateRange range, CancellationToken cancellationToken)
        => SendAsync(
            "v2/charts/historical",
            () => Data("https://api.dhan.co/v2/charts/historical", accessToken, key, range.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), range.To.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), interval: null),
            true,
            cancellationToken);

    public async Task<long> DownloadMasterAsync(string destinationPath, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        using var request = new HttpRequestMessage(HttpMethod.Get, MasterUrl);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var temporary = destinationPath + ".tmp";
        long length;
        await using (var file = File.Create(temporary))
        {
            await response.Content.CopyToAsync(file, cancellationToken);
            length = file.Length;
        }

        File.Move(temporary, destinationPath, overwrite: true);
        _calls.Enqueue(new CallRecord("images/api-scrip-master.csv", (int)response.StatusCode, watch.Elapsed.TotalMilliseconds, (int)Math.Min(length, int.MaxValue), null, null, 1));
        return length;
    }

    private static HttpRequestMessage Auth(HttpMethod method, string url, string apiKey, string apiSecret)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("app_id", apiKey);
        request.Headers.Add("app_secret", apiSecret);
        return request;
    }

    private static HttpRequestMessage Data(string url, string accessToken, InstrumentKey key, string from, string to, object? interval)
    {
        var body = new Dictionary<string, object>
        {
            ["securityId"] = key.SecurityId,
            ["exchangeSegment"] = key.Segment,
            ["instrument"] = key.Instrument,
            ["oi"] = false,
            ["fromDate"] = from,
            ["toDate"] = to,
        };
        if (interval is not null)
        {
            body["interval"] = interval;
        }

        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        request.Headers.Add("access-token", accessToken);
        return request;
    }

    private async Task<ApiResponse> SendAsync(string endpoint, Func<HttpRequestMessage> build, bool limited, CancellationToken cancellationToken)
    {
        var total = Stopwatch.StartNew();
        for (var attempt = 1; ; attempt++)
        {
            if (limited)
            {
                await limiter.WaitAsync(cancellationToken);
            }

            var watch = Stopwatch.StartNew();
            using var request = build();
            int status;
            string body;
            try
            {
                using var response = await http.SendAsync(request, cancellationToken);
                status = (int)response.StatusCode;
                body = await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                status = 0;
                body = redactor.Redact(ex.Message);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                status = 0;
                body = "request timed out";
            }

            var (code, message) = DhanErrors.Parse(status, body);
            if (DhanErrors.IsThrottle(status, code) && attempt < maxAttempts)
            {
                _calls.Enqueue(new CallRecord(endpoint, status, watch.Elapsed.TotalMilliseconds, body.Length, code, redactor.Redact(message), attempt));
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), clock, cancellationToken);
                continue;
            }

            var safeMessage = message is null ? null : redactor.Redact(message);
            _calls.Enqueue(new CallRecord(endpoint, status, watch.Elapsed.TotalMilliseconds, body.Length, code, safeMessage, attempt));
            return new ApiResponse(status, body, code, safeMessage, total.Elapsed, attempt);
        }
    }
}
