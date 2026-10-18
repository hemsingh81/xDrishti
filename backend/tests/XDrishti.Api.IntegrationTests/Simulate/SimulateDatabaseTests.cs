using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Market.Baskets;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Manifest;
using XDrishti.Tests.Shared;
using ResearchPort = XDrishti.Application.Abstractions.Research;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>
/// The verb against a real TimescaleDB, called as <c>SimulateCommand.RunAsync</c> over the real host with the container's connection string (P3-04b2 criteria 4, 6, 7, 8, 10, 11 and 12): the days of P3-03 are seeded through the real stores, the
/// expected values are those of P3-04a's end-to-end table, and a recording bar store proves that a refusal reads no bar.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed partial class SimulateDatabaseTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateOnly D1 = ReplayDays.D1;

    private static readonly DateOnly D2 = ReplayDays.D2;

    private static readonly DateOnly D3 = ReplayDays.D3;

    private static readonly DateOnly D4 = ReplayDays.D4;

    private const string Window = "--from 2026-03-02 --to 2026-03-04";

    [GeneratedRegex(@"^[^:\s]+: [a-z_.]+: .+$", RegexOptions.CultureInvariant)]
    private static partial Regex ProblemLine();

    private static Bar[] A() => ReplayDays.Join(ReplayDays.Flat(D1, 99.00), ReplayDays.F1(D2), ReplayDays.Flat(D3, 105.00));

    private static Bar[] B() => ReplayDays.Join(ReplayDays.F2(D1), ReplayDays.Flat(D2, 98.50), ReplayDays.Flat(D3, 98.50));

    private sealed record Outcome(int Exit, string Out, string Error, IReadOnlyCollection<string> Logs);

    /// <summary>A bar store and bar queries that record every call and answer nothing: a refusal must not reach them.</summary>
    private sealed class NoReads : IBarStore, IBarQueries
    {
        public List<string> Calls { get; } = [];

        public ValueTask<IBarMergeSession> BeginMergeAsync(Guid instrumentId, BarSource source, CancellationToken cancellationToken) => Record<IBarMergeSession>(nameof(BeginMergeAsync));

        public ValueTask<IReadOnlyList<Bar>> ReadAsync(Guid instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) => Record<IReadOnlyList<Bar>>(nameof(ReadAsync));

        public ValueTask<BarTotals> GetBarTotalsAsync(DateTimeOffset expectedStart, CancellationToken cancellationToken) => Record<BarTotals>(nameof(GetBarTotalsAsync));

        public ValueTask<AggregateTotals> GetAggregateTotalsAsync(CancellationToken cancellationToken) => Record<AggregateTotals>(nameof(GetAggregateTotalsAsync));

        public ValueTask<DataInstrumentPage> ListInstrumentsAsync(DataInstrumentSearch search, DateTimeOffset expectedStart, DateOnly expectedSession, XDrishti.Domain.Market.Calendar.TradingCalendar calendar, CancellationToken cancellationToken) =>
            Record<DataInstrumentPage>(nameof(ListInstrumentsAsync));

        public ValueTask<BarsInstrument?> FindInstrumentAsync(Guid id, CancellationToken cancellationToken) => Record<BarsInstrument?>(nameof(FindInstrumentAsync));

        public ValueTask<IReadOnlyList<BarRow>> ReadBarsAsync(Guid instrumentId, Timeframe timeframe, int take, CancellationToken cancellationToken) => Record<IReadOnlyList<BarRow>>(nameof(ReadBarsAsync));

        public ValueTask<IReadOnlyList<BarRow>> ReadDailyBarsAsync(Guid instrumentId, DateOnly from, DateOnly toExclusive, CancellationToken cancellationToken) => Record<IReadOnlyList<BarRow>>(nameof(ReadDailyBarsAsync));

        private ValueTask<T> Record<T>(string name)
        {
            Calls.Add(name);
            return ValueTask.FromResult(default(T)!);
        }
    }

    private async Task<Outcome> VerbAsync(string args, TempFolder temp, NoReads? noReads = null, Action<IServiceCollection>? configure = null, string? connectionString = null, CancellationToken? token = null, string? root = null)
    {
        var logs = new CapturedLogs();
        var overrides = new Dictionary<string, string?> { ["Database:ConnectionString"] = connectionString ?? Factory.AdminConnectionString, ["BrokerVault:Key"] = string.Empty };
        using var host = SimulateHost.Create(overrides, services =>
        {
            services.AddSingleton<ILogEventSink>(logs);
            if (noReads is not null)
            {
                services.AddSingleton<IBarStore>(noReads);
                services.AddSingleton<IBarQueries>(noReads);
            }

            configure?.Invoke(services);
        });
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = await SimulateCommand.RunAsync(args.Split(' ', StringSplitOptions.RemoveEmptyEntries), host.Services, new SimulateConsole(output, error), root ?? temp.Root, token ?? Ct);

        return new Outcome(exit, output.ToString(), error.ToString(), [.. logs.All]);
    }

    private async Task LoadAsync(Guid id, IEnumerable<Bar> bars, BarSource source = BarSource.Dhan)
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IBarStore>();
        var list = bars.ToArray();
        await using var session = await store.BeginMergeAsync(id, source, Ct);
        await session.AddAsync(list, Ct);
        await session.CompleteAsync(null, Ct);
    }

    private async Task<Guid> InstrumentAsync(string ticker, IEnumerable<Bar>? bars = null, decimal? tickPaise = 5m, string type = "share", bool retired = false, string? isin = null)
    {
        var id = await SeedAsync(ticker, type, retired);
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "UPDATE market.instruments SET tick_size = @tick, isin = @isin WHERE id = @id", ("tick", tickPaise is null ? DBNull.Value : tickPaise.Value), ("isin", isin is null ? DBNull.Value : isin), ("id", id));
        }

        if (bars is not null)
        {
            await LoadAsync(id, bars);
        }

        return id;
    }

    private async Task AddSplitAsync(Guid id, DateOnly exDate)
    {
        using var response = await Client.PostJsonAsync("/api/data/corporate-actions", new { instrumentId = id, exDate = exDate.ToString("yyyy-MM-dd"), kind = "split", numerator = 1, denominator = 2 }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
    }

    private async Task<Guid> CreateBasketAsync(string name, params Guid[] members)
    {
        using var created = await Client.PostJsonAsync("/api/baskets", new { name }, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var basket = (await created.Content.ReadFromJsonAsync<BasketView>(Ct))!.Id;
        if (members.Length > 0)
        {
            using var added = await Client.PostJsonAsync($"/api/baskets/{basket}/members", new { instrumentIds = members }, Ct);
            added.StatusCode.ShouldBe(HttpStatusCode.OK, await added.Content.ReadAsStringAsync(Ct));
        }

        return basket;
    }

    private async Task<SimulationRun> HandlerRunAsync(Guid[]? ids, Guid? basket, FillRulesInput fills)
    {
        using var scope = Factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<RunSimulationQuery, Result<SimulationRun>>>();
        var result = await handler.HandleAsync(new RunSimulationQuery(new SimulationRequest("ORB", null, "15m", D1, D3, ids, basket, false, null, fills)), Ct);
        return result.Value;
    }

    private static List<Dictionary<string, string>> Rows(string folder, string file = "trades.csv")
    {
        var lines = File.ReadAllLines(Path.Combine(folder, file));
        var header = lines[0].Split(',');
        return [.. lines.Skip(1).Where(l => l.Length > 0).Select(l => l.Split(',')).Select(cells => header.Zip(cells).ToDictionary(p => p.First, p => p.Second))];
    }

    private static string RunFolder(TempFolder temp) => Directory.EnumerateDirectories(Path.Combine(temp.Root, "sim")).Single(d => !Path.GetFileName(d).StartsWith('.'));

    private async Task<Dictionary<string, long>> MarketRowCountsAsync()
    {
        await using var connection = await OpenAsync();
        var tables = new List<string>();
        await using (var list = new Npgsql.NpgsqlCommand("SELECT table_name FROM information_schema.tables WHERE table_schema = 'market' AND table_type = 'BASE TABLE' ORDER BY 1", connection))
        await using (var reader = await list.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                tables.Add(reader.GetString(0));
            }
        }

        var counts = new Dictionary<string, long>();
        foreach (var table in tables)
        {
#pragma warning disable CA2100 // the names come from the catalog query above
            await using var count = new Npgsql.NpgsqlCommand("SELECT count(*) FROM market.\"" + table + "\"", connection);
#pragma warning restore CA2100
            counts[table] = (long)(await count.ExecuteScalarAsync(Ct))!;
        }

        return counts;
    }

    // criterion 12: the end to end, the files, the tables, the empty vault key

    [Fact]
    public async Task By_symbols_and_by_basket_the_verb_writes_the_trades_of_the_end_to_end_table_changes_no_table_and_needs_no_vault_key_or_owner_secret()
    {
        var a = await InstrumentAsync("ABB", A());
        var b = await InstrumentAsync("TCS", B());
        var c = await InstrumentAsync("SPLITCO", ReplayDays.Scaled(A(), 2.0, 1), tickPaise: null);
        await AddSplitAsync(c, D4);
        var basket = await CreateBasketAsync("Lab", a, b, c);
        var before = await MarketRowCountsAsync();
        using var temp = new TempFolder();

        var bySymbols = await VerbAsync("--strategy ORB --symbols ABB,TCS,SPLITCO " + Window + " --fills no-slippage", temp);
        var byBasket = await VerbAsync("--strategy ORB --basket Lab " + Window + " --fills no-slippage", temp);

        bySymbols.Exit.ShouldBe(0, bySymbols.Error);
        byBasket.Exit.ShouldBe(0, byBasket.Error);
        (await MarketRowCountsAsync()).ShouldBe(before, "the verb writes nothing to the database");
        var exact = new FillRulesInput(0.1, 0, 0, 0);
        var symbolsRun = await HandlerRunAsync([a, b, c], null, exact);
        var basketRun = await HandlerRunAsync(null, basket, exact);
        var folders = Directory.EnumerateDirectories(Path.Combine(temp.Root, "sim")).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        folders.ShouldBe([.. new[] { RunId.Of("ORB", D1, D3, symbolsRun.RunKey), RunId.Of("ORB", D1, D3, basketRun.RunKey) }.Order(StringComparer.Ordinal)]);
        symbolsRun.RunKey.ShouldNotBe(basketRun.RunKey, "the universe label is part of the key");

        var symbolsFolder = Path.Combine(temp.Root, "sim", RunId.Of("ORB", D1, D3, symbolsRun.RunKey));
        var rows = Rows(symbolsFolder);
        rows.Count.ShouldBe(3);
        var abb = rows.Single(r => r["symbol"] == "NSE:ABB");
        (abb["side"], abb["trade_date"], abb["entry_price"], abb["stop_price"], abb["target_price"], abb["exit_price"], abb["exit_reason"], abb["r_exact"], abb["fills"]).ShouldBe(("BUY", "2026-03-03", "100.5", "98", "105.5", "105.5", "target", "2", "no_slippage"));
        var tcs = rows.Single(r => r["symbol"] == "NSE:TCS");
        (tcs["side"], tcs["trade_date"], tcs["entry_price"], tcs["exit_price"], tcs["exit_reason"], tcs["r_exact"]).ShouldBe(("BUY", "2026-03-02", "100.5", "98", "stop", "-1"));
        var split = rows.Single(r => r["symbol"] == "NSE:SPLITCO");
        (split["entry_price"], split["stop_price"], split["target_price"], split["exit_price"], split["r_exact"]).ShouldBe(("100.5", "98", "105.5", "105.5", "2"));
        Rows(symbolsFolder).ShouldAllBe(r => r["basket_ids"].Length == 0);
        Rows(Path.Combine(temp.Root, "sim", RunId.Of("ORB", D1, D3, basketRun.RunKey))).ShouldAllBe(r => r["basket_ids"] == basket.ToString());

        using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Path.Combine(temp.Root, "sim", RunId.Of("ORB", D1, D3, basketRun.RunKey)), "run.json")));
        json.RootElement.GetProperty("run").GetProperty("universe").GetProperty("label").GetString().ShouldBe("members as of today");
        byBasket.Out.ShouldContain("universe: members as of today");
        byBasket.Out.ShouldContain("database: " + new Uri("tcp://" + HostOf(Factory.AdminConnectionString)).Host);
        ExportManifest.ParseBytes(File.ReadAllBytes(Path.Combine(Path.Combine(temp.Root, "sim", RunId.Of("ORB", D1, D3, basketRun.RunKey)), "manifest.json"))).Value.BasketId.ShouldBe(basket);
        Directory.EnumerateFiles(symbolsFolder).Select(Path.GetFileName).Order(StringComparer.Ordinal).ShouldBe(["README.txt", "manifest.json", "open_at_end.csv", "run.json", "trades.csv"]);
    }

    private static string HostOf(string connectionString) =>
        new System.Data.Common.DbConnectionStringBuilder { ConnectionString = connectionString }["Host"] + ":" + new System.Data.Common.DbConnectionStringBuilder { ConnectionString = connectionString }["Port"];

    [Fact]
    public async Task The_default_modelled_run_is_the_same_run_as_the_handler_gives_with_the_default_fills_and_a_second_identical_run_is_unchanged()
    {
        var a = await InstrumentAsync("ABB", A());
        var b = await InstrumentAsync("TCS", B());
        using var temp = new TempFolder();

        var first = await VerbAsync("--strategy ORB --symbols ABB,TCS " + Window, temp);
        var second = await VerbAsync("--strategy ORB --symbols ABB,TCS " + Window, temp);

        first.Exit.ShouldBe(0, first.Error);
        first.Out.ShouldContain("export: written");
        second.Exit.ShouldBe(0, second.Error);
        second.Out.ShouldContain("export: unchanged");
        var run = await HandlerRunAsync([a, b], null, new FillRulesInput(0.1, 1, 0.05, 2));
        Path.GetFileName(RunFolder(temp)).ShouldBe(RunId.Of("ORB", D1, D3, run.RunKey));
        Rows(RunFolder(temp)).ShouldAllBe(r => r["fills"] == "modelled");
        second.Logs.ShouldContain(l => l.Contains("already there and is unchanged", StringComparison.Ordinal));
    }

    // criterion 4: the refusals read no bar

    [Fact]
    public async Task A_request_the_engine_refuses_is_exit_1_with_one_line_per_problem_nothing_written_and_no_bar_read()
    {
        var a = await InstrumentAsync("ABB", A());
        using var temp = new TempFolder();
        var cases = new[]
        {
            ("--strategy MACD --symbols ABB " + Window, "strategy_id: simulation.unknown_strategy: "),
            ("--strategy ORB --symbols ABB " + Window + " --param bogus=1", "parameters.bogus: simulation.unknown_parameter: "),
            ("--strategy ORB --symbols ABB " + Window + " --param or_min=20", ": "),
            ("--strategy ORB --symbols ABB --from 2026-03-04 --to 2026-03-02", "from: "),
            ("--strategy ORB --symbols ABB --from 1999-01-01 --to 2026-03-02", "from: "),
        };

        foreach (var (args, expected) in cases)
        {
            var reads = new NoReads();

            var outcome = await VerbAsync(args, temp, reads);

            outcome.Exit.ShouldBe(1, args + "\n" + outcome.Error);
            var lines = outcome.Error.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            lines.Length.ShouldBeInRange(1, 20);
            lines.ShouldAllBe(l => ProblemLine().IsMatch(l), outcome.Error);
            outcome.Error.ShouldContain(expected);
            reads.Calls.ShouldBeEmpty(args);
            Directory.Exists(temp.Root).ShouldBeFalse(args);
        }

        a.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task A_symbol_that_matches_nothing_an_ambiguous_isin_and_a_basket_name_that_matches_nothing_are_exit_1_and_list_every_unmatched_entry()
    {
        await InstrumentAsync("ABB", A());
        await InstrumentAsync("TWOA", null, isin: "INE000T00012");
        await InstrumentAsync("TWOB", null, isin: "INE000T00012");
        await CreateBasketAsync("Banks");
        using var temp = new TempFolder();
        var reads = new NoReads();

        var symbols = await VerbAsync("--strategy ORB --symbols ABB,NOPE,INE000T00012,nse:ZZZ " + Window, temp, reads);
        var basket = await VerbAsync("--strategy ORB --basket Missing " + Window, temp, reads);

        symbols.Exit.ShouldBe(1);
        symbols.Error.Split('\n', StringSplitOptions.RemoveEmptyEntries).ShouldBe(["symbol 'NOPE': not_found", "symbol 'INE000T00012': ambiguous", "symbol 'nse:ZZZ': not_found"]);
        basket.Exit.ShouldBe(1);
        basket.Error.ShouldContain("matches no active manual basket");
        basket.Error.ShouldContain("Banks");
        reads.Calls.ShouldBeEmpty();
        Directory.Exists(temp.Root).ShouldBeFalse();
    }

    [Fact]
    public async Task A_portfolio_basket_id_is_refused_by_the_handler_as_not_found_and_an_archived_or_unknown_id_too()
    {
        var id = await InstrumentAsync("ABB", A());
        var archived = await CreateBasketAsync("Old", id);
        using (var archive = await Client.DeleteAsync(new Uri($"/api/baskets/{archived}", UriKind.Relative), Ct))
        {
            archive.StatusCode.ShouldBe(HttpStatusCode.NoContent, await archive.Content.ReadAsStringAsync(Ct));
        }

        var elevated = (await Factory.SignInElevatedAsync(Ct)).Client;
        await elevated.CreateAccountAsync(AccountsApi.NewAccount("Dhan - Main", "1000000001", roles: new { portfolio = new { enabled = true } }), Ct);
        using var buckets = await elevated.GetAsync("/api/portfolio-buckets", Ct);
        var portfolio = (await buckets.ReadAsync<List<XDrishti.Application.Accounts.Buckets.BucketView>>(Ct)).Single().Id;
        using var temp = new TempFolder();

        foreach (var unreachable in new[] { portfolio, archived, Guid.CreateVersion7() })
        {
            var reads = new NoReads();

            var outcome = await VerbAsync("--strategy ORB --basket " + unreachable + " " + Window, temp, reads);

            outcome.Exit.ShouldBe(1, outcome.Error);
            outcome.Error.ShouldContain("simulation.basket_not_found: ");
            reads.Calls.ShouldBeEmpty();
            Directory.Exists(temp.Root).ShouldBeFalse();
        }
    }

    // criterion 8: the tick gate with the real stores

    [Fact]
    public async Task Shares_with_ticks_of_one_five_and_ten_paise_run_and_a_share_stored_as_rupees_stops_the_verb_before_any_bar_is_read_by_symbols_and_by_basket()
    {
        var ok5 = await InstrumentAsync("ABB", A(), 5m);
        var etf = await InstrumentAsync("GOLDX", A(), 1m, "etf");
        var ok10 = await InstrumentAsync("SHARE10", A(), 10m);
        var index = await InstrumentAsync("NIFTYX", null, 0.05m, "index");
        var future = await InstrumentAsync("FUTX", null, 0.25m, "future");
        var missing = await InstrumentAsync("NOTICK", A(), null);
        var big = await InstrumentAsync("BIGTICK", A(), 150m);
        var suspect = await InstrumentAsync("RUPEES", A(), 0.05m);
        var good = await CreateBasketAsync("Good", ok5, etf, ok10, index, future, missing, big);
        var bad = await CreateBasketAsync("Bad", ok5, suspect, index);
        using var temp = new TempFolder();

        var runs = await VerbAsync("--strategy ORB --symbols ABB,GOLDX,SHARE10,NIFTYX,FUTX,NOTICK,BIGTICK " + Window, temp);
        var basketRuns = await VerbAsync("--strategy ORB --basket Good " + Window + " --force", temp);
        var reads = new NoReads();
        var bySymbols = await VerbAsync("--strategy ORB --symbols ABB,RUPEES " + Window, temp, reads);
        var byBasket = await VerbAsync("--strategy ORB --basket Bad " + Window, temp, reads);

        runs.Exit.ShouldBe(0, runs.Error);
        runs.Out.ShouldContain("not tick-checked (not a share or an ETF): future 1, index 1");
        runs.Out.ShouldContain("  0.01: 1 instruments, for example NSE:GOLDX");
        runs.Out.ShouldContain("  0.05: ");
        runs.Out.ShouldContain("  0.1: 1 instruments, for example NSE:SHARE10");
        runs.Out.ShouldContain("NSE default (no tick in the master): 1 instruments");
        runs.Out.ShouldContain("skipped by reason: no_bars 2, tick_size_invalid 1");
        basketRuns.Exit.ShouldBe(0, basketRuns.Error);
        foreach (var refused in new[] { bySymbols, byBasket })
        {
            refused.Exit.ShouldBe(1);
            refused.Error.ShouldContain("NSE:RUPEES: stored 0.05, converted 0.0005 rupee");
            refused.Error.ShouldContain("ADR 0038 item 6");
            refused.Error.ShouldNotContain("NIFTYX");
        }

        reads.Calls.ShouldBeEmpty("the gate stops the run before any bar is read");
        good.ShouldNotBe(bad);
    }

    // criterion 6: the symbols

    [Fact]
    public async Task Symbols_resolve_in_the_order_given_to_canonical_symbols_duplicates_collapse_a_retired_instrument_runs_marked_and_an_isin_resolves_to_the_listed_row()
    {
        var reliance = await InstrumentAsync("RELIANCE", A());
        var mm = await InstrumentAsync("M&M", B());
        var old = await InstrumentAsync("OLDCO", A(), retired: true);
        var heldListed = await InstrumentAsync("LISTEDCO", A(), isin: "INE000L00019");
        var heldRetired = await InstrumentAsync("GONECO", null, retired: true, isin: "INE000L00019");
        using var temp = new TempFolder();

        var outcome = await VerbAsync("--strategy ORB --symbols M&M,x,reliance " + Window, temp);
        var clean = await VerbAsync("--strategy ORB --symbols M&M,reliance,NSE:RELIANCE,OLDCO,INE000L00019 " + Window, temp);

        outcome.Exit.ShouldBe(1, "the unmatched entry x is listed");
        outcome.Error.ShouldContain("symbol 'x': not_found");
        clean.Exit.ShouldBe(0, clean.Error);
        using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(RunFolder(temp), "run.json")));
        json.RootElement.GetProperty("run").GetProperty("universe").GetProperty("members").EnumerateArray().Select(m => m.GetProperty("symbol").GetString())
            .ShouldBe(["NSE:M&M", "NSE:RELIANCE", "NSE:OLDCO", "NSE:LISTEDCO"], "the order given, canonical symbols, the listed row of the ISIN");
        clean.Out.ShouldContain("duplicates collapsed: 1");
        clean.Out.ShouldContain("retired instruments run: NSE:OLDCO");
        reliance.ShouldNotBe(mm);
        old.ShouldNotBe(heldListed);
        heldRetired.ShouldNotBe(heldListed);
    }

    // criterion 7: baskets by name, by case and by id

    [Fact]
    public async Task A_manual_basket_runs_by_name_ignoring_case_and_by_its_id_and_every_trade_carries_the_basket_id()
    {
        var a = await InstrumentAsync("ABB", A());
        var b = await InstrumentAsync("TCS", B());
        var basket = await CreateBasketAsync("Banks", a, b);
        using var temp = new TempFolder();

        var byName = await VerbAsync("--strategy ORB --basket bAnKs " + Window + " --fills no-slippage", temp);
        var byId = await VerbAsync("--strategy ORB --basket " + basket + " " + Window + " --fills no-slippage", temp);

        byName.Exit.ShouldBe(0, byName.Error);
        byId.Exit.ShouldBe(0, byId.Error);
        byId.Out.ShouldContain("export: unchanged", Case.Sensitive, "the same basket by id is the identical export");
        Rows(RunFolder(temp)).ShouldAllBe(r => r["basket_ids"] == basket.ToString());
    }

    // criterion 4 and 10: failure, cancellation and the overwrite through the verb

    [Fact]
    public async Task A_database_that_cannot_be_reached_is_exit_3_with_the_type_and_no_stack_and_no_folder()
    {
        using var temp = new TempFolder();

        var outcome = await VerbAsync("--strategy ORB --symbols ABB " + Window, temp, connectionString: "Host=127.0.0.1;Port=1;Database=none;Username=nobody;Timeout=2;Pooling=false");

        outcome.Exit.ShouldBe(3);
        outcome.Error.ShouldStartWith("failed: ");
        outcome.Error.ShouldNotContain("   at ");
        outcome.Error.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length.ShouldBe(1);
        Directory.Exists(temp.Root).ShouldBeFalse();
        outcome.Logs.ShouldContain(l => l.StartsWith("Error Simulate verb failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_token_cancelled_during_the_replay_is_exit_130_and_leaves_no_folder_and_no_staging()
    {
        await InstrumentAsync("ABB", A());
        await InstrumentAsync("TCS", B());
        using var temp = new TempFolder();
        using var cancel = new CancellationTokenSource();

        var outcome = await VerbAsync(
            "--strategy ORB --symbols ABB,TCS " + Window,
            temp,
            configure: services =>
            {
                var engine = services.Single(d => d.ServiceType == typeof(ResearchPort.ISimulationEngine));
                services.Remove(engine);
                services.AddSingleton<ResearchPort.ISimulationEngine>(sp => new CancellingEngine((ResearchPort.ISimulationEngine)ActivatorUtilities.CreateInstance(sp, engine.ImplementationType!), cancel));
            },
            token: cancel.Token);

        outcome.Exit.ShouldBe(130);
        outcome.Error.ShouldNotContain("   at ");
        Directory.Exists(Path.Combine(temp.Root, "sim")).ShouldBeFalse("the writer was never reached");
    }

    [Fact]
    public async Task A_different_export_in_the_same_folder_is_refused_without_force_and_a_forced_replacement_keeps_one_previous_copy()
    {
        await InstrumentAsync("ABB", A());
        using var temp = new TempFolder();
        (await VerbAsync("--strategy ORB --symbols ABB " + Window, temp)).Exit.ShouldBe(0);
        File.AppendAllText(Path.Combine(RunFolder(temp), "run.json"), " ");

        var refused = await VerbAsync("--strategy ORB --symbols ABB " + Window, temp);
        var forced = await VerbAsync("--strategy ORB --symbols ABB " + Window + " --force", temp);

        refused.Exit.ShouldBe(1);
        refused.Error.ShouldContain("different or modified export");
        forced.Exit.ShouldBe(0, forced.Error);
        forced.Out.ShouldContain("export: replaced");
        Directory.EnumerateDirectories(Path.Combine(temp.Root, "sim")).Select(Path.GetFileName).Order(StringComparer.Ordinal).Count(n => n!.EndsWith(".previous", StringComparison.Ordinal)).ShouldBe(1);
    }

    // criterion 11: logging and secrets

    [Fact]
    public async Task The_database_password_is_in_no_output_log_line_or_file_and_the_handlers_lines_are_logged_with_the_verbs_own()
    {
        await InstrumentAsync("ABB", A());
        using var temp = new TempFolder();
        const string password = "integration-tests";

        var success = await VerbAsync("--strategy ORB --symbols ABB " + Window, temp);
        var refusal = await VerbAsync("--strategy MACD --symbols ABB " + Window, temp);
        var failure = await VerbAsync("--strategy ORB --symbols ABB " + Window, temp, connectionString: "Host=127.0.0.1;Port=1;Database=none;Username=nobody;Password=" + password + ";Timeout=2;Pooling=false");

        success.Exit.ShouldBe(0);
        refusal.Exit.ShouldBe(1);
        failure.Exit.ShouldBe(3);
        failure.Logs.ShouldContain(l => l.Contains("Simulate verb failed", StringComparison.Ordinal));
        foreach (var text in new[] { failure.Out, failure.Error }.Concat(failure.Logs))
        {
            text.ShouldNotContain("   at ", Case.Sensitive);
        }

        foreach (var text in new[] { success.Out, success.Error, refusal.Out, refusal.Error, failure.Out, failure.Error }.Concat(success.Logs).Concat(refusal.Logs).Concat(failure.Logs))
        {
            text.ShouldNotContain(password);
            text.ShouldNotContain(Factory.VaultKey);
            text.ShouldNotContain(Factory.OwnerPassword);
        }

        foreach (var file in Directory.EnumerateFiles(temp.Root, "*", SearchOption.AllDirectories))
        {
            var bytes = File.ReadAllBytes(file);
            bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(password)).ShouldBe(-1, file);
            bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(Factory.VaultKey)).ShouldBe(-1, file);
            bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(Factory.OwnerPassword)).ShouldBe(-1, file);
        }

        success.Logs.ShouldContain(l => l.Contains("Simulation run started", StringComparison.Ordinal));
        success.Logs.ShouldContain(l => l.Contains("Simulate verb started", StringComparison.Ordinal));
        success.Logs.ShouldContain(l => l.Contains("Simulation run ", StringComparison.Ordinal) && l.Contains("finished", StringComparison.Ordinal));
        success.Logs.ShouldContain(l => l.Contains("wrote export", StringComparison.Ordinal) && l.Contains("orb-20260302-20260304-", StringComparison.Ordinal));
        ForbiddenWords.In(success.Out.Replace(temp.Path, "<root>", StringComparison.Ordinal)).ShouldBeEmpty();
        ForbiddenWords.In(string.Join("\n", success.Logs).Replace(temp.Path, "<root>", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    private sealed class CancellingEngine(ResearchPort.ISimulationEngine inner, CancellationTokenSource cancel) : ResearchPort.ISimulationEngine
    {
        public string EngineVersion => inner.EngineVersion;

        public IReadOnlyList<BuiltInStrategyInfo> BuiltIns => inner.BuiltIns;

        public Result<ResearchPort.IPreparedStrategy> Prepare(string strategyId, IReadOnlyDictionary<string, double> parameters, Timeframe evaluationTimeframe, FillRules? fills) =>
            inner.Prepare(strategyId, parameters, evaluationTimeframe, fills);

        public InstrumentReplay Replay(ResearchPort.IPreparedStrategy strategy, InstrumentReplayInput input)
        {
            var replay = inner.Replay(strategy, input);
            cancel.Cancel();
            return replay;
        }
    }
}
