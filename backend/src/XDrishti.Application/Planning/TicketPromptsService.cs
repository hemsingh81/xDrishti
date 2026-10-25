using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Planning;

/// <summary>
/// The post-trade prompts as a service (<see cref="Id"/>, spec P7-20, ADR 0115): every trading day at 16:45 it stores one prompt for each ticket that was Armed on the session and each of its paper allocation accounts (insert-only, stored once). It covers
/// up to the last three trading days, so a missed run is made up. It starts <b>paused</b>, stores nothing while <c>Planning:TicketAlerts:Enabled</c> is false, never places an order and changes no ticket.
/// </summary>
public sealed class TicketPromptsService(IServiceScopeFactory scopes, TimeProvider clock, ILogger<TicketPromptsService> logger) : IScheduledService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "post-trade-prompts";

    /// <summary>How many trading days back a run looks for sessions without prompts.</summary>
    public const int CatchUpTradingDays = 3;

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Post-trade prompts",
        "At 16:45 on trading days, after the 16:30 portfolio sync, stores one question for each ticket that was armed during the session and each paper account it was sized for: was the trade taken? The question closes by itself when a matching trade is on record. It places no order and changes no ticket. Needs Planning:TicketAlerts:Enabled.",
        "xd-api",
        [ScheduleType.Daily],
        new ServiceSettings(false, Schedule.Daily(new TimeOnly(16, 45)), 0, 4, true, true, new Dictionary<string, System.Text.Json.JsonElement>()),
        [],
        new ServiceConstraints
        {
            StartNotBefore = new TimeOnly(16, 30),
            StartAdvice = "Start it after plan-expiry (15:40) and the 16:30 portfolio sync, so the day's trades are on record when the question is asked.",
            NotBeforeSessionEnd = true,
            CatchUpReach = CatchUpReach.Latest,
        });

    ServiceDefinition IScheduledService.Definition => Definition;

    public ServiceSettings? FirstStartSettings() => null;

    public async ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        if (!services.GetRequiredService<ITicketAlertSettings>().Enabled)
        {
            return ServiceOutcome.Succeeded("Ticket alerts are off (Planning:TicketAlerts:Enabled); nothing stored.");
        }

        var now = clock.GetUtcNow();
        var calendar = await services.GetRequiredService<ITradingCalendar>().GetAsync(cancellationToken);
        var last = calendar.LastClosedSession(now);
        var sessions = new List<DateOnly>();
        for (var day = last; sessions.Count < CatchUpTradingDays && day > last.AddDays(-30); day = day.AddDays(-1))
        {
            if (calendar.IsTradingDay(day))
            {
                sessions.Add(day);
            }
        }

        var store = services.GetRequiredService<ITicketAlertStore>();
        var stored = 0;
        foreach (var session in sessions)
        {
            stored += await store.AddPromptsAsync(await store.ListArmedOnAsync(session, cancellationToken), cancellationToken);
        }

        TicketAlertLog.PromptsStored(logger, stored, sessions.Count);
        return ServiceOutcome.Succeeded($"{stored} prompt(s) stored for {sessions.Count} session(s).");
    }
}
