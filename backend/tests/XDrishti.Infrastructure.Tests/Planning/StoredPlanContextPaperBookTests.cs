#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Market.Context;
using XDrishti.Domain.Paper;
using XDrishti.Domain.Selection;
using XDrishti.Domain.Selection.SmartRules;
using XDrishti.Infrastructure.Planning;

namespace XDrishti.Infrastructure.Tests.Planning;

/// <summary>Spec P10-02 criteria 6, 7, 8 at the plan's door: with the setting off the context is exactly P7-09's and the reader is never asked; on, the paper book fills the book, the paper account and the taken trades; unavailable gives a null book.</summary>
public sealed class StoredPlanContextPaperBookTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Session = new(2026, 11, 5);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Settings(bool assumedEmpty, bool usePaper) : IOpenBookSettings
    {
        public bool AssumedEmpty => assumedEmpty;

        public bool UsePaperLedger => usePaper;
    }

    private static (StoredPlanContextSource Source, IPaperBookReader Reader, Guid Account) Build(bool assumedEmpty, bool usePaper, Func<Guid, PaperBookRead>? read)
    {
        var limits = new RiskLimits(0.5m, 25m, 3, 5, 3.0m, 2, 0.70m, 2, 2, 2m, 1.5m, 5m, WeeklyLossAction.HalveSize);
        var profile = RiskProfile.Create("Std", limits, T0).Value;
        var account = BrokerAccount.Create("Acct", "1000000001", null, null, null, new DateOnly(2026, 10, 10), T0).Value;
        account.ConfigureTrading(new TradingSpec(TradingMode.Paper, profile.Id, CapitalSource.Fixed, 100_000m, true, false, Instruments.EquityIntraday, Instruments.None, Instruments.None, TradingOverrides.None), T0).IsSuccess.ShouldBeTrue();
        account.ChangeRoles(new RolesChange(null, true, null), null, null, T0).IsSuccess.ShouldBeTrue();
        var accounts = Substitute.For<IAccountStore>();
        accounts.ListActiveAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<BrokerAccount>>([account]));
        var profiles = Substitute.For<IRiskProfileStore>();
        profiles.ListAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<RiskProfile>>([profile]));
        var holdings = Substitute.For<IHoldingQueries>();
        holdings.ListAsync(Arg.Any<HoldingFilter>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<HoldingData>>([]));
        var reader = Substitute.For<IPaperBookReader>();
        reader.ReadAsync(Arg.Any<DateOnly>(), Arg.Any<IReadOnlyList<PaperAccountInput>>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(read?.Invoke(account.Id) ?? new PaperBookUnavailable("x")));
        return (new StoredPlanContextSource(accounts, profiles, holdings, Substitute.For<IHoldingClassifications>(), Substitute.For<IBarQueries>(), new Settings(assumedEmpty, usePaper), reader, NoContext()), reader, account.Id);
    }

    private static IContextFigures NoContext()
    {
        var figures = Substitute.For<IContextFigures>();
        figures.AsOfAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(ContextRead.Not(ContextRead.NoContext)));
        return figures;
    }

    private static PlanContextRequest Request => new(Session, Session.AddDays(1), []);

    [Fact]
    public async Task With_the_setting_off_the_reader_is_not_asked_and_the_book_stays_unavailable()
    {
        var (source, reader, _) = Build(false, false, null);

        var ctx = (await source.ReadAsync(Request, Ct)).Value;

        ctx.Book.ShouldBeNull();
        ctx.TakenTrades.ShouldBeNull();
        ctx.BookFromLedger.ShouldBeFalse();
        ctx.Notes.ShouldBe([PlanContextNotes.BookUnavailableNote]);
        ctx.Accounts.ShouldHaveSingleItem().OpenRiskPct.ShouldBeNull();
        await reader.DidNotReceiveWithAnyArgs().ReadAsync(default, default!, Ct);
    }

    [Fact]
    public async Task With_assumed_empty_alone_the_context_is_the_empty_book_as_before()
    {
        var (source, reader, _) = Build(true, false, null);

        var ctx = (await source.ReadAsync(Request, Ct)).Value;

        ctx.Book.ShouldBe([]);
        ctx.BookFromLedger.ShouldBeFalse();
        ctx.Accounts.ShouldHaveSingleItem().OpenRiskPct.ShouldBe(0);
        await reader.DidNotReceiveWithAnyArgs().ReadAsync(default, default!, Ct);
    }

    private static PaperBook Book(Guid id) => new(
        [new BookPosition("CCC", "BUY", "swing", 0.1, "IT", [])],
        [new PaperAccountBook(id, 99_989, 0.100011, 97_989, 0.1199, new HashSet<string> { "CCC" })],
        [new TakenTrade("k1", new DateOnly(2026, 11, 3), 1.8)],
        [PaperWording.BookNote]);

    [Fact]
    public async Task With_the_setting_on_the_plan_reads_the_paper_book()
    {
        var (source, reader, id) = Build(false, true, Book);

        var ctx = (await source.ReadAsync(Request, Ct)).Value;

        ctx.Book.ShouldBe(Book(id).Positions);
        ctx.BookFromLedger.ShouldBeTrue();
        ctx.TakenTrades.ShouldBe(Book(id).Taken);
        ctx.Notes.ShouldBe([PaperWording.BookNote]);
        await reader.Received(1).ReadAsync(Session, Arg.Any<IReadOnlyList<PaperAccountInput>>(), Ct);
    }

    [Fact]
    public async Task The_paper_account_takes_the_ledgers_figures()
    {
        var (source, _, _) = Build(false, true, Book);

        var account = (await source.ReadAsync(Request, Ct)).Value.Accounts.ShouldHaveSingleItem();

        (account.Equity, account.OpenRiskPct, account.FundsAvailable, account.DailyLossPct).ShouldBe((99_989, 0.100011, 97_989, 0.1199));
        account.HeldSymbols.ShouldBe(["CCC"]);
    }

    [Fact]
    public async Task An_unavailable_book_gives_a_null_book_and_the_reason_note()
    {
        var (source, _, _) = Build(false, true, _ => new PaperBookUnavailable("paper book unavailable: capture is behind (sessions without a finished capture: 2026-11-04)"));

        var ctx = (await source.ReadAsync(Request, Ct)).Value;

        ctx.Book.ShouldBeNull();
        ctx.TakenTrades.ShouldBeNull();
        ctx.BookFromLedger.ShouldBeFalse();
        ctx.Notes.ShouldBe(["paper book unavailable: capture is behind (sessions without a finished capture: 2026-11-04)"]);
        ctx.Accounts.ShouldHaveSingleItem().OpenRiskPct.ShouldBeNull();
    }
}
