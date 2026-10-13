using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Market.Bars.Eod;
using XDrishti.Application.Market.Bars.Gaps;
using XDrishti.Application.Market.Calendar;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// Market data (ADR 0016, P1-17a): what the system holds (per instrument: first bar, last bar, bars, source, how far behind), the data job that loads the CSV
/// folder, fetches what is missing from Dhan's Data API and aggregates every timeframe, and the latest bars of an instrument. All routes need a session; starting a
/// job also needs the CSRF token; none needs a step-up (the job only reads the mounted folder and the Data account's already stored token). The trading calendar
/// (ADR 0017, P1-11a) is under /data/calendar, the review step of the CSV load (preview, undo) under /data/csv. Part of the market module.
/// </summary>
internal sealed class DataEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/data").WithTags("Data");

        group.MapGet("/overview", GetOverviewAsync)
            .WithName("GetDataOverview")
            .WithSummary("The CSV folder (files loaded and pending), the Dhan Data account and whether a fetch can run, the aggregates per timeframe, the stored bars with the last bar (last tick) and how many trading days (by the trading calendar) it is behind, the job in progress, the last job of each phase, the next holiday, how many calendar days await confirmation, the gaps in the bars (open, unfillable, filled), the scheduled end-of-day run (enabled, time, next run, last automatic run) and pendingCorporateActions (suggested splits or bonus issues waiting for a decision)");

        group.MapGet("/instruments", ListInstrumentsAsync)
            .WithName("ListDataInstruments")
            .WithSummary("The tracked instruments and the ones with bars, with first bar, last bar, 1-minute bars, source (csv|dhan|mixed), status (current|behind|empty) and the open gaps: q, status, hasGaps (true keeps instruments with open gaps), sort (symbol|name|firstBar|lastBar|bars, '-' for descending), page, pageSize (max 200)")
            .ProducesValidationProblem();

        group.MapGet("/instruments/{id:guid}/bars", GetBarsAsync)
            .WithName("GetInstrumentBars")
            .WithSummary("The latest bars of an instrument, newest first: timeframe 1m|5m|10m|15m|30m|1h|1D|1W|1M (default 1D), take 1 to 1000 (default 100), adjusted (default false = raw prices; true adjusts prices and volume for the applied corporate actions on read — each bar by the factor of its India date, a week or month bar by the first day of its bucket — and adds the adjustment block: applied, factor, actions)")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/refresh", StartAsync)
            .WithName("StartDataRefresh")
            .WithSummary("Start a data update in the background: steps (any of csv, dhan, aggregate, quality; default csv, dhan and aggregate, always in that order; quality checks the data after the others, see /data/quality), forceCsv loads unchanged CSV files again, refetchGaps also retries the sessions the Dhan phase gave up on (unfillable), csvFiles (1 to 214 file names of the CSV folder, only with the csv step; see /data/csv/files) loads only those files - an empty list or a name the folder does not have is 400 data.invalid naming it. 202 with the job; 409 data.job_running while one runs, 409 data.master_missing without an instrument list")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/jobs", ListJobsAsync)
            .WithName("ListDataJobs")
            .WithSummary("The most recent data updates, newest first (take 1 to 100, default 20)");

        group.MapGet("/gaps", ListGapsAsync)
            .WithName("ListDataGaps")
            .WithSummary("The sessions of tracked instruments with missing 1-minute bars (ADR 0017 §3), newest first: status (open|filled|unfillable), instrumentId, page, pageSize (max 200)")
            .ProducesValidationProblem();

        group.MapGet("/notices", GetNoticesAsync)
            .WithName("GetDataNotices")
            .WithSummary("What the owner should know about the data for the notification bell: sessions with missing bars (data_gaps_open), instruments failing the data quality checks (data_quality_failed), suggested corporate actions to review (data_corporate_actions_pending). A failed scheduled run is no longer here: it is the service_failed notice of GET /api/services/notices");

        group.MapGet("/calendar", GetCalendarAsync)
            .WithName("GetTradingCalendar")
            .WithSummary("The trading calendar: confirmed holidays and special sessions from..to (yyyy-MM-dd, default today and a year on, at most 3660 days), every proposed day (from the stored bars, also a proposal next to a confirmed day: replaces names that day's kind) and the next holiday")
            .ProducesValidationProblem();

        group.MapPost("/calendar", AddCalendarDayAsync)
            .WithName("AddCalendarDay")
            .WithSummary("Add a holiday (a weekday) or a special session (sessionFrom/sessionTo HH:mm India time; both left out means 09:15 to 15:30) as confirmed, replacing what the date had. 201 for a new date, 200 when it replaced a row; 400 data.invalid with the field")
            .ProducesValidationProblem();

        group.MapPost("/calendar/refresh", RefreshCalendarAsync)
            .WithName("RefreshTradingCalendar")
            .WithSummary("Read NSE's public holiday list now (best effort, one request). Always 200: succeeded=false with the reason when NSE refused or the list could not be read; add holidays by hand then");

        group.MapPut("/calendar/{date}/confirm", ConfirmCalendarDayAsync)
            .WithName("ConfirmCalendarDay")
            .WithSummary("Confirm a proposed day (idempotent; a proposal next to a confirmed day replaces it); 404 data.calendar_day_not_found when the date has no row or was dismissed, 400 data.invalid for a date that cannot be read")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/calendar/{date}", RemoveCalendarDayAsync)
            .WithName("RemoveCalendarDay")
            .WithSummary("Take a day off the calendar, or dismiss a proposal: the owner's own entry is deleted; a proposal from the stored bars or a day from NSE's list stays as dismissed so it is not proposed again (removing a dismissed day deletes it); a date with a proposal next to a confirmed day dismisses only the proposal (remove again to take the day off). 204; 404 data.calendar_day_not_found")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/jobs/{id:guid}", GetJobAsync)
            .WithName("GetDataJob")
            .WithSummary("One data update with the progress and result of each phase")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Ok<DataOverview>> GetOverviewAsync(
        IQueryHandler<GetDataOverviewQuery, DataOverview> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetDataOverviewQuery(), cancellationToken));

    private static async Task<Results<Ok<DataInstrumentPage>, ProblemHttpResult>> ListInstrumentsAsync(
        HttpContext http,
        IQueryHandler<ListDataInstrumentsQuery, Result<DataInstrumentPage>> handler,
        CancellationToken cancellationToken,
        [FromQuery(Name = "q")] string? q = null,
        string? status = null,
        string? sort = null,
        int? page = null,
        int? pageSize = null,
        bool? hasGaps = null)
    {
        var result = await handler.HandleAsync(new ListDataInstrumentsQuery(new DataInstrumentRequest(q, status, sort, page, pageSize, hasGaps)), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<GapPage>, ProblemHttpResult>> ListGapsAsync(
        HttpContext http,
        IQueryHandler<ListBarGapsQuery, Result<GapPage>> handler,
        CancellationToken cancellationToken,
        string? status = null,
        Guid? instrumentId = null,
        int? page = null,
        int? pageSize = null)
    {
        var result = await handler.HandleAsync(new ListBarGapsQuery(new GapRequest(status, instrumentId, page, pageSize)), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Ok<DataNotices>> GetNoticesAsync(
        IQueryHandler<GetDataNoticesQuery, DataNotices> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetDataNoticesQuery(), cancellationToken));

    private static async Task<Results<Ok<InstrumentBars>, ProblemHttpResult>> GetBarsAsync(
        Guid id,
        HttpContext http,
        IQueryHandler<GetInstrumentBarsQuery, Result<InstrumentBars>> handler,
        CancellationToken cancellationToken,
        string? timeframe = null,
        int? take = null,
        bool? adjusted = null)
    {
        var result = await handler.HandleAsync(new GetInstrumentBarsQuery(id, timeframe, take, adjusted ?? false), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Accepted<DataJobView>, ProblemHttpResult>> StartAsync(
        HttpContext http,
        ICommandHandler<StartDataJobCommand, DataJobView> handler,
        CancellationToken cancellationToken,
        [FromBody] StartDataJobRequest? request = null)
    {
        var result = await handler.HandleAsync(new StartDataJobCommand(request ?? new StartDataJobRequest(), http.GetActor()), cancellationToken);
        return result.IsFailure
            ? ProblemResults.From(result.Error, http)
            : TypedResults.Accepted($"/api/data/jobs/{result.Value.Id}", result.Value);
    }

    private static async Task<Ok<IReadOnlyList<DataJobView>>> ListJobsAsync(
        IQueryHandler<ListDataJobsQuery, IReadOnlyList<DataJobView>> handler,
        CancellationToken cancellationToken,
        int? take = null) =>
        TypedResults.Ok(await handler.HandleAsync(new ListDataJobsQuery(take), cancellationToken));

    private static async Task<Results<Ok<DataJobView>, ProblemHttpResult>> GetJobAsync(
        Guid id,
        HttpContext http,
        IQueryHandler<GetDataJobQuery, Result<DataJobView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetDataJobQuery(id), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<CalendarView>, ProblemHttpResult>> GetCalendarAsync(
        HttpContext http,
        IQueryHandler<GetCalendarQuery, Result<CalendarView>> handler,
        CancellationToken cancellationToken,
        string? from = null,
        string? to = null)
    {
        var result = await handler.HandleAsync(new GetCalendarQuery(from, to), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Created<CalendarDayView>, Ok<CalendarDayView>, ProblemHttpResult>> AddCalendarDayAsync(
        HttpContext http,
        ICommandHandler<AddCalendarDayCommand, AddedCalendarDay> handler,
        CancellationToken cancellationToken,
        [FromBody] AddCalendarDayRequest? request = null)
    {
        var result = await handler.HandleAsync(new AddCalendarDayCommand(request ?? new AddCalendarDayRequest(null, null, null, null, null), http.GetActor()), cancellationToken);
        if (result.IsFailure)
        {
            return ProblemResults.From(result.Error, http);
        }

        return result.Value.Created
            ? TypedResults.Created($"/api/data/calendar?from={result.Value.Day.Date:yyyy-MM-dd}&to={result.Value.Day.Date:yyyy-MM-dd}", result.Value.Day)
            : TypedResults.Ok(result.Value.Day);
    }

    private static async Task<Results<Ok<CalendarRefreshResult>, ProblemHttpResult>> RefreshCalendarAsync(
        HttpContext http,
        ICommandHandler<RefreshCalendarCommand, CalendarRefreshResult> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new RefreshCalendarCommand(http.GetActor()), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<CalendarDayView>, ProblemHttpResult>> ConfirmCalendarDayAsync(
        string date,
        HttpContext http,
        ICommandHandler<ConfirmCalendarDayCommand, CalendarDayView> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ConfirmCalendarDayCommand(date, http.GetActor()), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RemoveCalendarDayAsync(
        string date,
        HttpContext http,
        ICommandHandler<RemoveCalendarDayCommand> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new RemoveCalendarDayCommand(date, http.GetActor()), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.NoContent();
    }
}
