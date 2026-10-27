using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Ai;

/// <summary>Whether a change to these values turns something on or raises something relative to the settings in force, so that it needs the owner's fresh password (spec P9-03 AC 8). Reads only.</summary>
public sealed record AssistantSettingsNeedPasswordQuery(bool Enabled, bool PortfolioContext, int MaxToolCalls, bool? AllowDraftConfirm = null, bool? AutoLabQuickTest = null, bool? AllowProposalAccept = null, bool? AutoAcceptHypothesis = null) : IQuery<bool>;
