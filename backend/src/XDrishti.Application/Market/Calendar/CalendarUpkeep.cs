using Microsoft.Extensions.Logging;
using XDrishti.Application.Market.Bars.Pipeline;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Market.Calendar;

/// <summary>
/// What happens to the calendar after every data job (ADR 0017 §1): NSE's holiday list is tried once a day (a failure is only a job warning) and the days the stored bars
/// suggest are proposed. Never fails the job.
/// </summary>
internal sealed class CalendarUpkeep(
    NseCalendarRefresher nse,
    ObservedCalendarRunner observed,
    HolidayFetchGate gate,
    TimeProvider clock,
    ILogger<CalendarUpkeep> logger) : ICalendarUpkeep
{
    private const string Crashed = "The trading calendar could not be updated. See the API log.";

    public async ValueTask RunAsync(JobReporter reporter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reporter);
        try
        {
            if (gate.TryBegin(IndiaTime.Today(clock.GetUtcNow())))
            {
                var fetched = await nse.RefreshAsync(null, cancellationToken);
                if (!fetched.Succeeded)
                {
                    await reporter.WarnAsync("Trading calendar: " + fetched.Message, cancellationToken);
                }
            }

            await observed.RunAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // the calendar is a side effect of the job: whatever goes wrong is a warning, the job's own result stands
        catch (Exception ex)
#pragma warning restore CA1031
        {
            CalendarLog.UpkeepCrashed(logger, ex);
            await reporter.WarnAsync(Crashed, cancellationToken);
        }
    }
}
