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
/// <param name="AllowDraftConfirm">The owner may confirm a strategy draft (P9-07); absent means unchanged.</param>
/// <param name="AutoLabQuickTest">A valid draft of the dedicated drafting job may be queued for a quick test without a confirm (P9-07 D9); absent means unchanged.</param>
/// <param name="AllowProposalAccept">Used by P9-08; absent means unchanged.</param>
/// <param name="AutoAcceptHypothesis">Used by P9-08; absent means unchanged.</param>
internal sealed record AssistantSettingsRequest(bool Enabled, bool PortfolioContext, int MaxToolCalls, string? Reason, long? ExpectedSettingsId, string? Password, bool? AllowDraftConfirm = null, bool? AutoLabQuickTest = null, bool? AllowProposalAccept = null, bool? AutoAcceptHypothesis = null);
