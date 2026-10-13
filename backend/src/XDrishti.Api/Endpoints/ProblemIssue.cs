namespace XDrishti.Api.Endpoints;

/// <summary>One finding in the <c>issues</c> of a <c>services.invalid</c> problem: the service it is about (null: the service of the request), the field, the severity (error), a stable code and the text.</summary>
internal sealed record ProblemIssue(string? ServiceId, string Field, string Severity, string Code, string Message);
