using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Planning;

/// <summary>
/// Reads the stored crossings, prompts and acts and derives what the owner sees (spec P7-20, ADR 0115): statuses from the stored trades at read time, visibility from the newest act. Shared by the plan screen's view and the bell's notice source,
/// so both say the same. Writes nothing.
/// </summary>
internal sealed class TicketAlertReader(ITicketAlertStore store, ITradingCalendar calendar)
{
    /// <summary>The derived rows of sessions from <paramref name="fromSession"/> on (optionally one plan), and the muted items.</summary>
    public async ValueTask<(IReadOnlyList<CrossingView> Crossings, IReadOnlyList<PromptView> Prompts, IReadOnlyList<long> Muted)> ReadAsync(DateOnly fromSession, long? planId, int windowDays, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var crossings = await store.ListCrossingsAsync(fromSession, planId, cancellationToken);
        var prompts = await store.ListPromptsAsync(fromSession, planId, cancellationToken);
        var acks = await store.ListAcksAsync(cancellationToken);
        var muted = acks.Where(a => a is { NoticeKind: "ticket", Action: AlertAcks.Mute, ItemId: not null }).Select(a => a.ItemId!.Value).Distinct().Order().ToList();

        IReadOnlyList<AlertAck> AcksOf(string kind, long id) =>
            [.. acks.Where(a => a.NoticeKind == kind && a.NoticeId == id && a.Action is AlertAcks.Snooze or AlertAcks.Dismiss).Select(a => new AlertAck(a.Id, a.Action, a.Until))];

        var crossingViews = crossings.Select(c =>
        {
            var own = AcksOf("crossing", c.Id);
            var state = AlertAcks.Visibility(own, now);
            return new CrossingView(c.Id, $"crossing:{c.Id}", c.PlanId, c.ItemId, c.Kind, c.Level, c.StopAnchor, c.Price, c.TickAt, TicketCrossing.Text(c.Kind, c.StopAnchor, c.Session, c.Level, c.Price),
                StateText(state), AlertAcks.SnoozedUntil(own, now), Math.Max(0, AlertAcks.MaxSnoozes - own.Count(a => a.Action == AlertAcks.Snooze)));
        }).ToList();

        var promptViews = new List<PromptView>();
        if (prompts.Count > 0)
        {
            var today = Domain.Market.IndiaTime.Today(now);
            var trades = await store.TradesAsync(prompts.Min(p => p.Session), today, cancellationToken);
            var facts = prompts.Select(p => new PromptFacts(p.Id, p.ItemId, p.AccountId, TradeRules.NormaliseSymbol(p.Symbol), TicketLifecycle.ParseSide(p.Side) ?? Domain.Research.Side.Buy, p.Session)).ToList();
            var results = PromptRules.Evaluate(facts, trades, today, await calendar.GetAsync(cancellationToken), windowDays);
            foreach (var (prompt, result) in prompts.Zip(results))
            {
                // An ambiguous prompt is its own notice (prompt:{id}:ambiguous) with its own acts: snoozing the open question does not hide it.
                var ambiguous = result.Status == PromptStatus.Ambiguous;
                var own = AcksOf(ambiguous ? TicketAlertErrors.AmbiguousKind : "prompt", prompt.Id);
                promptViews.Add(new PromptView(prompt.Id, ambiguous ? $"prompt:{prompt.Id}:ambiguous" : $"prompt:{prompt.Id}", prompt.PlanId, prompt.ItemId, prompt.AccountId, prompt.Session, result.Status, result.Via, result.SameTradeItems, PromptRules.Text(result),
                    StateText(AlertAcks.Visibility(own, now)), AlertAcks.SnoozedUntil(own, now), Math.Max(0, AlertAcks.MaxSnoozes - own.Count(a => a.Action == AlertAcks.Snooze))));
            }
        }

        return (crossingViews, promptViews, muted);
    }

    private static string StateText(AlertVisibility v) => v switch
    {
        AlertVisibility.Snoozed => "snoozed",
        AlertVisibility.Dismissed => "dismissed",
        _ => "shown",
    };
}
