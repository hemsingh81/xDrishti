using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;

namespace XDrishti.Infrastructure.Planning;

/// <summary>The checked options of <c>Planning:TicketAlerts:*</c> as the Application sees them.</summary>
internal sealed class TicketAlertSettings(TicketAlertOptions options) : ITicketAlertSettings
{
    public bool Enabled => options.Enabled;

    public int PromptTradingDays => options.PromptTradingDays;

    public int WatchedCacheSeconds => options.WatchedCacheSeconds;
}
