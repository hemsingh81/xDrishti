using Microsoft.Extensions.Time.Testing;

namespace XDrishti.DhanTrial.Tests;

public sealed class RunnerTests : IDisposable
{
    private const string Master = "SEM_EXM_EXCH_ID,SEM_SEGMENT,SEM_SMST_SECURITY_ID,SEM_INSTRUMENT_NAME,SEM_EXPIRY_CODE,SEM_TRADING_SYMBOL,SEM_LOT_UNITS,SEM_CUSTOM_SYMBOL,SEM_EXPIRY_DATE,SEM_STRIKE_PRICE,SEM_OPTION_TYPE,SEM_TICK_SIZE,SEM_EXPIRY_FLAG,SEM_EXCH_INSTRUMENT_TYPE,SEM_SERIES,SM_SYMBOL_NAME\n"
        + "NSE,E,2885,EQUITY,0,DEEPCO,1,Deep Co,,,,0.05,,ES,EQ,DEEP CO\n"
        + "NSE,E,1111,EQUITY,0,SHORTCO,1,Short Co,,,,0.05,,ES,EQ,SHORT CO\n"
        + "NSE,I,13,INDEX,0,NIFTY,1,Nifty 50,,,,0,,INDEX,,NIFTY 50\n";

    private static readonly DateTimeOffset Now = new(2026, 10, 9, 20, 0, 0, IstTime.Offset);
    private static readonly string[] Secrets = ["client-id-value", "api-key-value", "api-secret-value", "token-id-from-browser"];

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "xd-dhan-trial-" + Guid.NewGuid().ToString("N"));

    public RunnerTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(
            Path.Combine(_dir, "symbols.json"),
            "{\"recentDays\":45,\"deepMonths\":3,\"deep\":[\"DEEPCO\"],\"symbols\":[{\"name\":\"DEEPCO\",\"kind\":\"equity\"},{\"name\":\"SHORTCO\",\"kind\":\"equity\"},{\"name\":\"NIFTY 50\",\"kind\":\"index\",\"aliases\":[\"NIFTY\"]},{\"name\":\"GHOST\",\"kind\":\"equity\"}]}");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static FakeApi NewApi() => new()
    {
        MasterCsv = Master,
        Intraday = r => FakeApi.Ok(TestData.CandleJson(TestData.Sessions(r))),
        Daily = r => FakeApi.Ok(TestData.DailyJson(r)),
    };

    private (TrialRunner Runner, FakeApi Api, MemoryTokenStore Tokens) Create(TrialOptions options, FakeApi? api = null)
    {
        api ??= NewApi();
        var redactor = new Redactor();
        var clock = new FakeTimeProvider(Now);
        var tokens = new MemoryTokenStore();
        var prompt = new ScriptedPrompt("token-id-from-browser");
        var flow = new TokenFlow(api, tokens, prompt, redactor, clock);
        var totpFlow = new TokenFlow(api, new MemoryTokenStore(), prompt, redactor, clock);
        var secrets = new FakeSecrets(new Dictionary<string, string> { ["client-id"] = "client-id-value", ["api-key"] = "api-key-value", ["api-secret"] = "api-secret-value" });
        var runner = new TrialRunner(api, api, flow, totpFlow, tokens, secrets, new FileRawStore(Path.Combine(_dir, "raw")), options, clock, redactor, _ => { });
        return (runner, api, tokens);
    }

    private TrialOptions Options(bool offline = false, DateOnly? today = null)
        => new(offline, false, false, false, _dir, Path.Combine(_dir, "symbols.json"), today ?? new DateOnly(2026, 10, 9));

    [Fact]
    public async Task A_short_window_symbol_is_not_penalised_for_days_only_a_deeper_symbol_has()
    {
        var (runner, _, _) = Create(Options());

        var report = await runner.RunAsync(TestContext.Current.CancellationToken);

        report.ShouldContain("| SHORTCO | 45 d |");
        var shortRow = report.Split('\n').Single(l => l.StartsWith("| SHORTCO | 45 d |", StringComparison.Ordinal));
        shortRow.ShouldContain("| 100.00 |");
        var deepRow = report.Split('\n').Single(l => l.StartsWith("| DEEPCO | 3 mo |", StringComparison.Ordinal));
        deepRow.ShouldContain("| 100.00 |");
    }

    [Fact]
    public async Task Unresolvable_symbols_are_reported_missing_and_the_report_still_has_every_section()
    {
        var (runner, _, _) = Create(Options());

        var report = await runner.RunAsync(TestContext.Current.CancellationToken);

        report.ShouldContain("| GHOST | Equity | Missing |");
        report.ShouldContain("## 8. Error shapes and range probes");
        report.ShouldContain("profile with an invalid token");
    }

    [Fact]
    public async Task An_offline_rerun_on_a_later_day_makes_no_calls_and_reproduces_the_same_report()
    {
        var (live, _, _) = Create(Options());
        var liveReport = await live.RunAsync(TestContext.Current.CancellationToken);
        var offlineApi = NewApi();
        var (offline, _, _) = Create(Options(offline: true, today: new DateOnly(2026, 11, 20)), offlineApi);

        var offlineReport = await offline.RunAsync(TestContext.Current.CancellationToken);

        offlineApi.Log.ShouldBeEmpty();
        Body(offlineReport).ShouldBe(Body(liveReport));
    }

    [Fact]
    public async Task A_failed_daily_fetch_does_not_lose_the_report()
    {
        var api = NewApi();
        api.Daily = _ => FakeApi.Fail("DH-907", "No data");
        var (runner, _, _) = Create(Options(), api);

        var report = await runner.RunAsync(TestContext.Current.CancellationToken);

        report.ShouldContain("| DEEPCO | 3 mo |");
        report.ShouldContain("DH-907");
    }

    [Fact]
    public async Task Nothing_written_to_the_output_folder_contains_a_secret()
    {
        var (runner, _, _) = Create(Options());
        await runner.RunAsync(TestContext.Current.CancellationToken);

        foreach (var file in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories))
        {
            var text = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);
            foreach (var secret in Secrets)
            {
                text.ShouldNotContain(secret, customMessage: file);
            }

            text.ShouldNotContain("eyJhbGci", customMessage: file);
        }
    }

    [Fact]
    public async Task A_renewal_that_returns_no_token_stops_the_run_instead_of_continuing_with_a_dead_token()
    {
        var api = NewApi();
        api.Renew = () => FakeApi.Ok("{\"status\":\"ok\"}");
        var (runner, _, _) = Create(Options(), api);

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => runner.RunAsync(TestContext.Current.CancellationToken));

        ex.Message.ShouldContain("login");
    }

    [Fact]
    public void The_shipped_symbol_list_has_twenty_symbols_including_two_indices_and_three_deep_ones()
    {
        var plan = SymbolPlan.Load(Path.Combine(AppContext.BaseDirectory, "symbols.json"));

        plan.Symbols.Count.ShouldBe(20);
        plan.Symbols.Count(s => s.Kind == SymbolKind.Index).ShouldBe(2);
        plan.Deep.Count.ShouldBe(3);
        plan.Deep.ShouldAllBe(d => plan.Symbols.Any(s => s.Name == d));
    }

    private static string Body(string report) => string.Join('\n', report.Split('\n').Where(l => !l.StartsWith("Generated ", StringComparison.Ordinal)));
}
