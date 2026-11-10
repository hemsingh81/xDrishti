using System.Text;
using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Api.IntegrationTests.Infrastructure;

/// <summary>
/// The system baskets' file server and settings of the suite: files are served from memory (the network is never reached), the fetch setting is OFF until a test turns it on, and every request is counted so that a test can show zero calls.
/// </summary>
public sealed class FakeBasketSources : IBasketSourceFetcher, IBasketFetchSettings, IListSettings
{
    private readonly Dictionary<string, SourceResponse> _files = [];

    public int Calls { get; private set; }

    public bool Enabled { get; set; }

    /// <summary><c>Baskets:System:Lists:FetchEnabled</c> of the suite: OFF until a test turns it on.</summary>
    public bool FetchEnabled { get; set; }

    public IReadOnlyList<TimeOnly> BanRunTimes => [new(19, 0), new(8, 30)];

    public TimeOnly LotSizeRunAt => new(8, 50);

    public string UserAgent => "xDrishti integration tests (owner@example.invalid)";

    public FetchLimits Limits => FetchLimits.Default;

    public string PreferredHost => "A";

    public TimeOnly RunAt => new(7, 30);

    public IReadOnlyList<int> BackoffMinutes => [5, 30, 120];

    public int StaleAfterDays => 3;

    public int BackoffHours => 24;

    public IReadOnlyList<int> ReviewMonths => [3, 6, 9, 12];

    public int ReviewFromDay => 20;

    public int ReviewToDay => 7;

    public string Snapshot() => "{\"enabled\":\"" + Enabled + "\"}";

    public void Reset()
    {
        _files.Clear();
        Calls = 0;
        Enabled = false;
        FetchEnabled = false;
    }

    /// <summary>Serves this answer for the file name (the url's last segment).</summary>
    public void Serve(string fileName, string body, int status = 200, string contentType = "text/csv") =>
        _files[fileName] = new SourceResponse(status, contentType, Encoding.UTF8.GetBytes(body), "\"" + body.Length + "\"", null, null);

    public ValueTask<SourceResponse> GetAsync(BasketSourceInfo source, CancellationToken cancellationToken)
    {
        Calls++;
        return ValueTask.FromResult(_files.TryGetValue(source.FileName, out var response) ? response : new SourceResponse(404, "text/html", Encoding.UTF8.GetBytes("<html>Not Found</html>"), null, null, null));
    }
}
