using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Api.Exports;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Capabilities;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The one way data leaves the app as a file (spec P8-20, ADR 0131): the CSV exports and the Lab's report downloads all pass here, in this order. The capability <c>exports</c> is asked FIRST (OFF is always the 403, never a rate answer); the owner of the session; the rate
/// rule (6 starts a minute, one at a time; a refused start still counts); the gate again; the file is made; the audit row is written BEFORE the bytes are given (no row, no file); then the bytes. The row and the log hold the kind, the format and counts, never a value.
/// </summary>
internal static class ExportDelivery
{
    public static async Task<Results<FileContentHttpResult, ProblemHttpResult>> DeliverAsync(
        HttpContext http,
        ICapabilityGate gate,
        ExportSlot slot,
        IAccountEventLog events,
        IAccountUnitOfWork unitOfWork,
        TimeProvider clock,
        ILogger logger,
        Func<ValueTask<Result<ExportFile>>> make,
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
            var open = await gate.RequireAsync(Domain.Capabilities.Capabilities.Exports, cancellationToken);
            if (open.IsFailure)
            {
                return Refused(open.Error, http, logger);
            }

            var started = clock.GetTimestamp();
            var made = await make();
            if (made.IsFailure)
            {
                return Refused(made.Error, http, logger);
            }

            var file = made.Value;
            var detail = $"kind={file.Kind} format={file.Format} rows={file.Rows} bytes={file.Content.Length} filters={file.Filters}";
            if (!await RecordAsync(events, unitOfWork, detail, owner.Value, clock))
            {
                ExportLogEvents.AuditFailed(logger, file.Kind);
                return TypedResults.Problem(ProblemResults.Create(StatusCodes.Status500InternalServerError, ExportErrors.AuditFailed().Message, ExportErrors.AuditFailedCode));
            }

            var ms = (long)clock.GetElapsedTime(started).TotalMilliseconds;
            ExportLogEvents.Done(logger, file.Kind, file.Format, file.Rows, file.Content.Length, ms);
            return TypedResults.File(file.Content, file.ContentType, file.Inline ? null : file.FileName);
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
