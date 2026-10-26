using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.ChargeSheet;
using XDrishti.Domain.Common;
using XDrishti.Domain.Reporting;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The owner's confirmation of the charge sheet (spec P8-23, ADR 0127): read what stands, and confirm, revoke or switch the capability as the owner. Every write needs a session, the CSRF token and the password typed again for THIS act (an earlier step-up window does not count and is not used up);
/// the actor is built from the signed-in identity, never from the body; confirming is refused while the capability is off (the default). This module is the only place that writes the confirmation; nothing here places an order or changes a stored run.
/// </summary>
internal sealed class ChargeSheetEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/charge-sheet").WithTags("ChargeSheet");

        group.MapGet("/confirmation", GetAsync)
            .WithName("GetChargeSheetConfirmation")
            .WithSummary("What stands about the owner's confirmation of the charge sheet (not_confirmed, confirmed, stale, sheet_missing, revoked), whether confirming is switched on (off by default), a preview of the stored sheet (the one named by sheetId, else the one in force) with the rates hash a confirmation must carry, and the history. 400 chargesheet.sheet_invalid")
            .ProducesRejections(conflict: false, notFound: false);

        group.MapPost("/confirmation", ConfirmAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy) // a password is checked here, so the sign-in limit applies
            .WithName("ConfirmChargeSheet")
            .WithSummary("Confirm ONE stored charge sheet as the owner, at the SHA-256 of its rates as shown; appends an audited row. Needs a session, the CSRF token and the password in the body, checked on EVERY request. 401 auth.invalid_credentials, 423 auth.locked, 403 chargesheet.capability_off or chargesheet.owner_only, 404 chargesheet.sheet_not_found, 400 chargesheet.sheet_invalid, chargesheet.rates_invalid or chargesheet.reason_invalid, 409 chargesheet.rates_changed, 429 auth.rate_limited. Repeating the same confirmation writes nothing and answers changed=false")
            .ProducesRejections(conflict: true);

        group.MapPost("/confirmation/revoke", RevokeAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy)
            .WithName("RevokeChargeSheetConfirmation")
            .WithSummary("Withdraw the confirmation in force as the owner; appends an audited row (a revoke never edits the confirmation it withdraws). Works also while confirming is switched off. Revoking when nothing is confirmed writes nothing and answers changed=false. Needs a session, the CSRF token and the password in the body, checked on EVERY request. 401 auth.invalid_credentials, 423 auth.locked, 400 chargesheet.reason_invalid, 429 auth.rate_limited")
            .ProducesRejections(conflict: false, notFound: false);

        group.MapPut("/capability", ChangeCapabilityAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy)
            .WithName("ChangeChargeSheetCapability")
            .WithSummary("Switch on or off the capability to confirm a charge sheet (off by default; no setting confirms anything by itself). Appends an audited row. Needs a session, the CSRF token and the password in the body, checked on EVERY request. 401 auth.invalid_credentials, 423 auth.locked, 400 chargesheet.reason_invalid, 429 auth.rate_limited")
            .ProducesRejections(conflict: false, notFound: false);
    }

    private static async Task<Results<Ok<ChargeSheetStatusView>, ProblemHttpResult>> GetAsync(string? sheetId, HttpContext http, IQueryHandler<GetChargeSheetStatusQuery, Result<ChargeSheetStatusView>> handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetChargeSheetStatusQuery(string.IsNullOrWhiteSpace(sheetId) ? null : sheetId.Trim()), cancellationToken);
        http.Response.Headers.CacheControl = "no-store";
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static Task<Results<Ok<ChargeSheetActOutcome>, ProblemHttpResult>> ConfirmAsync(ConfirmChargeSheetRequest request, HttpContext http, ICommandHandler<StepUpCommand, StepUpResult> passwordCheck, ICommandHandler<RecordChargeSheetActCommand, ChargeSheetActOutcome> handler, CancellationToken cancellationToken) =>
        ActAsync(http, passwordCheck, handler, request.Password, owner => new RecordChargeSheetActCommand(ChargeSheetActKind.Confirm, owner, request.Reason ?? string.Empty, request.SheetId, request.RatesSha256), cancellationToken);

    private static Task<Results<Ok<ChargeSheetActOutcome>, ProblemHttpResult>> RevokeAsync(RevokeChargeSheetRequest request, HttpContext http, ICommandHandler<StepUpCommand, StepUpResult> passwordCheck, ICommandHandler<RecordChargeSheetActCommand, ChargeSheetActOutcome> handler, CancellationToken cancellationToken) =>
        ActAsync(http, passwordCheck, handler, request.Password, owner => new RecordChargeSheetActCommand(ChargeSheetActKind.Revoke, owner, request.Reason ?? string.Empty, null, null), cancellationToken);

    private static Task<Results<Ok<ChargeSheetActOutcome>, ProblemHttpResult>> ChangeCapabilityAsync(ChangeChargeSheetCapabilityRequest request, HttpContext http, ICommandHandler<StepUpCommand, StepUpResult> passwordCheck, ICommandHandler<RecordChargeSheetActCommand, ChargeSheetActOutcome> handler, CancellationToken cancellationToken) =>
        ActAsync(http, passwordCheck, handler, request.Password, owner => new RecordChargeSheetActCommand(request.On ? ChargeSheetActKind.GateOn : ChargeSheetActKind.GateOff, owner, request.Reason ?? string.Empty, null, null), cancellationToken);

    private static async Task<Results<Ok<ChargeSheetActOutcome>, ProblemHttpResult>> ActAsync(
        HttpContext http,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        ICommandHandler<RecordChargeSheetActCommand, ChargeSheetActOutcome> handler,
        string? password,
        Func<string, RecordChargeSheetActCommand> command,
        CancellationToken cancellationToken)
    {
        var owner = await OwnerAct.AuthorizeAsync(http, passwordCheck, password, cancellationToken);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(command(owner.Value), cancellationToken);
        http.Response.Headers.CacheControl = "no-store";
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }
}
