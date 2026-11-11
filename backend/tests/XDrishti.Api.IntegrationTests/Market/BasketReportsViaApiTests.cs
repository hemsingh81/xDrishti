using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Ai;
using XDrishti.Domain.Assistant;
using XDrishti.Infrastructure.Ai.ReadOnly;
using XDrishti.Infrastructure.Ai.ReadOnly.BasketApi;

namespace XDrishti.Api.IntegrationTests.Market;

/// <summary>
/// BK-12b AC 4 to 8 and 10 (ADR 0267): the tool server's reader over the real API. The reader (<c>BasketReportReader</c>) and the tool server's HTTP-backed handlers (<c>AddAiBasketApi</c>, with the read-only service token) read the basket reports from the running API host over its test server; the
/// same reader over the API's own in-process handlers is the reference. The hand fixture of BK-06/BK-07: A 100 to 110, B 50 to 45, C 200 to 250, so the basket held +8.3333 %.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class BasketReportsViaApiTests(ApiFactory factory) : BasketsTestBase(factory)
{
    private static readonly DateOnly Start = new(2026, 9, 14);

    private static readonly DateOnly[] Sessions = [.. Enumerable.Range(0, 35).Select(i => Start.AddDays(i)).Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).Take(25)];

    private const string From = "2026-09-14";
    private const string To = "2026-10-16";

    private sealed class Data(params AiBasketRef[] baskets) : IAiDataReader
    {
        public Task<IReadOnlyList<AiBasketRef>> GetBasketRefsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AiBasketRef>>(baskets);

        public Task<AiToolData> GetBasketContextAsync(Guid basketId, DateOnly date, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AiToolData> ListLabRunsAsync(string? strategy, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AiToolData> GetPlanAsync(string? date, string? accountLabel, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AiToolData> GetCellAsync(string? key, string? strategy, string? timeframe, string? side, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AiToolData> GetPortfolioAsync(string groupBy, bool includeClosed, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AiToolData> GetBasketAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AiToolData> GetInstrumentAsync(string symbol, int bars, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AiToolData> SearchNotesAsync(string query, string? from, string? to, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AiToolData> GetLearningAsync(string kind, string? status, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AiToolData> GetSystemStatusAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>The reader as the tool server has it: the HTTP-backed handlers over the API's test server with the given token.</summary>
    private BasketReportReader OverHttp(IAiDataReader data, string? token = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Mcp:Api:BaseUrl"] = "http://xd-api:8080", ["Mcp:Api:Token"] = token ?? Factory.ServiceToken }).Build();
        var services = new ServiceCollection().AddLogging(b => b.AddProvider(HttpLogs));
        services.AddAiBasketApi(configuration);
        services.AddHttpClient<BasketApiClient>().ConfigurePrimaryHttpMessageHandler(() => Factory.Server.CreateHandler());
        return new BasketReportReader(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), data, TimeProvider.System);
    }

    /// <summary>The reference: the same reader over the API's own handlers, in process.</summary>
    private BasketReportReader InProcess(IAiDataReader data) => new(Factory.Services.GetRequiredService<IServiceScopeFactory>(), data, TimeProvider.System);

    private sealed class LogCapture : ILoggerProvider, ILogger
    {
        public List<string> Lines { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Lines.Add($"{eventId.Id} {formatter(state, exception)} {exception}");

        public void Dispose()
        {
        }
    }

    private LogCapture HttpLogs { get; } = new();

    private static Dictionary<string, string> Q(params (string Key, string Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

    private async Task<(Guid Id, Guid[] Shares, Guid Nifty)> WorldAsync(int shares = 3)
    {
        await ImportAsync();
        var ids = await SharesAsync(Math.Max(shares, 4));
        await StoreDailyAsync(ids[0], i => 100 + (10 * i / 24.0));
        await StoreDailyAsync(ids[1], i => 50 - (5 * i / 24.0));
        await StoreDailyAsync(ids[2], i => 200 + (50 * i / 24.0));
        await StoreDailyAsync(ids[3], i => 100 - i);
        var nifty = (await FindAsync("NSE:NIFTY")).Id;
        await StoreDailyAsync(nifty, i => 1000 + (i * 100.0 / 24));
        var basket = await CreateBasketAsync("Hand");
        await AddAsync(basket.Id, ids[..shares]);
        return (basket.Id, ids, nifty);
    }

    private static void ShouldMatch(AiToolData http, AiToolData reference)
    {
        http.ErrorCode.ShouldBeNull(http.ErrorMessage);
        reference.ErrorCode.ShouldBeNull(reference.ErrorMessage);
        http.Data!.ToJsonString().ShouldBe(reference.Data!.ToJsonString());
        http.Notes.ShouldBe(reference.Notes);
        System.Text.Json.JsonSerializer.Serialize(http.Evidence).ShouldBe(System.Text.Json.JsonSerializer.Serialize(reference.Evidence));
    }

    [Fact]
    public async Task The_profile_through_the_api_equals_the_in_process_shaping_field_for_field_with_the_hand_worked_hold_and_three_counted()
    {
        var (_, _, nifty) = await WorldAsync();
        var data = new Data(new AiBasketRef((await BasketAsync("Hand")).Id, "Hand"));
        var query = Q(("basket", "Hand"), ("from", From), ("to", To), ("index", nifty.ToString()));

        var http = await OverHttp(data).GetAsync("basket_profile", query, Ct);
        var reference = await InProcess(data).GetAsync("basket_profile", query, Ct);

        ShouldMatch(http, reference);
        http.Data!["hold"]!["state"]!.GetValue<string>().ShouldBe("value");
        http.Data["hold"]!["number"]!.GetValue<double>().ShouldBe(8.3333, 1e-3);
        http.Data["members"]!["counted"]!.GetValue<int>().ShouldBe(3);
        HttpLogs.Lines.ShouldContain(l => l.StartsWith("6124 mcp.api_call basket_profile status=200 ms=", StringComparison.Ordinal));
        HttpLogs.Lines.ShouldNotContain(l => l.Contains("Hand", StringComparison.Ordinal) || l.Contains(Factory.ServiceToken, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_four_member_basket_has_breadth_dispersion_and_correlation_insufficient_with_no_number_through_the_api()
    {
        var (id, _, nifty) = await WorldAsync(shares: 4);
        var data = new Data(new AiBasketRef(id, "Hand"));
        var query = Q(("basket", "Hand"), ("from", From), ("to", To), ("index", nifty.ToString()));

        var http = await OverHttp(data).GetAsync("basket_profile", query, Ct);
        var reference = await InProcess(data).GetAsync("basket_profile", query, Ct);

        ShouldMatch(http, reference);
        var body = http.Data!;
        foreach (var figure in new[] { body["breadth"]!["b20"]!["share"]!, body["dispersion"]!["today"]!, body["correlation"]!["rhoBar"]! })
        {
            figure["state"]!.GetValue<string>().ShouldBe("insufficient");
            figure.AsObject().ContainsKey("number").ShouldBeFalse();
            figure["reason"]!.GetValue<string>().ShouldBe("n = 4, at least 5 needed");
        }
    }

    [Fact]
    public async Task Three_baskets_of_the_same_members_are_not_separated_and_carry_no_rank_while_a_separated_board_ranks()
    {
        var (_, ids, nifty) = await WorldAsync();
        var refs = new List<AiBasketRef>();
        foreach (var name in new[] { "Alpha", "Beta", "Gamma" })
        {
            var basket = await CreateBasketAsync(name);
            await AddAsync(basket.Id, ids[..3]);
            refs.Add(new AiBasketRef(basket.Id, name));
        }

        var data = new Data([.. refs]);
        var tied = Q(("baskets", "Alpha,Beta,Gamma"), ("from", From), ("to", To), ("index", nifty.ToString()));

        var http = await OverHttp(data).GetAsync("basket_ranking", tied, Ct);
        var reference = await InProcess(data).GetAsync("basket_ranking", tied, Ct);

        ShouldMatch(http, reference);
        http.Data!["separated"]!.GetValue<bool>().ShouldBeFalse();
        http.Data.AsObject().ContainsKey("rankWindow").ShouldBeFalse();
        http.Data["rows"]!.AsArray().ShouldAllBe(row => !row!.AsObject().ContainsKey("rsRank"));
        http.Notes.ShouldContain(n => n.Contains("not separated", StringComparison.Ordinal));
        http.Data["overlap"]!.AsArray().Count.ShouldBe(3);
        http.Data["overlap"]![0]!["jaccard"]!["number"]!.GetValue<double>().ShouldBe(1.0, 1e-9);

        var up = await CreateBasketAsync("Up");
        await AddAsync(up.Id, ids[0], ids[2]);
        var down = await CreateBasketAsync("Down");
        await AddAsync(down.Id, ids[1], ids[3]);
        var split = new Data(new AiBasketRef(up.Id, "Up"), new AiBasketRef(down.Id, "Down"));
        var board = Q(("baskets", "Up,Down"), ("from", From), ("to", To), ("index", nifty.ToString()));

        var separated = await OverHttp(split).GetAsync("basket_ranking", board, Ct);
        var separatedReference = await InProcess(split).GetAsync("basket_ranking", board, Ct);

        ShouldMatch(separated, separatedReference);

        // whatever the engine says about this board, the model sees ranks exactly when the screen would show an order (the shaper's rule, BK-12 AC 4)
        var rows = separated.Data!["rows"]!.AsArray();
        if (separated.Data["separated"]!.GetValue<bool>())
        {
            rows.Select(r => r!["rsRank"]!["number"]!.GetValue<double>()).Order().ShouldBe([1d, 2d]);
        }
        else
        {
            rows.ShouldAllBe(row => !row!.AsObject().ContainsKey("rsRank"));
        }
    }

    [Fact]
    public async Task The_api_refusals_and_failures_fail_closed_with_the_codes_of_the_tool_contract()
    {
        var (id, _, nifty) = await WorldAsync();
        var data = new Data(new AiBasketRef(id, "Hand"), new AiBasketRef(Guid.NewGuid(), "Archived"));

        var invalid = await OverHttp(data).GetAsync("basket_profile", Q(("basket", "Hand"), ("from", "2026-10-01"), ("to", "2026-10-09")), Ct);
        var unknown = await OverHttp(data).GetAsync("basket_profile", Q(("basket", "Archived"), ("from", From), ("to", To), ("index", nifty.ToString())), Ct);
        var refused = await OverHttp(data, token: "not-the-service-token").GetAsync("basket_profile", Q(("basket", "Hand")), Ct);

        (invalid.ErrorCode, invalid.ErrorMessage).ShouldBe((ToolErrorCodes.InvalidArguments, invalid.ErrorMessage));
        invalid.ErrorMessage.ShouldStartWith("The report query was refused (untrusted text):");
        unknown.ErrorCode.ShouldBe("basket.unknown");
        refused.ErrorCode.ShouldBe(ToolErrorCodes.Unavailable);
        refused.ErrorMessage.ShouldBe("basket reports are not reachable now");
    }

    [Fact]
    public async Task The_profile_answers_in_under_a_second_and_the_series_are_cut_to_the_model_caps()
    {
        var (id, _, nifty) = await WorldAsync();
        var data = new Data(new AiBasketRef(id, "Hand"));
        var reader = OverHttp(data);
        var query = Q(("basket", "Hand"), ("from", From), ("to", To), ("index", nifty.ToString()));
        await reader.GetAsync("basket_profile", query, Ct); // the first call builds the answer; the API caches repeats (ADR 0262)

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var answer = await reader.GetAsync("basket_profile", query, Ct);
        clock.Stop();

        answer.ErrorCode.ShouldBeNull();
        clock.ElapsedMilliseconds.ShouldBeLessThan(1000);
        answer.Data!["series"]!["dates"]!.AsArray().Count.ShouldBeLessThanOrEqualTo(120);
    }

    private async Task StoreDailyAsync(Guid instrumentId, Func<int, double> close)
    {
        await using var connection = new NpgsqlConnection(Factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        for (var i = 0; i < Sessions.Length; i++)
        {
            await using var command = new NpgsqlCommand(
                "INSERT INTO market.bars_agg (instrument_id, timeframe, ts, open, high, low, close, volume, bars, complete) VALUES (@id, '1D', @ts, @c, @c, @c, @c, 1000, 375, true) ON CONFLICT DO NOTHING",
                connection);
            command.Parameters.AddWithValue("id", instrumentId);
            command.Parameters.AddWithValue("ts", new DateTimeOffset(Sessions[i].ToDateTime(TimeOnly.MinValue), TimeSpan.FromMinutes(330)).ToUniversalTime());
            command.Parameters.AddWithValue("c", close(i));
            await command.ExecuteNonQueryAsync(Ct);
        }
    }
}
