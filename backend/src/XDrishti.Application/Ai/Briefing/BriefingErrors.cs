using XDrishti.Domain.Common;

namespace XDrishti.Application.Ai.Briefing;

/// <summary>The expected failures of the briefing (spec P9-06 Contract).</summary>
public static class BriefingErrors
{
    public const string InvalidCode = "assistant.invalid";
    public const string NotFoundCode = "assistant.briefing_not_found";
    public const string NotEvaluableCode = "assistant.briefing_not_evaluable";
    public const string DeferredCode = "assistant.briefing_deferred";

    public static Error Invalid(string field, string message) => new(InvalidCode, message, ErrorKind.Validation, Fields: [new FieldError(field, message)]);

    public static Error NotFound(string message) => Error.NotFound(NotFoundCode, message);
}
