#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Selection;
using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Application.Tests.Planning;

/// <summary>
/// The seeded fixture of P7-09: six candidates, one paper account and one live account, a correlation table; the plan it must reach is worked out by hand in <see cref="WiredPlanEndToEndTests"/>.
/// <code>
/// symbol  sector  entry  stop  turnover  win  conservative E[R] (lower 0.5)  correlation with AAA
/// AAA     Banks   100    98    50 M      2.0  0.5*2.0 - 0.5*1 = 0.50          -
/// BBB     Banks   200    190   100 M     1.8  0.40                            0.3
/// CCC     Banks   100    98    50 M      1.6  0.30                            0.2 (BBB 0.4)
/// FFF     (none)  100    98    50 M      1.5  0.25                            none
/// DDD     IT      100    98    50 M      1.4  0.20                            0.9
/// EEE     IT      100    98    50 M      2.0  posterior win rate 0.40 &lt; 0.45: not eligible
/// </code>
/// </summary>
internal static class WiredPlanFixture
{
    public static readonly DateOnly Session = new(2026, 10, 23);

    public static readonly DateOnly ForDate = new(2026, 10, 26);

    public static readonly PlanBuildRequest Request = new(Session, ForDate, NightlyRules.ReviewCutoff(ForDate), new DateTimeOffset(2026, 10, 23, 13, 0, 0, TimeSpan.Zero));

    public static readonly Guid Paper = new("00000000-0000-0000-0000-0000000000a1");

    public static readonly Guid Live = new("00000000-0000-0000-0000-0000000000b2");

    /// <summary>cost, event, F&amp;O ban and surveillance have no data: the owner loosens them (P7-01), so the candidates are not all held back.</summary>
    public static readonly SelectionRules Loose = new(OptionalFilters: [FilterCodes.Cost, FilterCodes.Event, FilterCodes.FnoBan, FilterCodes.Surveillance]);

    public static CellStatsRow Stats(string strategy, double avgWin, double posterior = 0.55) => new(
        new CellId(strategy, "1D", "BUY", "swing", "fixed"), CellWindow.All, new DateOnly(2026, 10, 22), true, 40, 20, 40, 0.5, 0.5, posterior, 0.50, 0.6, avgWin, -1.0, 0.5, 1.5, 3, 0.3, 0.8, 0.5, 1.5, true);

    public static TicketCandidate Candidate(string symbol, double avgWin, double entry = 100, double stop = 98, double turnover = 50_000_000, double posterior = 0.55)
    {
        var stats = Stats("S_" + symbol, avgWin, posterior);
        var draft = new TicketDraft(
            TicketVerdict.Conditional, symbol, "BUY", "S_" + symbol, new string('a', 64), Session, SetupTruth.True, SetupTruth.Unknown, SetupTruth.False, SetupTruth.Unknown, [],
            "reason", "watch the breakout", "stop below the low", "target 2 x risk", "valid for the day", stop, entry, 2.0, turnover, ["setup"]);
        return new TicketCandidate(draft, stats.Cell, stats, CellState.Active, Guid.NewGuid());
    }

    public static IReadOnlyList<TicketCandidate> Candidates() =>
    [
        Candidate("AAA", 2.0),
        Candidate("BBB", 1.8, 200, 190, 100_000_000),
        Candidate("CCC", 1.6),
        Candidate("FFF", 1.5),
        Candidate("DDD", 1.4),
        Candidate("EEE", 2.0, posterior: 0.40),
    ];

    public static ContextAccount PaperAccount(HashSet<string>? held = null) => new(Paper, TradingMode.Paper, 1_000_000, 0.5, 20, 3, 0, 1_000_000, 2, 1.5, 0, held ?? []);

    public static ContextAccount LiveAccount() => new(Live, TradingMode.ManualLive, 1_000_000, 0.5, 20, 3, 0, 1_000_000, 2, 1.5, 0, new HashSet<string>());

    public static PlanContext Context(MarketGateInput? gate = null, IReadOnlyList<ContextAccount>? accounts = null, bool withCorrelations = true, bool bookAssumedEmpty = true) => new(
        bookAssumedEmpty ? [] : null,
        new Dictionary<string, InstrumentFacts>
        {
            ["AAA"] = new("Banks", ["Nifty"], false),
            ["BBB"] = new("Banks", ["Nifty"], false),
            ["CCC"] = new("Banks", ["Nifty"], false),
            ["FFF"] = new(null, ["Nifty"], false),
            ["DDD"] = new("IT", ["Nifty"], false),
            ["EEE"] = new("IT", ["Nifty"], false),
        },
        accounts ?? [PaperAccount(), LiveAccount()],
        withCorrelations
            ? new CorrelationTable([("AAA", "BBB", 0.3), ("AAA", "CCC", 0.2), ("BBB", "CCC", 0.4), ("AAA", "DDD", 0.9), ("BBB", "DDD", 0.1), ("CCC", "DDD", 0.1)])
            : null,
        gate,
        null,
        Notes(bookAssumedEmpty, accounts));

    /// <summary>The notes the stored context source adds (its own constants).</summary>
    public static IReadOnlyList<string> Notes(bool bookAssumedEmpty, IReadOnlyList<ContextAccount>? accounts)
    {
        var notes = new List<string> { bookAssumedEmpty ? PlanContextNotes.NoBookNote : PlanContextNotes.BookUnavailableNote };
        if (accounts is not null && !accounts.Any(a => a.Mode == TradingMode.Paper))
        {
            notes.Add(PlanContextNotes.NoAccountNote);
        }

        return notes;
    }

    public static SelectionPlanBuilder Builder(IReadOnlyList<TicketCandidate> found, PlanContext context, out IPlanContextSource contextSource, SelectionRules? rules = null)
    {
        var source = Substitute.For<ITicketSource>();
        source.ReadAsync(Session, ForDate, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(new TicketSourceResult(found, 1, 2, ["history not ready at the close"]))));
        var ctx = Substitute.For<IPlanContextSource>();
        ctx.ReadAsync(Arg.Any<PlanContextRequest>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(context)));
        contextSource = ctx;
        return new SelectionPlanBuilder(source, ctx, NullLogger<SelectionPlanBuilder>.Instance, rules ?? Loose);
    }
}
