namespace XDrishti.Application.Abstractions.Ai;

/// <summary>One look at the model server. <paramref name="Detail"/> comes from a fixed vocabulary (never an exception message, a URL or the key); the other text fields are null unless the server reported them.</summary>
public sealed record LlmStatus(LlmState State, string? Model, string? ModelFile, string? ModelSha256, string? Build, DateTimeOffset CheckedAt, string Detail);
