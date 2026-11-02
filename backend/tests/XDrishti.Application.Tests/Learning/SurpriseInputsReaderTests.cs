#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using NSubstitute;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Learning;
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Research;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Market.Context;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Learning;

/// <summary>
/// P15-09: the reader of the surprise numbers asks for the features of <c>fu-1</c> and <c>ev-1</c> at the close of the reaction session of each filing and no later instant, leaves out a filing that names no period, and keeps an unknown number as NaN (never filled).
/// </summary>
public sealed class SurpriseInputsReaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private static readonly DateTimeOffset Filed = new(2026, 7, 24, 16, 12, 0, Ist);

    private readonly IBarQueries _bars = Substitute.For<IBarQueries>();

    private readonly IAdjustmentFactors _factors = Substitute.For<IAdjustmentFactors>();

    private readonly IResultFactsAsOf _facts = Substitute.For<IResultFactsAsOf>();

    private readonly IShareholdingAsOf _holdings = Substitute.For<IShareholdingAsOf>();

    private readonly IContextDayStore _context = Substitute.For<IContextDayStore>();

    private SurpriseInputsReader Reader()
    {
        _context.ListAsync(Ct).Returns(ValueTask.FromResult<IReadOnlyList<ContextDayRecord>>([]));
        _bars.ReadDailyBarsAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Ct).Returns(ValueTask.FromResult<IReadOnlyList<BarRow>>([]));
        _factors.GetAsync(Arg.Any<Guid>(), Ct).Returns(XDrishti.Domain.Market.Actions.AdjustmentFactors.None);
        _facts.Facts(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Ct).Returns(ValueTask.FromResult(new ResultFactsAnswer([], 0) { ParserVersion = ResultsParser.Version }));
        _holdings.Latest(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Ct).Returns(ValueTask.FromResult(new ShareholdingAnswer(ShareholdingAnswer.Unavailable, null, null, ShareholdingAnswer.NoEntry)));
        return new SurpriseInputsReader(new ExtensionFeatureExtractor(_bars, _factors, _facts, _holdings), _context);
    }

    [Fact]
    public async Task The_facts_are_asked_at_the_close_of_the_reaction_session_and_an_unknown_number_stays_nan()
    {
        var instrument = FabricatedRun.IdA;
        var withPeriod = new ResultsFilingTime(instrument, new DateOnly(2026, 6, 30), Filed.ToUniversalTime());
        var noPeriod = new ResultsFilingTime(instrument, null, Filed.AddDays(-90).ToUniversalTime());

        var inputs = await Reader().ReadAsync([withPeriod, noPeriod], EventCalendarSnapshot.Create([], 5), new TradingCalendar(), TimeSpan.FromMinutes(5), Ct);

        // the filing of Friday 2026-07-24 16:12 reacts on Monday 2026-07-27; the numbers are asked for at its close, 15:30, and at no other instant
        var close = new DateTimeOffset(2026, 7, 27, 15, 30, 0, Ist);
        await _facts.Received(1).Facts(instrument, close, Ct);
        await _facts.DidNotReceive().Facts(instrument, Arg.Is<DateTimeOffset>(t => t != close), Ct);
        inputs.Rows.ShouldBe(1, "the filing that names no period ranks nothing and is left out");
        var found = inputs.Find(instrument, new DateOnly(2026, 6, 30), Filed).ShouldNotBeNull();
        double.IsNaN(found.Sue).ShouldBeTrue("no facts: unknown, not zero");
        double.IsNaN(found.Car3).ShouldBeTrue("no bars and no index: unknown, not zero");
        inputs.UnverifiedRows.ShouldBe(0);
    }
}
