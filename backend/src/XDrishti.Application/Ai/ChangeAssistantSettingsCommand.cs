using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Ai;

/// <summary>
/// Changes the assistant's settings (P9-03): the owner's act only. The actor must be <c>owner:...</c> and comes from the signed-in identity, never from a body. <paramref name="PasswordVerified"/> says that the endpoint checked a fresh password for THIS change; a change that turns something on or raises it
/// is refused without it, a change that only lowers or keeps needs none. Every call appends a row; none is edited. The four write switches of P9-07/P9-08 are optional: absent means unchanged.
/// </summary>
public sealed record ChangeAssistantSettingsCommand(string Actor, string Reason, bool Enabled, bool PortfolioContext, int MaxToolCalls, long? ExpectedSettingsId, bool PasswordVerified, bool? AllowDraftConfirm = null, bool? AutoLabQuickTest = null, bool? AllowProposalAccept = null, bool? AutoAcceptHypothesis = null) : ICommand;
