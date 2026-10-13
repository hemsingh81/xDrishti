namespace XDrishti.Domain.Common;

/// <summary>
/// One rejected input value: which field (dotted path, camelCase like the JSON) and why. <see cref="Scope"/> names the thing the field belongs to when it is not the one being changed
/// (another service whose schedule the change would break) and <see cref="Code"/> is a stable machine-readable reason, for the requests that give one.
/// </summary>
public sealed record FieldError(string Field, string Message, string? Scope = null, string? Code = null);
