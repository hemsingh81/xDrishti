using XDrishti.Application.Market.Calendar;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Market.Calendar;

/// <summary>The scoped snapshot other slices read: one read per request or job until something invalidates it.</summary>
public sealed class TradingCalendarProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_days_are_read_once_and_only_the_confirmed_ones_count()
    {
        var data = new CountingStore();
        data.Days.Add(TradingDay.Holiday(new DateOnly(2026, 10, 14), CalendarSource.Manual, CalendarStatus.Confirmed, null, Now));
        data.Days.Add(TradingDay.Holiday(new DateOnly(2026, 10, 15), CalendarSource.Observed, CalendarStatus.Proposed, null, Now));
        var provider = new TradingCalendarProvider(data);

        var first = await provider.GetAsync(Ct);
        var second = await provider.GetAsync(Ct);

        second.ShouldBeSameAs(first);
        data.Reads.ShouldBe(1);
        first.IsTradingDay(new DateOnly(2026, 10, 14)).ShouldBeFalse();
        first.IsTradingDay(new DateOnly(2026, 10, 15)).ShouldBeTrue();
    }

    [Fact]
    public async Task After_an_invalidation_the_next_read_sees_the_change()
    {
        var data = new CountingStore();
        var provider = new TradingCalendarProvider(data);
        (await provider.GetAsync(Ct)).IsTradingDay(new DateOnly(2026, 10, 14)).ShouldBeTrue();
        data.Days.Add(TradingDay.Holiday(new DateOnly(2026, 10, 14), CalendarSource.Manual, CalendarStatus.Confirmed, null, Now));

        provider.Invalidate();

        (await provider.GetAsync(Ct)).IsTradingDay(new DateOnly(2026, 10, 14)).ShouldBeFalse();
        data.Reads.ShouldBe(2);
    }

    private sealed class CountingStore : XDrishti.Application.Abstractions.Market.Calendar.ITradingDayStore
    {
        public List<TradingDay> Days { get; } = [];

        public int Reads { get; private set; }

        public ValueTask<IReadOnlyList<TradingDay>> ListAsync(CancellationToken cancellationToken)
        {
            Reads++;
            return ValueTask.FromResult<IReadOnlyList<TradingDay>>([.. Days]);
        }

        public ValueTask<TradingDay?> FindAsync(DateOnly date, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<int> CountProposedAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public void Add(TradingDay day) => throw new NotSupportedException();

        public void Remove(TradingDay day) => throw new NotSupportedException();
    }
}
