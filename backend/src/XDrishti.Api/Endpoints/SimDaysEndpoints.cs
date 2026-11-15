using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Replay.Sim;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The day-by-day simulation (SR-04, ADR 0293): start, read the status, a day and its tape, settle the cursor day (Instant), advance, auto-run, continue, cancel. The owner's session is needed for every route. Research only: it places no order and changes no plan, paper, shadow, statistic, model or
/// setting; there is no push endpoint. Every answer carries the hindsight labels and the gross cost basis.
/// </summary>
internal sealed class SimDaysEndpoints : IEndpointModule
{
    private const string Codes = " 404 sim.not_found; 409 sim.busy, sim.not_settled, sim.not_complete, sim.nothing_new, sim.final; 422 sim.invalid.";

    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/sim/days").WithTags("Simulation");

        group.MapPost("", StartAsync)
            .WithName("StartSimDays")
            .WithSummary("Start a day-by-day simulation: body start, end (default the last complete stored session; clamped to the day before hold-out block 1 and its embargo unless block 1's strategy look is stored, which the start takes automatically when free and says so in holdOutNote), scope {basketIds, instrumentIds} (required), capital (default Sim:DefaultCapital), riskProfileId (default the seeded standard profile), mode (learning or frozen, default learning). Gross costs. 201 with the id. 422 sim.invalid (start after end, before the first stored bar plus the warm-up, empty scope, capital <= 0); 409 sim.busy (a simulation is active).")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetSimDays")
            .WithSummary("The status of a simulation: cursor day, phase (idle, settling, settled, complete, cancelled, error), auto, equity, days done of total, the hold-out note, the hindsight labels and the cost basis. Read only." + Codes)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/days/{date}", DayAsync)
            .WithName("GetSimDay")
            .WithSummary("One day (yyyy-MM-dd): the picks, blocked with reasons, fills, the trades entering or exiting that day, the closing equity and result hash; stored false for the settled cursor day before it is advanced. 404 sim.day_not_found. Read only.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/days/{date}/tape", TapeAsync)
            .WithName("GetSimDayTape")
            .WithSummary("One page of a settled day's tape for playback: the stored 1-minute bars of the instruments in play (picked, held or traded that day) and the trade events (entry, exit; the stop is not moved) whose minute lies in [from, to] (date-times on the day, default 09:15 to 15:29 India time), and the live board at to (open positions, entry, stop, last price, running P&L, realised equity), using only bars up to to. Nothing after to and nothing beyond the settled day. 404 sim.not_found, sim.day_not_found; 409 sim.not_settled; 422 sim.invalid. Read only.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/settle", SettleAsync)
            .WithName("SettleSimDay")
            .WithSummary("Settle the cursor day (Instant): the runner moves the phase settling to settled. 202 with the status." + Codes)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/advance", AdvanceAsync)
            .WithName("AdvanceSimDay")
            .WithSummary("Store the settled day (immutable) and move the cursor to the next trading session, or complete the run at its end." + Codes)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/auto", AutoAsync)
            .WithName("SetSimDaysAuto")
            .WithSummary("Auto-run on or off (body on): on settles and advances every day to the end; off stops after the current day." + Codes)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/continue", ContinueAsync)
            .WithName("ContinueSimDays")
            .WithSummary("Extend a complete simulation to the newest complete stored session; the stored days are not recomputed." + Codes)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/cancel", CancelAsync)
            .WithName("CancelSimDays")
            .WithSummary("Cancel a simulation; the stored days stay." + Codes)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<Results<Created<SimDaysStarted>, ProblemHttpResult>> StartAsync(SimDaysStartBody body, HttpContext http, ICommandHandler<StartSimDaysCommand, Guid> handler, CancellationToken cancellationToken)
    {
        if (OwnerAct.SessionOwner(http) is { IsFailure: true } owner)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new StartSimDaysCommand(body.Start, body.End, body.Scope?.BasketIds, body.Scope?.InstrumentIds, body.Capital, body.RiskProfileId, body.Mode), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Created("/api/sim/days/" + result.Value.ToString("D"), new SimDaysStarted(result.Value));
    }

