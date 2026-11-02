using XDrishti.Domain.Filings;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Abstractions.Research;

/// <summary>
/// Where a run that reads <c>results_sue_q</c> or <c>results_car3_q</c> gets its surprise numbers (P15-09): for each results filing of the universe, the <c>sue_yoy</c> and the <c>results_car3</c> as they were computable at the close of its reaction session, through the same point-in-time
/// functions the extensions <c>fu-1</c> and <c>ev-1</c> use. Reads only; nothing is stored. An unknown number is NaN, never filled.
/// </summary>
public interface ISurpriseInputsReader
{
    /// <summary>Reads the numbers of the filings (only those that name a period) and builds the inputs with the breakpoint rules of <see cref="SurpriseInputs"/>.</summary>
    ValueTask<SurpriseInputs> ReadAsync(IReadOnlyList<ResultsFilingTime> filings, EventCalendarSnapshot snapshot, TradingCalendar calendar, TimeSpan lag, CancellationToken cancellationToken);
}
