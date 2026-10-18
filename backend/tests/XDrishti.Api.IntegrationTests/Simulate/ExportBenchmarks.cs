using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Cards;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>
/// The explicit benchmark of P3-04b1 (Non-functional, E4), not part of <c>scripts/check.sh</c>; run with <c>--explicit only</c>. It replaces the size estimate of the spec by figures. First the 20 invented instruments of P3-04a's benchmark
/// (two years, 507 sessions, a random walk) are seeded in the Testcontainers database, run through the handler and exported to a temporary root through the real library and folder writer: it prints the bytes of every file, the time of each
/// phase and the memory. Then a synthetic record of 200,000 trades (the upper bound of the spec) is exported the same way, because a selective rule gives far fewer trades than the bound and the bound is what the design must hold. Reported and
/// not asserted.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ExportBenchmarks(ApiFactory factory) : DataTestBase(factory)
{
    private const int Instruments = 20;

    private const int Sessions = 507;

    private static readonly DateOnly Start = new(2024, 1, 1);

    private static IEnumerable<Bar> Walk(int instrument)
    {
        var state = (uint)((instrument * 7919) + 17);
        var price = 100.0;
        var date = Start;
        for (var session = 0; session < Sessions; session++)
        {
            for (var minute = 0; minute < SessionClock.RegularBars; minute++)
            {
                state = ((state * 1103515245u) + 12345u) & 0x7fffffffu;
                var open = price;
                var close = Math.Max(10.0, open + ((((int)(state % 5)) - 2) * 0.25));
                var high = Math.Max(open, close) + (((state >> 8) % 3) * 0.25);
                var low = Math.Min(open, close) - (((state >> 12) % 3) * 0.25);
                yield return new Bar(Day.Epoch(date, SessionClock.OpenMinute + minute), open, high, Math.Max(low, 0.25), close, 1_000);
                price = close;
            }

            date = date.DayOfWeek == DayOfWeek.Friday ? date.AddDays(3) : date.AddDays(1);
        }
    }

    /// <summary>The cards pass of P3-04c (<c>--cards 200</c>): the selection, the second read of the instruments that have a card, and the rendering of every card; prints the figures, asserts nothing.</summary>
    private static async Task CardsPassAsync(IServiceProvider provider, SimulationRun record, StringBuilder report)
    {
        var watch = Stopwatch.StartNew();
        var choices = CardSelection.Choose(record, 200);
        var select = watch.Elapsed;

        watch.Restart();
        var handler = provider.GetRequiredService<IQueryHandler<BuildTradeCardsQuery, Result<IReadOnlyList<TradeCardData>>>>();
        var cards = await handler.HandleAsync(new BuildTradeCardsQuery(record, [.. choices.Select(c => c.Trade.Trade.Key)]), Ct);
        var read = watch.Elapsed;
        cards.IsSuccess.ShouldBeTrue();

        watch.Restart();
        var labels = RunLabels.Of(record, FillRules.Default);
        long bytes = 0;
        long largest = 0;
        foreach (var card in cards.Value)
        {
            var sink = new System.Buffers.ArrayBufferWriter<byte>();
            TradeCardHtml.Write(sink, card, record, labels);
            bytes += sink.WrittenCount;
            largest = Math.Max(largest, sink.WrittenCount);
        }

        var index = new System.Buffers.ArrayBufferWriter<byte>();
        IndexHtml.Write(index, record, labels, 200, choices);
        report.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "cards pass (--cards 200): {0} cards of {1} trades from {2} instruments; select {3:F3} s, second read {4:F2} s, render {5:F2} s; {6:N0} bytes of cards (largest {7:N0}) and {8:N0} bytes of index",
            choices.Count,
            record.Trades.Count,
            choices.Select(c => c.Trade.Trade.InstrumentId).Distinct().Count(),
            select.TotalSeconds,
            read.TotalSeconds,
            watch.Elapsed.TotalSeconds,
            bytes,
            largest,
            index.WrittenCount));
    }

    [Fact(Explicit = true)]
    public async Task Export_phases_for_seeded_universe()
    {
        var ids = new List<Guid>();
        for (var i = 0; i < Instruments; i++)
        {
            var id = await SeedAsync("BENCH" + i.ToString("00", CultureInfo.InvariantCulture));
            using var scope = Factory.Services.CreateScope();
            await using var session = await scope.ServiceProvider.GetRequiredService<IBarStore>().BeginMergeAsync(id, BarSource.Dhan, Ct);
            var batch = new List<Bar>(50_000);
            foreach (var bar in Walk(i))
            {
                batch.Add(bar);
                if (batch.Count == 50_000)
                {
                    await session.AddAsync(batch.ToArray(), Ct);
                    batch.Clear();
                }
            }

            await session.AddAsync(batch.ToArray(), Ct);
            await session.CompleteAsync(null, Ct);
            ids.Add(id);
        }

        using var run = Factory.Services.CreateScope();
        var handler = run.ServiceProvider.GetRequiredService<IQueryHandler<RunSimulationQuery, Result<SimulationRun>>>();
        var clock = Stopwatch.StartNew();
        var result = await handler.HandleAsync(new RunSimulationQuery(new SimulationRequest("ORB", null, "15m", Start, new DateOnly(2025, 12, 31), ids, null)), Ct);
        var simulate = clock.Elapsed;
        result.IsSuccess.ShouldBeTrue();

        var report = new StringBuilder();
        report.AppendLine(string.Format(CultureInfo.InvariantCulture, "seeded universe: {0} instruments of {1} sessions, {2:N0} trades, {3:N0} open at end; simulation {4:F1} s", Instruments, Sessions, result.Value.Trades.Count, result.Value.OpenAtEnd.Count, simulate.TotalSeconds));
        await ExportReport.WriteAsync(result.Value, report, Ct);
        await CardsPassAsync(run.ServiceProvider, result.Value, report);
        TestContext.Current.TestOutputHelper?.WriteLine(report.ToString());
    }
}
