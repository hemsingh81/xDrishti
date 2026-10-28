using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Accounts;
using XDrishti.Application.Accounts.Connections;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// Dhan accounts, their roles and settings. Broker secrets are write-only (stored encrypted, reported only as set / not set). Sensitive
/// writes need a recent password re-entry (<c>POST /api/auth/step-up</c>).
/// </summary>
internal sealed class AccountsEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/accounts").WithTags("Accounts");

        group.MapGet("/", ListAsync)
            .WithName("ListAccounts")
            .WithSummary("Every active account with its roles, masked client id and which broker secrets are set (never their values)");

        group.MapPost("/", CreateAsync)
            .RequireStepUp()
            .WithName("CreateAccount")
            .WithSummary("Register a Dhan account (client id, write-only API key and secret, identity, roles and their settings); needs step-up")
            .ProducesRejections(conflict: true, notFound: false);

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetAccount")
            .WithSummary("One account with all settings (client id masked; secrets only as set / not set)")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}/connection", UpdateConnectionAsync)
            .RequireStepUp()
            .WithName("UpdateAccountConnection")
            .WithSummary("Change name, client id, colour and notes; needs step-up")
            .ProducesRejections(conflict: true);

        group.MapPut("/{id:guid}/secrets", UpdateSecretsAsync)
            .RequireStepUp()
            .WithName("UpdateAccountSecrets")
            .WithSummary("Replace the Dhan API key and/or secret (write-only; omitted values stay) and the key's issue date; needs step-up")
            .ProducesRejections(conflict: true);

        group.MapPut("/{id:guid}/roles", UpdateRolesAsync)
            .RequireStepUp()
            .WithName("UpdateAccountRoles")
            .WithSummary("Switch roles on or off and set the Data kind (primary or standby); needs step-up")
            .ProducesRejections(conflict: true);

        group.MapPost("/{id:guid}/make-primary-data", MakePrimaryDataAsync)
            .RequireStepUp()
            .WithName("MakePrimaryDataAccount")
            .WithSummary("Make the account the primary Data account (atomic swap; the previous primary becomes standby when possible); needs step-up")
            .ProducesRejections(conflict: true);

        group.MapPut("/{id:guid}/trading", UpdateTradingAsync)
            .RequireStepUp()
            .WithName("UpdateAccountTrading")
            .WithSummary("Replace the trading configuration (mode, risk profile, capital, styles, instruments, overrides); needs step-up. Switching from paper to manual_live runs the live-ready gate: 422 accounts.live_not_ready lists every failing item (the soft items need liveConfirmReason). Switching back to paper is never judged")
            .ProducesRejections(conflict: true);

        group.MapPut("/{id:guid}/portfolio", UpdatePortfolioAsync)
            .WithName("UpdateAccountPortfolio")
            .WithSummary("Save the portfolio settings (default bucket, include in totals, auto refresh off or daily at a time, first import date). When the account is connected, has the Portfolio role and has no holdings, every holding Dhan lists is imported in the same request, dated with the first import date (optional firstImport.rows override dates per key); the settings are saved even when the import fails (import.status failed, import.errorCode portfolio.import_failed). Answer: {account, import} (import always present)")
            .ProducesRejections(conflict: true);

        group.MapPut("/{id:guid}/data", UpdateDataAsync)
            .WithName("UpdateAccountData")
            .WithSummary("Change the Data settings (live-feed budget)")
            .ProducesRejections(conflict: true);

        group.MapDelete("/{id:guid}", ArchiveAsync)
            .RequireStepUp()
            .WithName("ArchiveAccount")
            .WithSummary("Remove an account (archive: history and audit trail are kept); needs step-up")
            .ProducesRejections(conflict: true);

        group.MapPost("/{id:guid}/connect", ConnectAsync)
            .RequireStepUp()
            .WithName("ConnectAccount")
            .WithSummary("Start the Dhan consent sign-in (at most 25 a day): returns the Dhan login page to open in the same tab; Dhan returns to /api/brokers/dhan/callback within 10 minutes; needs step-up")
            .ProducesValidationProblem()
            .ProducesBrokerRejections()
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapDelete("/{id:guid}/token", DisconnectAsync)
            .RequireStepUp()
            .WithName("DisconnectAccount")
            .WithSummary("Disconnect: delete the stored access token (local only; Dhan tokens end at their expiry); nothing to do when not connected; needs step-up")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/activity", GetActivityAsync)
            .WithName("GetAccountActivity")
            .WithSummary("Audit entries of the account, newest first")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Ok<IReadOnlyList<AccountSummary>>> ListAsync(
        IQueryHandler<ListAccountsQuery, IReadOnlyList<AccountSummary>> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new ListAccountsQuery(), cancellationToken));

    private static async Task<Results<Created<AccountDetail>, ProblemHttpResult>> CreateAsync(
        CreateAccountInput input,
        HttpContext http,
        ICommandHandler<CreateAccountCommand, AccountDetail> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new CreateAccountCommand(input, http.GetActor()), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Created($"/api/accounts/{result.Value.Id}", result.Value);
    }

    private static async Task<Results<Ok<AccountDetail>, ProblemHttpResult>> GetAsync(
        Guid id,
        HttpContext http,
        IQueryHandler<GetAccountQuery, Result<AccountDetail>> handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new GetAccountQuery(id), cancellationToken), http);

    private static async Task<Results<Ok<AccountDetail>, ProblemHttpResult>> UpdateConnectionAsync(
        Guid id,
        ConnectionInput input,
        HttpContext http,
        ICommandHandler<UpdateConnectionCommand, AccountDetail> handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new UpdateConnectionCommand(id, input, http.GetActor()), cancellationToken), http);

    private static async Task<Results<Ok<AccountDetail>, ProblemHttpResult>> UpdateSecretsAsync(
        Guid id,
        UpdateSecretsInput input,
        HttpContext http,
        ICommandHandler<UpdateSecretsCommand, AccountDetail> handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new UpdateSecretsCommand(id, input, http.GetActor()), cancellationToken), http);

    private static async Task<Results<Ok<AccountDetail>, ProblemHttpResult>> UpdateRolesAsync(
        Guid id,
        RolesInput input,
        HttpContext http,
        ICommandHandler<UpdateRolesCommand, AccountDetail> handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new UpdateRolesCommand(id, input, http.GetActor()), cancellationToken), http);

    private static async Task<Results<Ok<AccountDetail>, ProblemHttpResult>> MakePrimaryDataAsync(
        Guid id,
        HttpContext http,
        ICommandHandler<MakePrimaryDataCommand, AccountDetail> handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new MakePrimaryDataCommand(id, http.GetActor()), cancellationToken), http);

    private static async Task<Results<Ok<AccountDetail>, ProblemHttpResult>> UpdateTradingAsync(
        Guid id,
        TradingInput input,
        HttpContext http,
        ICommandHandler<UpdateTradingCommand, AccountDetail> handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new UpdateTradingCommand(id, input, http.GetActor()), cancellationToken), http);

    private static async Task<Results<Ok<PortfolioSaved>, ProblemHttpResult>> UpdatePortfolioAsync(
        Guid id,
        PortfolioInput input,
        HttpContext http,
        ICommandHandler<UpdatePortfolioCommand, PortfolioSaved> handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new UpdatePortfolioCommand(id, input, http.GetActor()), cancellationToken), http);

    private static async Task<Results<Ok<AccountDetail>, ProblemHttpResult>> UpdateDataAsync(
        Guid id,
        DataInput input,
        HttpContext http,
        ICommandHandler<UpdateDataCommand, AccountDetail> handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new UpdateDataCommand(id, input, http.GetActor()), cancellationToken), http);

    private static async Task<Results<NoContent, ProblemHttpResult>> ArchiveAsync(
        Guid id,
        HttpContext http,
        ICommandHandler<ArchiveAccountCommand> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ArchiveAccountCommand(id, http.GetActor()), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.NoContent();
    }

    private static async Task<Results<Ok<ConnectStartView>, ProblemHttpResult>> ConnectAsync(
        Guid id,
        HttpContext http,
        ICommandHandler<StartConnectCommand, ConnectStartView> handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new StartConnectCommand(id, http.GetVerifiedSession()!.SessionId, http.GetActor()), cancellationToken), http);

    private static async Task<Results<Ok<AccountDetail>, ProblemHttpResult>> DisconnectAsync(
        Guid id,
        HttpContext http,
        ICommandHandler<DisconnectCommand, AccountDetail> handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new DisconnectCommand(id, http.GetActor()), cancellationToken), http);

    private static async Task<Results<Ok<IReadOnlyList<AccountEventView>>, ProblemHttpResult>> GetActivityAsync(
        Guid id,
        [FromQuery] int? take,
        HttpContext http,
        IQueryHandler<GetAccountActivityQuery, Result<IReadOnlyList<AccountEventView>>> handler,
        CancellationToken cancellationToken) =>
        Respond(await handler.HandleAsync(new GetAccountActivityQuery(id, take ?? GetAccountActivityQuery.DefaultTake), cancellationToken), http);

    private static Results<Ok<T>, ProblemHttpResult> Respond<T>(Result<T> result, HttpContext http) =>
        result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
}
