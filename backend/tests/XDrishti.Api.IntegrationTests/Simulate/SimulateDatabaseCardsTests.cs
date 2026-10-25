using System.Buffers;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Csv;
using XDrishti.SimExport.Json;
using XDrishti.SimExport.Manifest;
using XDrishti.SimExport.Text;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>
/// P3-04c against a real TimescaleDB (criteria 3, 8 and 9 of the spec): the three cards of the end-to-end table carry the trades of the record, the chart of the split instrument is adjusted once and lies in the prices of the trade, the files
/// other than the cards are those of the library for the same run, no table changes, and a database that changes between the two passes is refused with nothing written.
/// </summary>
public sealed partial class SimulateDatabaseTests
{
    private static byte[] Render(Action<IBufferWriter<byte>> write)
    {
        var sink = new ArrayBufferWriter<byte>();
        write(sink);
        return sink.WrittenSpan.ToArray();
    }

    /// <summary>A bar store that answers the first read of an instrument as the database does and every later read of it with one more bar: what a backfill between the two passes would do.</summary>
    private sealed class DriftingStore(IBarStore inner) : IBarStore
    {
        private readonly HashSet<Guid> _read = [];

        public ValueTask<IBarMergeSession> BeginMergeAsync(Guid instrumentId, BarSource source, CancellationToken cancellationToken) => inner.BeginMergeAsync(instrumentId, source, cancellationToken);

        public async ValueTask<IReadOnlyList<Bar>> ReadAsync(Guid instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
        {
            var bars = await inner.ReadAsync(instrumentId, from, to, cancellationToken);
            return _read.Add(instrumentId) ? bars : [.. bars, new Bar(Day.Epoch(D3, 15 * 60 + 29) + 60, 1, 1, 1, 1, 1)];
        }
    }

    [Fact]
    public async Task With_cards_3_over_the_basket_of_the_end_to_end_table_each_card_carries_the_trade_of_the_record_the_split_chart_is_adjusted_once_and_the_other_files_are_those_of_the_library_and_no_table_changes()
    {
        var a = await InstrumentAsync("ABB", A());
        var b = await InstrumentAsync("TCS", B());
        var c = await InstrumentAsync("SPLITCO", ReplayDays.Scaled(A(), 2.0, 1), tickPaise: null);
        await AddSplitAsync(c, D4);
        var basket = await CreateBasketAsync("Lab", a, b, c);
        var before = await MarketRowCountsAsync();
        using var temp = new TempFolder();

        var outcome = await VerbAsync("--strategy ORB --basket Lab " + Window + " --fills no-slippage --cards 3", temp);

        outcome.Exit.ShouldBe(0, outcome.Error);
        (await MarketRowCountsAsync()).ShouldBe(before, "the second read writes nothing to the database");
        var exact = new FillRulesInput(0.1, 0, 0, 0);
        var run = await HandlerRunAsync(null, basket, exact);
        var folder = Path.Combine(temp.Root, "sim", RunId.Of("ORB", D1, D3, run.RunKey));
        var cards = Directory.EnumerateFiles(Path.Combine(folder, "cards")).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        cards.Count.ShouldBe(3);
        run.Trades.Count.ShouldBe(3);
        foreach (var (trade, number) in run.Trades.Select((t, i) => (t.Trade, i + 1)))
        {
            var name = XDrishti.SimExport.Cards.CardNames.Of(number, run.Trades.Count, trade);
            var html = File.ReadAllText(Path.Combine(folder, name));
            html.ShouldContain("<h1>" + trade.Symbol + " BUY " + trade.TradeDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + ", " + XDrishti.SimExport.ExportSpellings.Of(trade.ExitReason) + ", exact result R " + (trade.RExact >= 0 ? "+" : "-") + Math.Abs(trade.RExact).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + "</h1>");
            html.ShouldContain("entry 100.5");
            html.ShouldContain("stop 98");
            html.ShouldContain("target 105.5");
            html.ShouldContain("run key: " + run.RunKey);
        }

        var splitCard = File.ReadAllText(Path.Combine(folder, XDrishti.SimExport.Cards.CardNames.Of(run.Trades.ToList().FindIndex(t => t.Trade.Symbol == "NSE:SPLITCO") + 1, 3, run.Trades.Single(t => t.Trade.Symbol == "NSE:SPLITCO").Trade)));
        splitCard.ShouldContain("<td>as stored (raw)</td><td>198</td><td>198</td><td>198</td><td>198</td>", Case.Sensitive, "the stored bar is twice the price");
        splitCard.ShouldContain("<td>as on the chart (adjusted)</td><td>99</td><td>99</td><td>99</td><td>99</td>", Case.Sensitive, "halved once, by the split of 2026-03-05");
        splitCard.ShouldContain("adjustments applied: split ex-date 2026-03-05, 1:2, factor 0.5");

        var labels = RunLabels.Of(run, FillRules.Create(FillRules.Default.EntryLimitPercent, SlippageConfig.None).Value);
        File.ReadAllBytes(Path.Combine(folder, "trades.csv")).ShouldBe(Render(s => TradesCsv.Write(s, run.RunKey, run.Trades)));
        File.ReadAllBytes(Path.Combine(folder, "run.json")).ShouldBe(Render(s => SimExportJson.Write(s, run, labels, "xd-sim-export-4")));
        File.ReadAllText(Path.Combine(folder, "README.txt")).ShouldStartWith(RunBanner.Text(labels));
    }

