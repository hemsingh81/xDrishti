namespace XDrishti.Api.Endpoints;

/// <summary>
/// The body of <c>PUT /api/assistant/settings</c>. The actor is never in it (it comes from the session). <paramref name="Password"/> is the owner's password for THIS change; it is needed when the change turns something on or raises it, and is not needed to switch something off or lower it.
/// </summary>
/// <param name="Enabled">The assistant answers.</param>
/// <param name="PortfolioContext">The portfolio views show data to the tools.</param>
/// <param name="MaxToolCalls">The budget of tool calls per question, 1 to 12.</param>
/// <param name="Reason">Why, 1 to 500 characters.</param>
/// <param name="ExpectedSettingsId">The row the form was loaded at (0 when none was stored); a stale form is a 409.</param>
/// <param name="Password">The owner's password, for a change that raises exposure.</param>
internal sealed record AssistantSettingsRequest(bool Enabled, bool PortfolioContext, int MaxToolCalls, string? Reason, long? ExpectedSettingsId, string? Password);
