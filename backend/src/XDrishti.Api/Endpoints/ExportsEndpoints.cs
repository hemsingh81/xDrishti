using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Api.Exports;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Capabilities;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// Saving a research table as a file (spec P8-20, ADR 0131): ONE route. It is a POST only because it writes an audit row (a GET must change nothing); it reads, produces bytes for the browser's download and keeps nothing. Order of the layers: the capability <c>exports</c> (OFF by
/// default, asked first, so that an OFF answer is always the 403 and never a 429), the session owner (and, by the CSRF middleware, the token), the rate rule, then the handler, which asks the gate again and calls the very handler of the screen. The audit row is written BEFORE the file is
/// given: when it cannot be written there is no file. The row and the log hold the kind, the format, the counts and the size, never a value.
/// </summary>
internal sealed class ExportsEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        api.MapGroup("/exports").WithTags("Exports")
            .MapPost("/{kind}", ExportAsync)
            .WithName("ExportTable")
            .WithSummary("Save one research table the Reports screens show (report-groups, report-pivot, report-what-works, report-equity, report-trades) as a CSV file: the same numbers as the GET of the same query, with the evidence block (basis with the charge-sheet confirmation if net, population rule, as-of, runs, unavailable inputs, limits, row count and hash) as # comment lines before the table and a closing line after it. A figure is three columns (number, state, reason); a non-value has no number. The file is never cut: a table over 100,000 rows or 25 MB is refused (413 exports.too_large) with the sentence to narrow the filters. Needs a session, the CSRF token and the capability exports (OFF by default; switching it ON asks the password), at most 6 a minute and one at a time. Body {format:'csv', query:{...the ReportQueryInput fields of the screen}}. The name of the file is made by the server (xd-<kind>-<yyyyMMdd>-<8 hex of the table hash>.csv). One audit row (ReportExported: kind, format, rows, bytes, how many filters) and no content in any log. 403 capabilities.off, 422 exports.kind_unknown, exports.format_unknown, reports.invalid and the other report refusals of the same query, 409 reports.run_overlap or reports.mixed_basis, 404 reports.not_found, 413 exports.too_large, 429 auth.rate_limited, 500 exports.audit_failed.")
            .Produces(StatusCodes.Status200OK, contentType: "text/csv")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesRejections(conflict: true);
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> ExportAsync(
        string kind,
        ExportRequest? request,
        HttpContext http,
        ICapabilityGate gate,
        ExportSlot slot,
        ICommandHandler<ExportCommand, ExportFile> handler,
        IAccountEventLog events,
        IAccountUnitOfWork unitOfWork,
        TimeProvider clock,
        ILogger<ExportsEndpoints> logger,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.XContentTypeOptions = "nosniff";
        if (await OwnerAct.RefusedWhileOffAsync(http, gate, Domain.Capabilities.Capabilities.Exports, cancellationToken) is { } off)
        {
            ExportLogEvents.Refused(logger, "capabilities.off");
            return off;
        }

        var owner = OwnerAct.SessionOwner(http);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        if (string.IsNullOrEmpty(owner.Value))
        {
            return Refused(Error.Forbidden("auth.forbidden", "No owner is signed in."), http, logger);
        }

        if (!slot.TryEnter())
        {
            ExportLogEvents.Refused(logger, SessionAuthentication.RateLimitedCode);
            return TypedResults.Problem(ProblemResults.Create(StatusCodes.Status429TooManyRequests, "At most 6 exports a minute, one at a time. Try again in a moment.", SessionAuthentication.RateLimitedCode, 10));
        }

        try
        {
            var started = clock.GetTimestamp();
            var made = await handler.HandleAsync(new ExportCommand(kind, request?.Format, request?.Query ?? new ReportQueryInput()), cancellationToken);
            if (made.IsFailure)
            {
                return Refused(made.Error, http, logger);
            }

            var file = made.Value;
            var detail = $"kind={file.Kind} format=csv rows={file.Rows} bytes={file.Content.Length} filters={file.Filters}";
            if (!await RecordAsync(events, unitOfWork, detail, owner.Value, clock))
            {
                ExportLogEvents.AuditFailed(logger, file.Kind);
                return TypedResults.Problem(ProblemResults.Create(StatusCodes.Status500InternalServerError, ExportErrors.AuditFailed().Message, ExportErrors.AuditFailedCode));
            }

            var ms = (long)clock.GetElapsedTime(started).TotalMilliseconds;
            ExportLogEvents.Done(logger, file.Kind, "csv", file.Rows, file.Content.Length, ms);
            return TypedResults.File(file.Content, "text/csv; charset=utf-8", file.FileName);
        }
        finally
        {
            slot.Leave();
        }
    }

    /// <summary>The audit row in its own unit of work; the token of the request is not used, so that a client that went away cannot leave a file made and unrecorded.</summary>
    private static async Task<bool> RecordAsync(IAccountEventLog events, IAccountUnitOfWork unitOfWork, string detail, string actor, TimeProvider clock)
    {
        try
        {
            events.Record(AccountEvent.Record(AccountEventType.ReportExported, null, detail, actor, clock.GetUtcNow()));
            return await unitOfWork.TrySaveChangesAsync(CancellationToken.None);
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
        {
            return false;
        }
    }

    /// <summary>The refusals of the exports are 422 (the report ones keep their own mapping), the cap is 413. Only the code is logged.</summary>
    private static ProblemHttpResult Refused(Error error, HttpContext http, ILogger logger)
    {
        ExportLogEvents.Refused(logger, error.Code);
        if (error.Code == ExportErrors.TooLargeCode)
        {
            return TypedResults.Problem(ProblemResults.Create(StatusCodes.Status413PayloadTooLarge, error.Message, error.Code));
        }

        return error.Kind == ErrorKind.Validation && (error.Code.StartsWith(ExportErrors.Prefix, StringComparison.Ordinal) || error.Code.StartsWith("reports.", StringComparison.Ordinal))
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code))
            : ProblemResults.From(error, http);
    }
}
