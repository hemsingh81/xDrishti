using System.Collections.Concurrent;

namespace XDrishti.Indicators.Tests.Context;

/// <summary>
/// A live run appends today's close to the context while frames exist (ADR 0033): <c>Append</c> is single-writer, but a reader on another thread must never see a day that is not complete (review of P3-01c, M6).
/// A reader that saw the count of days before the day was written would get an all-zero record — a valid-looking range regime — instead of "not ready".
/// <para>
/// The test does not rely on the scheduler to overlap the two threads: the reader is started and signals that it is running before the first <c>Append</c>, and after every <see cref="Burst"/> appends the writer waits
/// until the reader has read at least one more non-empty context (no sleeps; the wait is a spin on a counter). So the reader is alive and reading all through the run, and the number of reads that saw a day — the only
/// reads that can catch anything — is asserted, not hoped for.
/// </para>
/// </summary>
public sealed class ContextAppendConcurrencyTests
{
    private const int Days = 60_000;

    /// <summary>Appends between two checks that the reader is still reading.</summary>
    private const int Burst = 1_000;

    private static readonly DateOnly Start = new(1990, 1, 1);

    private static readonly ContextOptions Small = new(TrendSmaPeriod: 6, TrendSlopeDays: 2, AdxPeriod: 3, AdxMin: 15, AtrPeriod: 3, PercentileWindow: 10);

    /// <summary>The breadth every day carries, a function of its date: 1 to 97, never the 0 of a slot that was not written.</summary>
    private static double BreadthOf(DateOnly date) => 1 + (date.DayNumber - Start.DayNumber) % 97;

    private static ContextDay DayAt(int i)
    {
        var date = Start.AddDays(i);
        var close = 1_000 + (i % 41) * 3.0;
        return new ContextDay(date, close + 5, close - 5, close, 12 + i % 9, BreadthOf(date));
    }

    [Fact]
    public async Task A_reader_never_sees_a_day_that_is_not_complete_while_another_thread_appends()
    {
        var context = MarketContext.Create([], Small).Value;
        var failures = new ConcurrentQueue<string>();
        var writing = true;
        var readsOfDays = 0L;
        using var running = new ManualResetEventSlim();

        var reader = Task.Factory.StartNew(() =>
        {
            var cursor = 0;
            var counted = 0L;
            running.Set();
            while (Volatile.Read(ref writing))
            {
                var days = context.Count;
                Check(context.TryAsOf(DateOnly.MaxValue, out var latest), latest, days, "binary search");
                Check(context.TryAsOf(DateOnly.MaxValue, ref cursor, out var walked), walked, days, "cursor");
                if (days > 0)
                {
                    counted++;
                    Volatile.Write(ref readsOfDays, counted);
                }
            }

            void Check(bool found, ContextValues values, int daysCounted, string how)
            {
                if (daysCounted == 0)
                {
                    return;
                }

                if (!found)
                {
                    failures.Enqueue($"{how}: {daysCounted} days were counted and none was found");
                }
                else if (values.Date < Start || values.Breadth != BreadthOf(values.Date))
                {
                    failures.Enqueue($"{how}: a half-written day, date {values.Date:yyyy-MM-dd}, breadth {values.Breadth}");
                }
                else if (values.Date.DayNumber - Start.DayNumber + 1 < daysCounted)
                {
                    failures.Enqueue($"{how}: {daysCounted} days were counted before the read but the latest found is day {values.Date.DayNumber - Start.DayNumber + 1}");
                }
            }
        }, TestContext.Current.CancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        running.Wait(TestContext.Current.CancellationToken);

        var seen = 0L;
        for (var i = 0; i < Days; i++)
        {
            context.Append(DayAt(i)).IsSuccess.ShouldBeTrue();
            if (i % Burst == Burst - 1)
            {
                var before = seen;
                SpinWait.SpinUntil(() => Volatile.Read(ref readsOfDays) > before || reader.IsCompleted);
                seen = Volatile.Read(ref readsOfDays);
            }
        }

        Volatile.Write(ref writing, false);
        await reader;

        failures.ShouldBeEmpty();
        context.Count.ShouldBe(Days);
        Volatile.Read(ref readsOfDays).ShouldBeGreaterThanOrEqualTo(Days / Burst, "reads that saw at least one day while the writer was appending");
        context.TryAsOf(DateOnly.MaxValue, out var last).ShouldBeTrue();
        last.Date.ShouldBe(Start.AddDays(Days - 1));
    }
}
