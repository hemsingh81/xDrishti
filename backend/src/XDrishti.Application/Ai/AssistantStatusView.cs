namespace XDrishti.Application.Ai;

/// <summary>The state of the tool server as the status shows it: <c>ready</c>, <c>down</c> or <c>disabled</c> (the assistant is off, or no address and token are configured), how many tools it lists and the estimated tokens of their schemas.</summary>
public sealed record ToolsStatusView(string State, int Count, int SchemaTokensEstimate);

/// <summary>The result of the assistant's evaluation (P9-09): <c>not_yet_evaluated</c> until that slice writes one.</summary>
public sealed record EvaluationView(string State, DateTimeOffset? At, string? Detail);

/// <summary>What <c>GET /api/assistant/status</c> answers (spec P9-03 AC 9). No key, address or path in it.</summary>
public sealed record AssistantStatusView(bool Enabled, bool PortfolioContext, int MaxToolCalls, long? SettingsId, LlmStatusView Llm, ToolsStatusView Tools, bool Busy, bool NightlyRunning, EvaluationView Evaluation, bool AllowDraftConfirm = false, bool AutoLabQuickTest = false, bool AllowProposalAccept = false, bool AutoAcceptHypothesis = false);
