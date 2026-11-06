using XDrishti.Domain.Common;

namespace XDrishti.Application.Confidence;

/// <summary>The expected failures of the readiness scorecard and its gates (spec CF-11, ADR 0221). The codes are part of the API.</summary>
public static class ReadinessErrors
{
    /// <summary>The strategy or the version does not exist.</summary>
    public const string ScorecardNotFoundCode = "confidence.scorecard_not_found";

    /// <summary>The gate did not pass and the owner's enforce switch is ON (or could not be read, which is read as ON): the lifecycle move is refused.</summary>
    public const string GateFailedCode = "confidence.gate_failed";

    /// <summary>The wrong shape of a request.</summary>
    public const string InvalidCode = "confidence.readiness_invalid";

    public static Error ScorecardNotFound(string strategy, int? version) =>
        Error.NotFound(ScorecardNotFoundCode, version is null ? $"No strategy {strategy}." : $"No version {version} of strategy {strategy}.");

    public static Error GateFailed(string gate, string message) => Error.Conflict(GateFailedCode, $"Readiness gate {gate} refuses the move: {message}");

    public static Error Invalid(string field, string message) => new(InvalidCode, message, ErrorKind.Validation, Fields: [new FieldError(field, message)]);
}
