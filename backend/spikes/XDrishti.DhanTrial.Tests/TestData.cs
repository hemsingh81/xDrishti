using System.Net;
using System.Text;

namespace XDrishti.DhanTrial.Tests;

internal static class TestData
{
    public static readonly DateOnly Monday = new(2026, 10, 5);

    /// <summary>A full session of 1-minute bars (09:15–15:29 IST) with the requested epoch convention; <paramref name="skip"/> removes minute-of-day values.</summary>
    public static List<Bar> Session(DateOnly date, EpochBase epoch = EpochBase.TrueUtc, BarLabel label = BarLabel.Start, IEnumerable<int>? skip = null, long volume = 100, double price = 100)
    {
        var skipped = skip?.ToHashSet() ?? [];
        var bars = new List<Bar>();
        for (var minute = 9 * 60 + 15; minute <= 15 * 60 + 29; minute++)
        {
            if (skipped.Contains(minute))
            {
                continue;
            }

            var labelled = minute + (label == BarLabel.End ? 1 : 0);
            bars.Add(new Bar(Epoch(date, labelled, epoch), price, price + 1, price - 1, price + 0.5, volume));
        }

        return bars;
    }

    /// <summary>One full session per weekday in the range, like the real API returns for a window.</summary>
    public static List<Bar> Sessions(DateRange range, double price = 100)
    {
        var bars = new List<Bar>();
        for (var d = range.From; d <= range.To; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                bars.AddRange(Session(d, price: price));
            }
        }

        return bars;
    }

    public static string DailyJson(DateRange range)
    {
        var days = new List<DateOnly>();
        for (var d = range.From; d <= range.To; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                days.Add(d);
            }
        }

        var bars = days.Select(d => new Bar(new DateTimeOffset(d.Year, d.Month, d.Day, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(), 100, 101, 99, 100.5, 375 * 100));
        return CandleJson(bars);
    }

    public static long Epoch(DateOnly date, int minuteOfDay, EpochBase epoch)
    {
        var local = date.ToDateTime(TimeOnly.MinValue).AddMinutes(minuteOfDay);
        var utc = epoch == EpochBase.IstAsUtc ? local : local - IstTime.Offset;
        return new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeSeconds();
    }

    public static string CandleJson(IEnumerable<Bar> bars)
    {
        var list = bars.ToArray();
        string Arr(Func<Bar, string> f) => "[" + string.Join(",", list.Select(f)) + "]";
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return "{\"open\":" + Arr(b => b.Open.ToString(inv)) + ",\"high\":" + Arr(b => b.High.ToString(inv)) + ",\"low\":" + Arr(b => b.Low.ToString(inv))
            + ",\"close\":" + Arr(b => b.Close.ToString(inv)) + ",\"volume\":" + Arr(b => b.Volume.ToString(inv)) + ",\"timestamp\":" + Arr(b => b.Epoch.ToString(inv)) + "}";
    }
}

/// <summary>Scripted HTTP responses; records every request (method, URL, headers) for assertions.</summary>
internal sealed class FakeHttp(Func<HttpRequestMessage, (HttpStatusCode Status, string Body)> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    public List<string> Bodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
        var (status, body) = respond(request);
        return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}

internal sealed class FakeApi : IDhanApi
{
    public Func<ApiResponse> Consent { get; set; } = () => Ok("{\"consentAppId\":\"consent-123\"}");

    public Func<ApiResponse> Consume { get; set; } = () => Ok("{\"dhanClientId\":\"1000000001\",\"accessToken\":\"eyJhbGciOiJIUzUxMiJ9.payload-part.signature-part\",\"expiryTime\":\"2026-10-10T20:30:00\"}");

    public Func<ApiResponse> Renew { get; set; } = () => Ok("{\"accessToken\":\"eyJhbGciOiJIUzUxMiJ9.renewed-payload.renewed-signature\",\"expiryTime\":\"2026-10-11T20:30:00\"}");