    private static async Task<Results<Ok<SimDaysView>, ProblemHttpResult>> GetAsync(Guid id, HttpContext http, IQueryHandler<GetSimDaysQuery, Result<SimDaysView>> handler, CancellationToken cancellationToken) =>
        Read(http, await handler.HandleAsync(new GetSimDaysQuery(id), cancellationToken));

    private static async Task<Results<Ok<SimDaySnapshotView>, ProblemHttpResult>> DayAsync(Guid id, DateOnly date, HttpContext http, IQueryHandler<GetSimDayQuery, Result<SimDaySnapshotView>> handler, CancellationToken cancellationToken) =>
        Read(http, await handler.HandleAsync(new GetSimDayQuery(id, date), cancellationToken));

    private static async Task<Results<Ok<SimDayTapeView>, ProblemHttpResult>> TapeAsync(Guid id, DateOnly date, DateTimeOffset? from, DateTimeOffset? to, HttpContext http, IQueryHandler<GetSimDayTapeQuery, Result<SimDayTapeView>> handler, CancellationToken cancellationToken) =>
        Read(http, await handler.HandleAsync(new GetSimDayTapeQuery(id, date, from, to), cancellationToken));

    private static async Task<Results<Accepted<SimDaysView>, ProblemHttpResult>> SettleAsync(Guid id, HttpContext http, ICommandHandler<SettleSimDayCommand, SimDaysView> handler, CancellationToken cancellationToken)
    {
        if (OwnerAct.SessionOwner(http) is { IsFailure: true } owner)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new SettleSimDayCommand(id), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Accepted("/api/sim/days/" + id.ToString("D"), result.Value);
    }

    private static Task<Results<Ok<SimDaysView>, ProblemHttpResult>> AdvanceAsync(Guid id, HttpContext http, ICommandHandler<AdvanceSimDayCommand, SimDaysView> handler, CancellationToken cancellationToken) =>
        ActAsync(http, () => handler.HandleAsync(new AdvanceSimDayCommand(id), cancellationToken));

    private static Task<Results<Ok<SimDaysView>, ProblemHttpResult>> AutoAsync(Guid id, SimDaysAutoBody body, HttpContext http, ICommandHandler<SetSimAutoCommand, SimDaysView> handler, CancellationToken cancellationToken) =>
        ActAsync(http, () => handler.HandleAsync(new SetSimAutoCommand(id, body.On), cancellationToken));

    private static Task<Results<Ok<SimDaysView>, ProblemHttpResult>> ContinueAsync(Guid id, HttpContext http, ICommandHandler<ContinueSimDaysCommand, SimDaysView> handler, CancellationToken cancellationToken) =>
        ActAsync(http, () => handler.HandleAsync(new ContinueSimDaysCommand(id), cancellationToken));

    private static Task<Results<Ok<SimDaysView>, ProblemHttpResult>> CancelAsync(Guid id, HttpContext http, ICommandHandler<CancelSimDaysCommand, SimDaysView> handler, CancellationToken cancellationToken) =>
        ActAsync(http, () => handler.HandleAsync(new CancelSimDaysCommand(id), cancellationToken));

    private static async Task<Results<Ok<SimDaysView>, ProblemHttpResult>> ActAsync(HttpContext http, Func<ValueTask<Result<SimDaysView>>> act)
    {
        if (OwnerAct.SessionOwner(http) is { IsFailure: true } owner)
        {
            return ProblemResults.From(owner.Error, http);
        }

        return Read(http, await act());
    }

    private static Results<Ok<T>, ProblemHttpResult> Read<T>(HttpContext http, Result<T> result)
    {
        http.Response.Headers.CacheControl = "no-store"; // a simulation is read while it changes
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    /// <summary>A refused input is 422 (sim.invalid); everything else is the shared mapping (409, 404).</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http) =>
        error.Kind == ErrorKind.Validation
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code))
            : ProblemResults.From(error, http);
}
