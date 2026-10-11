namespace XDrishti.Domain.Common;

/// <summary>One rejected input value: which field (dotted path, camelCase like the JSON) and why.</summary>
public sealed record FieldError(string Field, string Message);
