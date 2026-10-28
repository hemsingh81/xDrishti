using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.LiveTrading;
using XDrishti.Domain.Common;
using XDrishti.Domain.LiveTrading;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The compliance record of the small live phase (spec P11-05, ADR 0147): read the owner's dated notes about the static IP, the SEBI re-check and Dhan's IP rule for reads, and write one. The write needs a session, the CSRF token, the deployment gate
/// <c>Live:Writes:Enabled</c> (OFF by default) and a fresh password for THIS save (the step-up window is not used); the actor is built from the signed-in identity, never from the body. Nothing here calls a broker or an ISP, and no route has an order segment.
/// </summary>
internal sealed class LiveTradingEndpoints : IEndpointModule
{
    private const int DefaultLimit = 50;

    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/live-trading").WithTags("Live trading");

        group.MapGet("/compliance", GetAsync)
            .WithName("GetLiveCompliance")
            .WithSummary("The compliance records, newest first (kind filters; limit 1 to 200, default 50), with the newest record of each kind (null = nothing recorded for that kind, which is empty and not 'no change') and whether the web may write now. The overdue flag is set on the newest record of a kind whose review date has passed. 422 live.invalid")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/compliance", RecordAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy) // a password is checked here, so the sign-in limit applies
            .WithName("RecordLiveCompliance")
            .WithSummary("Write one compliance record as the owner; insert-only and audited (kind, outcome, decided date; never the text). Needs a session, the CSRF token, the deployment gate Live:Writes:Enabled (off by default) and the password in the body, checked on EVERY save (an earlier step-up window does not count and is not used up). The record is your note of what you were told: nothing is checked at Dhan or the ISP. 403 live.capability_off, 401 auth.invalid_credentials, 423 auth.locked, 403 live.owner_only, 422 live.invalid (a field that looks like an IP address, client id or token, or a rule of the record), 429 auth.rate_limited")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }

    private static async Task<Results<Ok<ComplianceView>, ProblemHttpResult>> GetAsync(
        IQueryHandler<GetComplianceQuery, Result<ComplianceView>> handler,
        HttpContext http,
        CancellationToken cancellationToken,
        string? kind = null,
        int limit = DefaultLimit)
    {
        var result = await handler.HandleAsync(new GetComplianceQuery(string.IsNullOrWhiteSpace(kind) ? null : kind, limit), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Created<ComplianceRecordView>, ProblemHttpResult>> RecordAsync(
        RecordComplianceRequest request,
        HttpContext http,
        ILiveWritesGate gate,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        ICommandHandler<RecordComplianceCommand, ComplianceRecordView> handler,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";

        // The gate first (a switched-off save neither counts toward the sign-in lockout nor says anything about the password), then a fresh password for this save, then the owner from the session.
        if (!gate.Enabled)
        {
            return Problem(Error.Forbidden(LiveErrors.CapabilityOff, "The live writes are switched off in this deployment (Live__Writes__Enabled is false)."), http);
        }

        var owner = await OwnerAct.AuthorizeAsync(http, passwordCheck, request.Password, cancellationToken);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(
            new RecordComplianceCommand(owner.Value, request.Kind, request.Outcome, request.DecidedOn, request.Summary, request.Sources, request.StaticIpState, request.Isp, request.MonthlyCost, request.IpVersion, request.WhitelistedAt, request.NextReviewOn),
            cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Created("/api/live-trading/compliance?limit=1", result.Value);
    }

    /// <summary>A refused input of the record is 422, as in the Lab and the owner moves; every other failure is the shared mapping.</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http) =>
        error.Code == ComplianceRecord.InvalidCode
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code))
            : ProblemResults.From(error, http);
}
