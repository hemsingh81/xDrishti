using XDrishti.Domain.Common;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Platform.Services;

/// <summary>The expected failures of the services use cases (codes are part of the API).</summary>
public static class ServiceErrors
{
    public const string NotFoundCode = "services.not_found";
    public const string InvalidCode = "services.invalid";
    public const string RevisionChangedCode = "services.revision_changed";
    public const string AlreadyRunningCode = "services.already_running";
    public const string ReadOnlyCode = "services.read_only";

    public static readonly Error NotFound = Error.NotFound(NotFoundCode, "There is no such service.");

    public static readonly Error RevisionChanged = Error.Conflict(RevisionChangedCode, "The service was changed elsewhere. Reload it and try again.");

    public static readonly Error AlreadyRunning = Error.Conflict(AlreadyRunningCode, "The service is running already.");

    public static readonly Error ReadOnly = Error.Conflict(ReadOnlyCode, "This service is set elsewhere (under Accounts) and cannot be changed here.");

    /// <summary>A refused schedule: one field error per error issue, named with its field; those of another service carry its id as the scope.</summary>
    public static Error Invalid(IEnumerable<ScheduleIssue> errors, string serviceId)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var fields = errors.Where(i => i.IsError).Select(i => new FieldError(i.Field, i.Message, i.ServiceId == serviceId ? null : i.ServiceId, i.Code)).ToArray();
        return Error.Validation(InvalidCode, "The schedule is not valid.") with { Fields = fields };
    }

    public static Error InvalidField(string field, string message) =>
        Error.Validation(InvalidCode, "The request is not valid.") with { Fields = [new FieldError(field, message)] };
}
