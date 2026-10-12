using XDrishti.Application.Market.Bars.Pipeline;

namespace XDrishti.Application.Market.Calendar;

/// <summary>The work on the trading calendar that follows every data job; failures are warnings of the job, never errors.</summary>
internal interface ICalendarUpkeep
{
    ValueTask RunAsync(JobReporter reporter, CancellationToken cancellationToken);
}
