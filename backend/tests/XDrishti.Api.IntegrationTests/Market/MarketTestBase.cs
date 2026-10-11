using System.Net;
using System.Net.Http.Json;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Market.Refresh;
using XDrishti.Application.Market.Search;

namespace XDrishti.Api.IntegrationTests.Market;

/// <summary>
/// Starts every instrument-master test from an empty master and a clock pinned just after the sample files were taken (the SBIN September 2026
/// future in the sample is already past, the October ones are not), with the owner signed in after the pin.
/// </summary>
public abstract class MarketTestBase(ApiFactory factory) : IAsyncLifetime
{
    /// <summary>2026-10-11 09:30 in India.</summary>
    protected static readonly DateTimeOffset Now = new(2026, 10, 11, 4, 0, 0, TimeSpan.Zero);

    protected ApiFactory Factory { get; } = factory;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected SignedInClient Owner { get; private set; } = null!;

    protected HttpClient Client => Owner.Client;

    public async ValueTask InitializeAsync()
    {
        Factory.Clock.Pin(Now);
        await Factory.ResetInstrumentsAsync();
        Owner = await Factory.SignInAsync(Ct);
    }

    /// <summary>Signs in again, for a test that moved the clock far enough to end the session.</summary>
    protected async Task ResignInAsync() => Owner = await Factory.SignInAsync(Ct);

    public ValueTask DisposeAsync()
    {
        Factory.Clock.Reset();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    /// <summary>Registers a refresh (as the owner would by pressing the button) without running it yet.</summary>
    protected async Task<RefreshStarted> StartRefreshAsync(bool? force = null)
    {
        using var response = force is null ? await Client.PostAsync("/api/instruments/refresh", Ct) : await Client.PostJsonAsync("/api/instruments/refresh", new { force }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<RefreshStarted>(Ct))!;
    }

    /// <summary>Starts a refresh, lets the background worker's job run to its end and returns the finished run.</summary>
    protected async Task<RefreshRunView> RefreshAsync(bool? force = null)
    {
        var started = await StartRefreshAsync(force);
        await Factory.RefreshJobs.RunPendingAsync(Factory.Services, Ct);
        var status = await GetStatusAsync();
        status.Current.ShouldBeNull();
        status.Last!.Id.ShouldBe(started.RefreshId);
        return status.Last;
    }

    protected async Task<RefreshStatusView> GetStatusAsync()
    {
        using var response = await Client.GetAsync("/api/instruments/refresh/status", Ct);
        return await response.ReadAsync<RefreshStatusView>(Ct);
    }

    protected async Task<InstrumentPage> SearchAsync(string query = "")
    {
        using var response = await Client.GetAsync("/api/instruments" + query, Ct);
        return await response.ReadAsync<InstrumentPage>(Ct);
    }

    protected async Task<InstrumentFacets> FacetsAsync()
    {
        using var response = await Client.GetAsync("/api/instruments/facets", Ct);
        return await response.ReadAsync<InstrumentFacets>(Ct);
    }

    /// <summary>The same sample with one line replaced (the line that contains <paramref name="marker"/>).</summary>
    protected static string WithLine(string csv, string marker, Func<string, string> change) =>
        string.Join('\n', csv.Split('\n').Select(line => line.Contains(marker, StringComparison.Ordinal) ? change(line) : line));

    protected static string WithoutLine(string csv, string marker) =>
        string.Join('\n', csv.Split('\n').Where(line => !line.Contains(marker, StringComparison.Ordinal)));
}
