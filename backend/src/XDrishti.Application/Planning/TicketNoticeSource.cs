using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Market;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>
/// The ticket notices of the bell (spec P7-20, ADR 0115): <c>ticket_level_crossed</c> (a stored crossing not snoozed or dismissed, until the end of the next India day) and <c>post_trade_prompt</c> (open or ambiguous prompts; a matched one stays as
/// resolved until the end of the day after its session; an expired one is not a notice). Counts and words, never a symbol or a price; route only, no command. Derived on read, nothing stored. With <c>Planning:TicketAlerts:Enabled</c> false it answers
/// nothing and reads nothing, exactly like the null source it replaces.
/// </summary>
internal sealed class TicketNoticeSource(TicketAlertReader reader, ITicketAlertSettings options) : ITicketNoticeSource
{
    private const int LookbackDays = 14;

    public async ValueTask<IReadOnlyList<PlanNotice>> ListAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return [];
        }

        var today = IndiaTime.Today(now);
        var (crossings, prompts, _) = await reader.ReadAsync(today.AddDays(-LookbackDays), null, options.PromptTradingDays, now, cancellationToken);
        var notices = new List<PlanNotice>();
        foreach (var c in crossings.Where(c => c.State == "shown" && TicketCrossing.IsLive(c.TickAt, now)))
        {
            notices.Add(new PlanNotice(c.NoticeId, "ticket_level_crossed", "warning", TicketCrossing.BellText(c.Kind), $"/plans?plan={c.PlanId}", c.PlanId, c.TickAt, c.TickAt));
        }

        foreach (var p in prompts.Where(p => p.State == "shown" && p.Status != PromptStatus.ExpiredNoFill))
        {
            var since = new DateTimeOffset(p.Session.ToDateTime(new TimeOnly(16, 45)), IndiaTime.Offset);
            var resolved = p.Status == PromptStatus.Matched;
            if (resolved && today > p.Session.AddDays(1))
            {
                continue;
            }

            notices.Add(new PlanNotice(p.NoticeId, "post_trade_prompt", "info", PromptRules.BellText(p.Status), $"/plans?plan={p.PlanId}", p.PlanId, since, since, resolved));
        }

        return notices;
    }
}