    [Fact]
    public async Task The_trades_entry_and_exit_prices_lie_within_the_high_and_low_of_the_chart_bars_at_their_minutes_so_the_chart_and_the_trade_are_in_the_same_prices()
    {
        var a = await InstrumentAsync("ABB", A());
        var c = await InstrumentAsync("SPLITCO", ReplayDays.Scaled(A(), 2.0, 1), tickPaise: null);
        await AddSplitAsync(c, D4);
        var run = await HandlerRunAsync([a, c], null, new FillRulesInput(0.1, 0, 0, 0));
        run.Trades.Count.ShouldBe(2);

        using var scope = Factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<BuildTradeCardsQuery, Result<IReadOnlyList<TradeCardData>>>>();
        var cards = await handler.HandleAsync(new BuildTradeCardsQuery(run, [.. run.Trades.Select(t => t.Trade.Key)]), Ct);

        cards.IsSuccess.ShouldBeTrue(cards.IsFailure ? cards.Error.ToString() : string.Empty);
        foreach (var card in cards.Value)
        {
            var trade = card.Trade.Trade;
            card.DayBars.Count.ShouldBe(375);
            var entryBar = card.DayBars.Single(bar => bar.EpochSeconds == trade.EntryEpochSeconds);
            var exitBar = card.DayBars.Single(bar => bar.EpochSeconds == trade.ExitEpochSeconds);
            trade.EntryPrice.ShouldBeInRange(entryBar.Low, entryBar.High, trade.Symbol + " entry");
            trade.ExitPrice.ShouldBeInRange(exitBar.Low, exitBar.High, trade.Symbol + " exit");
            card.OpeningBar.ShouldNotBeNull();
        }

        var split = cards.Value.Single(c2 => c2.Trade.Trade.Symbol == "NSE:SPLITCO");
        split.OpeningBar!.Value.Open.ShouldBe(99.0, "raw 198 halved once");
        split.OpeningBarRaw!.Value.Open.ShouldBe(198.0);
        cards.Value.Single(c2 => c2.Trade.Trade.Symbol == "NSE:ABB").OpeningBarRaw.ShouldBeNull();
    }

    [Fact]
    public async Task A_database_that_changes_between_the_two_passes_is_exit_1_naming_the_instrument_with_nothing_written_and_no_staging_folder()
    {
        await InstrumentAsync("ABB", A());
        using var temp = new TempFolder();

        var outcome = await VerbAsync(
            "--strategy ORB --symbols ABB " + Window + " --cards 3",
            temp,
            configure: services =>
            {
                var real = services.Last(s => s.ServiceType == typeof(IBarStore));
                services.AddScoped(real.ImplementationType!);
                services.AddScoped<IBarStore>(sp => new DriftingStore((IBarStore)sp.GetRequiredService(real.ImplementationType!)));
            });

        outcome.Exit.ShouldBe(1, outcome.Error);
        outcome.Error.ShouldContain("NSE:ABB: bars_changed: ");
        outcome.Out.ShouldBeEmpty();
        Directory.Exists(temp.Root).ShouldBeFalse("nothing was written, not even the root");
        outcome.Logs.ShouldContain(l => l.Contains("trade cards are stale for instrument", StringComparison.Ordinal) && l.Contains("NSE:ABB", StringComparison.Ordinal) && l.Contains("bars_changed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cards_0_over_the_same_data_is_the_same_run_without_cards_and_cards_default_writes_them()
    {
        await InstrumentAsync("ABB", A());
        await InstrumentAsync("TCS", B());
        using var none = new TempFolder();
        using var all = new TempFolder();

        var withoutCards = await VerbAsync("--strategy ORB --symbols ABB,TCS " + Window + " --cards 0", none);
        var withCards = await VerbAsync("--strategy ORB --symbols ABB,TCS " + Window, all);

        withoutCards.Exit.ShouldBe(0, withoutCards.Error);
        withCards.Exit.ShouldBe(0, withCards.Error);
        Directory.Exists(Path.Combine(RunFolder(none), "cards")).ShouldBeFalse();
        Directory.EnumerateFiles(Path.Combine(RunFolder(all), "cards")).Count().ShouldBe(2);
        File.ReadAllBytes(Path.Combine(RunFolder(none), "trades.csv")).ShouldBe(File.ReadAllBytes(Path.Combine(RunFolder(all), "trades.csv")));
        File.ReadAllBytes(Path.Combine(RunFolder(none), "run.json")).ShouldBe(File.ReadAllBytes(Path.Combine(RunFolder(all), "run.json")));
        withCards.Out.ShouldContain("trade cards: 2 written (up to 200 asked for), with index.html");
    }
}
