using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Checks;

namespace XDrishti.Execution.Tests.Checks;

/// <summary>Hand fixtures of the spec: capital 500,000; profile risk 0.5%, daily loss 1.5% / 2 R, max total risk 3%; entry 1,500, stop 1,485, target 1,530, tick 0.05, 10:30 IST on a trading day. Everything passes.</summary>
internal sealed record Facts(IntentFacts I, AccountFacts A, MarketFacts M, SystemFacts S, ExecLimits L)
{
    // 05:00 UTC = 10:30 IST.
    public static readonly DateTimeOffset Now = new(2026, 10, 29, 5, 0, 0, TimeSpan.Zero);

    public static Facts Good()
    {
        var intent = new IntentFacts("BUY", "NSE_EQ", "INTRADAY", "11536", 1500, 1485, 1530, 10, 0.05, new TicketFacts(TicketState.Armed, "owner", false, 10));
        var account = new AccountFacts(
            500_000, new ProfileFacts(0.5, 2, 1.5, 3, 3, 5, 2), new Stamped<double>(100_000, Now), new Stamped<MarginFacts>(new MarginFacts(2_000, false), Now), 0, 0,
            new Stamped<BookFacts>(new BookFacts(0, 0, false, false, null), Now), new Stamped<double>(0, Now), 0, 0, false, "IT", 0, false, false);
        var market = new MarketFacts(
            new Stamped<QuoteFacts>(new QuoteFacts(1500, 1499.9, 1500.1, 1650, 1350, 100), Now), new InstrumentFacts(true, true, false), true, null, 100_000, 10, false, false, 0.2, true);
        var system = new SystemFacts(
            new ModeAnswer(ExecMode.SemiAuto, true), ExecEnvironment.Sandbox, HaltReading.Clear, HaltReading.Clear, new Stamped<bool>(true, Now), new Stamped<DateTimeOffset>(Now.AddHours(2), Now),
            new Stamped<bool>(true, Now), new Stamped<bool>(true, Now), null, false, 3, true);
        return new Facts(intent, account, market, system, ExecLimits.Default);
    }

    public CheckRun Run(CheckRunKind kind = CheckRunKind.Full, CheckConfirmation? confirmation = null, DateTimeOffset? at = null) =>
        ExecChecklist.Evaluate(kind, I, A, M, S, L, at ?? Now, confirmation);

    public CheckResult Result(string code, DateTimeOffset? at = null) => Run(CheckRunKind.Full, null, at).Results.Single(r => r.Code == code);

    public CheckFacts ToLoaded() => new(I, A, M, S, L);

    public static readonly DateTimeOffset Stale = Now - TimeSpan.FromHours(1);
}
