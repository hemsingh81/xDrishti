using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Tests.Planning;

/// <summary>A small in-memory plan for the pre-open and expiry tests (spec P7-11): the stores and movers are fakes that keep the states, so a second run sees what the first did.</summary>
internal sealed class PreOpenFixture
{
    public static readonly DateOnly Session = new(2026, 10, 26);

    /// <summary>09:10 India time on the session.</summary>
    public static readonly DateTimeOffset PreOpenTime = new(2026, 10, 26, 3, 40, 0, TimeSpan.Zero);

    public static readonly DateTimeOffset Cutoff = new(2026, 10, 26, 3, 35, 0, TimeSpan.Zero);

    private readonly Dictionary<long, TicketState> _states = [];

    private readonly List<PlanItemRow> _items = [];

    public PreOpenFixture(PlanState planState = PlanState.Approved, DateOnly? forDate = null, DateTimeOffset? at = null)
    {
        PlanState = planState;
        ForDate = forDate ?? Session;
        Time = new FakeTimeProvider(at ?? PreOpenTime);
        Store.FindAsync(1, Arg.Any<CancellationToken>()).Returns(_ => Plan());
        Store.ListAsync(Arg.Any<DateOnly?>(), Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(_ => new List<PlanRow> { Plan() });
        Store.ItemsAsync(1, Arg.Any<CancellationToken>()).Returns(_ => (IReadOnlyList<PlanItemRow>)[.. _items.Select(i => i with { State = _states[i.Id] })]);
        Tickets.HandleAsync(Arg.Any<MoveTicketCommand>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var move = call.Arg<MoveTicketCommand>();
            Moves.Add(move);
            _states[move.ItemId] = move.To;
            return Result.Success(new TicketMoveOutcome(move.ItemId, move.To.Code()));
        });
        PlanMover.HandleAsync(Arg.Any<MovePlanCommand>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var move = call.Arg<MovePlanCommand>();
            PlanMoves.Add(move);
            PlanState = move.To;
            return Result.Success(new PlanMoveOutcome(move.PlanId, move.To.Code(), 0));
        });
    }

    public PlanState PlanState { get; private set; }

    public DateOnly ForDate { get; }

    public FakeTimeProvider Time { get; }

    public IPlanStore Store { get; } = Substitute.For<IPlanStore>();

    public IPreOpenSource Source { get; } = Substitute.For<IPreOpenSource>();

    public ISuggestionStore Inbox { get; } = Substitute.For<ISuggestionStore>();

    public ICommandHandler<MoveTicketCommand, TicketMoveOutcome> Tickets { get; } = Substitute.For<ICommandHandler<MoveTicketCommand, TicketMoveOutcome>>();

    public ICommandHandler<MovePlanCommand, PlanMoveOutcome> PlanMover { get; } = Substitute.For<ICommandHandler<MovePlanCommand, PlanMoveOutcome>>();

    public ILiveNotifier Notifier { get; } = Substitute.For<ILiveNotifier>();

    public List<MoveTicketCommand> Moves { get; } = [];

    public List<MovePlanCommand> PlanMoves { get; } = [];

    public TicketState StateOf(long id) => _states[id];

    public void SetState(long id, TicketState state) => _states[id] = state;

    public void Add(long id, string symbol, TicketState state = TicketState.Approved, TicketGroup group = TicketGroup.Primary)
    {
        _items.Add(new PlanItemRow(id, 1, new NewPlanItem(
            (int)id, group, symbol, "BUY", "ORB", new string('a', 64), "t", "s", "g", "v", group == TicketGroup.Primary ? [] : ["why"],
            new PlanEvidence("c", 30, "r", "b", 0.6, null, [], null, 0.7), []), state));
        _states[id] = state;
    }

    /// <summary>Readings whose price is timed at 09:10 India time on the session (inside the pre-open session).</summary>
    public void Quotes(params (string Symbol, double? Last, double? Previous, double? Atr)[] readings) => QuotesAt(PreOpenTime, readings);

    public void QuotesAt(DateTimeOffset? at, params (string Symbol, double? Last, double? Previous, double? Atr)[] readings)
    {
        var map = readings.ToDictionary(r => r.Symbol, r => new PreOpenReading(r.Last, r.Previous, r.Atr, at), StringComparer.Ordinal);
        Source.ReadAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(new PreOpenSnapshot(map, null));
    }

    public RunPreOpenCheckHandler PreOpen() => new(Store, Source, Tickets, PlanMover, Inbox, Notifier, Time, NullLogger<RunPreOpenCheckHandler>.Instance);

    public ExpireTicketsHandler Expiry() => new(Store, Tickets, PlanMover, Notifier, Time, NullLogger<ExpireTicketsHandler>.Instance);

    private PlanRow Plan() =>
        new(1, new NewPlan("k", ForDate, Cutoff, 1, new string('b', 64), "cell statistics only", true, "test", DateTimeOffset.UnixEpoch, []), PlanState, 2, 0, 0);
}
