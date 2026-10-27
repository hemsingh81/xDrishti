namespace XDrishti.Application.Ai;

/// <summary>What <c>GET /api/assistant/llm-status</c> answers. No key, no path and no address of the server is ever in it.</summary>
/// <param name="State">ready, loading, down or misconfigured.</param>
/// <param name="Model">The alias the server serves (for example xd-main), when it answered.</param>
/// <param name="ModelFile">Base name of the loaded model file, when the server said.</param>
/// <param name="ModelSha256">SHA-256 of that file when it is one of the pinned files; null for an unknown file.</param>
/// <param name="Build">The server's build number text, when it said.</param>
/// <param name="CheckedAt">When the answer was taken (cached for a few seconds).</param>
/// <param name="Detail">A fixed sentence.</param>
/// <param name="StartCommand">How the owner starts the server.</param>
public sealed record LlmStatusView(string State, string? Model, string? ModelFile, string? ModelSha256, string? Build, DateTimeOffset CheckedAt, string Detail, string StartCommand);
