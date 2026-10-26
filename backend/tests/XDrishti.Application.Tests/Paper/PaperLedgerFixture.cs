#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Paper;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Paper;

namespace XDrishti.Application.Tests.Paper;

/// <summary>
/// The hand-worked fixture of spec P10-02 over fake ports: epoch 1 started Tuesday 2026-11-03, now is the evening of Thursday 2026-11-05 (last closed session), capital 100,000.00; closed A (result 109.00, exit 11-03), closed B (result -120.00, exit 11-05)
/// and open C (BUY 40 at 50.00, stop 47.50). Capture finished for every session unless a test says otherwise.
/// </summary>
internal sealed class PaperLedgerFixture
{
    public static readonly BrokerAccount PaperOne = Make("1000000011", 100_000m);

    public static Guid AccountId => PaperOne.Id;

    public static readonly DateOnly Day1 = new(2026, 11, 3);

    public static readonly DateOnly Last = new(2026, 11, 5);

    public static readonly EpochRow Epoch = new(1, Day1, new DateTimeOffset(2026, 11, 3, 11, 30, 0, TimeSpan.Zero), "first", "the default epoch", "system:paper-capture");

    public List<LedgerTrade> Trades { get; set; } = [A(), B(), C()];

    public HashSet<DateOnly> Finished { get; } = [Day1, new(2026, 11, 4), Last];

    public Dictionary<string, InstrumentClose> Closes { get; } = new(StringComparer.OrdinalIgnoreCase) { ["CCC"] = new(Guid.Parse("00000000-0000-0000-0000-0000000000c3"), new DailyClose(Last, 51.00m, true)) };

    public List<BrokerAccount> Active { get; set; } = [PaperOne];

    public IReadOnlyList<PaperAccountInput> Inputs => [.. Active.Select(a => new PaperAccountInput(a.Id, a.Name, a.Trading.CapitalAmount, string.Empty))];

    public EpochRow? CurrentEpoch { get; set; } = Epoch;

    public StoredTotals Totals { get; set; } = new(-11.00m, 1, 0);

    public List<Guid> EntryAccounts { get; set; } = [AccountId];

    public IPaperLedgerData Data { get; }

    public PaperLedgerService Ledgers { get; }

    public IHoldingClassifications Classifications { get; } = Substitute.For<IHoldingClassifications>();

    public ITradingCalendar Calendars { get; } = Substitute.For<ITradingCalendar>();

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 5, 10, 30, 0, TimeSpan.Zero));

    public PaperLedgerFixture()
    {
        Data = Substitute.For<IPaperLedgerData>();
        Data.EpochAsync(Arg.Any<long?>(), Arg.Any<CancellationToken>()).Returns(c => ValueTask.FromResult(c.Arg<long?>() is { } id && id != 1 ? null : CurrentEpoch));
        Data.TradesAsync(Arg.Any<long>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(c => ValueTask.FromResult<IReadOnlyList<LedgerTrade>>([.. Trades.Where(t => c.Arg<Guid?>() is not { } a || a == t.AccountId)]));
        Data.FinishedSessionsAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult<IReadOnlySet<DateOnly>>(Finished));
        Data.ClosesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult<IReadOnlyDictionary<string, InstrumentClose>>(Closes));
        Data.EntryAccountsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult<IReadOnlyList<Guid>>(EntryAccounts));
        Data.TotalsAsync(Arg.Any<long>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(Totals));
        Calendars.GetAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(new TradingCalendar()));
        Classifications.SectorsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string> { [Guid.Parse("00000000-0000-0000-0000-0000000000c3")] = "IT" }));
        Classifications.BasketsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<BasketRef>>>(new Dictionary<Guid, IReadOnlyList<BasketRef>>()));
        var accounts = Substitute.For<IAccountStore>();
        accounts.ListActiveAsync(Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult<IReadOnlyList<BrokerAccount>>([.. Active]));
        Ledgers = new PaperLedgerService(Data, accounts);
    }

    public PaperBookReader Reader() => new(Ledgers, Data, Calendars, Classifications, NullLogger<PaperBookReader>.Instance);

    public PaperLedgerContext Context() => new(Data, Ledgers, Calendars, Time);

    public static LedgerTrade A(Guid? account = null, string key = "ORB|AAA|BUY|2026-11-02") =>
        new(1, account ?? AccountId, "AAA", "BUY", 60, At(Day1, 9, 20), 100.15m, 99.05m, false, "primary", key, new LedgerExit(At(Day1, 11, 5), 129.00m, 20.00m, 109.00m, "flat-10", 1.8));

    public static LedgerTrade B() =>
        new(2, AccountId, "BBB", "SELL", 50, At(Last, 9, 20), 200.00m, 204.00m, false, "primary", "ORB|BBB|SELL|2026-11-04", new LedgerExit(At(Last, 13, 0), -100.00m, 20.00m, -120.00m, "flat-10", -1.1));

    public static LedgerTrade C() => new(3, AccountId, "CCC", "BUY", 40, At(Day1, 10, 0), 50.00m, 47.50m, true, "primary", "ORB|CCC|BUY|2026-11-02", null);

    private static DateTimeOffset At(DateOnly day, int hour, int minute) => new(day.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.FromMinutes(330));

    public static BrokerAccount Make(string client, decimal? capital)
    {
        var limits = new RiskLimits(0.5m, 25m, 3, 5, 3.0m, 2, 0.70m, 2, 2, 2m, 1.5m, 5m, WeeklyLossAction.HalveSize);
        var t0 = new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);
        var profile = RiskProfile.Create("Std", limits, t0).Value;
        var account = BrokerAccount.Create("Acct " + client, client, null, null, null, new DateOnly(2026, 10, 10), t0).Value;
        var source = capital is null ? CapitalSource.BrokerFunds : CapitalSource.Fixed;
        account.ConfigureTrading(new TradingSpec(TradingMode.Paper, profile.Id, source, capital, true, false, Instruments.EquityIntraday, Instruments.None, Instruments.None, TradingOverrides.None), t0).IsSuccess.ShouldBeTrue();
        account.ChangeRoles(new RolesChange(null, true, null), null, null, t0).IsSuccess.ShouldBeTrue();
        return account;
    }
}
