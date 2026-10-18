using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// The explicit benchmark of P3-04a (Non-functional), not part of <c>scripts/check.sh</c>; run with <c>--explicit only</c>. Seeds 20 invented instruments of two years (507 sessions, 190,125 minutes each, a random walk in multiples of 0.25 with no
/// flagged day) in the Testcontainers database through the real bar store, runs the handler over them, and prints the time of the read, the replay and the assembly (the phases the handler itself logs) and the total. Reported and not asserted: the
/// figure on the owner's data is first measured by P3-04b and is UNVERIFIED until then.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class RunSimulationBenchmarks(ApiFactory factory) : DataTestBase(factory)
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

    [Fact(Explicit = true)]
    public async Task Run_phases_for_seeded_universe()
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

        var last = new DateOnly(2025, 12, 31);
        var request = new SimulationRequest("ORB", null, "15m", Start, last, ids, null);
        using var run = Factory.Services.CreateScope();
        var handler = run.ServiceProvider.GetRequiredService<IQueryHandler<RunSimulationQuery, Result<SimulationRun>>>();
        var clock = Stopwatch.StartNew();
        var result = await handler.HandleAsync(new RunSimulationQuery(request), Ct);
        var total = clock.Elapsed;

        result.IsSuccess.ShouldBeTrue();
        var report = string.Format(
            CultureInfo.InvariantCulture,
            "{0} instruments of {1} sessions: {2:F1} s in all ({3:F0} ms per instrument; the read, the replay and the assembly are the phases of the log line 'Simulation run … finished'); {4} trades, {5} candidates, {6} bars read; 214 instruments at this rate: {7:F0} s",
            Instruments,
            Sessions,
            total.TotalSeconds,
            total.TotalMilliseconds / Instruments,
            result.Value.Trades.Count,
            result.Value.Totals.Fates.Total,
            result.Value.Totals.BarsRead,
            total.TotalSeconds / Instruments * 214);
        var finish = Factory.Logs.All.Last(line => line.Contains("Simulation run ", StringComparison.Ordinal) && line.Contains(" finished", StringComparison.Ordinal));
        TestContext.Current.TestOutputHelper?.WriteLine(report + "\n" + finish);
    }
}