    public Func<ApiResponse> Totp { get; set; } = () => Ok("{\"accessToken\":\"eyJhbGciOiJIUzUxMiJ9.totp-payload.totp-signature\",\"expiryTime\":\"2026-10-10T21:00:00\"}");

    public Func<ApiResponse> Profile { get; set; } = () => Ok("{\"tokenValidity\":\"10/10/2026 20:30\",\"dataPlan\":\"Active\",\"dataValidity\":\"2026-11-08 12:00:00\",\"activeSegment\":\"Equity, Derivative\"}");

    public Func<DateRange, ApiResponse> Intraday { get; set; } = _ => Ok(TestData.CandleJson(TestData.Session(TestData.Monday)));

    public Func<DateRange, ApiResponse> Daily { get; set; } = _ => Ok("{}");

    public List<string> Log { get; } = [];

    public IReadOnlyList<CallRecord> Calls => [];

    public string IntervalForm => "integer";

    public string MasterCsv { get; set; } = string.Empty;

    public static ApiResponse Ok(string body) => new(200, body, null, null, TimeSpan.FromMilliseconds(5), 1);

    public static ApiResponse Fail(string code, string message) => new(400, "{}", code, message, TimeSpan.FromMilliseconds(5), 1);

    public Task<ApiResponse> GenerateConsentAsync(string clientId, string apiKey, string apiSecret, CancellationToken cancellationToken)
    {
        Log.Add($"consent:{clientId}:{apiKey}");
        return Task.FromResult(Consent());
    }

    public Task<ApiResponse> ConsumeConsentAsync(string tokenId, string apiKey, string apiSecret, CancellationToken cancellationToken)
    {
        Log.Add($"consume:{tokenId}");
        return Task.FromResult(Consume());
    }

    public Task<ApiResponse> GenerateAccessTokenAsync(string clientId, string pin, string totp, CancellationToken cancellationToken)
    {
        Log.Add($"totp:{totp}");
        return Task.FromResult(Totp());
    }

    public Task<ApiResponse> RenewTokenAsync(string accessToken, string clientId, CancellationToken cancellationToken)
    {
        Log.Add("renew");
        return Task.FromResult(Renew());
    }

    public Task<ApiResponse> GetProfileAsync(string accessToken, CancellationToken cancellationToken) => Task.FromResult(Profile());

    public Task<ApiResponse> GetIntradayAsync(string accessToken, InstrumentKey key, DateRange range, CancellationToken cancellationToken)
    {
        Log.Add($"intraday:{range.From:yyyy-MM-dd}..{range.To:yyyy-MM-dd}");
        return Task.FromResult(Intraday(range));
    }

    public Task<ApiResponse> GetDailyAsync(string accessToken, InstrumentKey key, DateRange range, CancellationToken cancellationToken)
    {
        Log.Add($"daily:{range.From:yyyy-MM-dd}..{range.To:yyyy-MM-dd}");
        return Task.FromResult(Daily(range));
    }

    public Task<long> DownloadMasterAsync(string destinationPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.WriteAllText(destinationPath, MasterCsv);
        return Task.FromResult((long)MasterCsv.Length);
    }
}

internal sealed class FakeSecrets(Dictionary<string, string> values) : ISecretSource
{
    public string? Get(string name) => values.GetValueOrDefault(name);
}

internal sealed class MemoryTokenStore : ITokenStore
{
    public AccessToken? Saved { get; private set; }

    public AccessToken? Load() => Saved;

    public void Save(AccessToken token) => Saved = token;
}

internal sealed class MemoryRawStore : IRawStore
{
    public Dictionary<string, string> Files { get; } = [];

    public string? Read(string key) => Files.GetValueOrDefault(key);

    public void Write(string key, string content) => Files[key] = content;
}

internal sealed class ScriptedPrompt(string hiddenAnswer) : IPrompt
{
    public List<string> Shown { get; } = [];

    public void Info(string text) => Shown.Add(text);

    public string ReadHidden(string label) => hiddenAnswer;
}
