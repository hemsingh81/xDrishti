namespace XDrishti.Api.Lab;

/// <summary>Body of <c>POST /api/replay/holdout-registrations</c> (SR-03): the scope (empty: the stored Auto scope), capital, risk profile, the checked charge sheet and the drawdown limit of the pass rule. Nulls are the defaults.</summary>
internal sealed record HoldOutRegistrationRequest(IReadOnlyList<Guid>? BasketIds = null, IReadOnlyList<Guid>? InstrumentIds = null, decimal? Capital = null, Guid? RiskProfileId = null, string? CostSheetId = null, double? MaxDrawdownPct = null);

/// <summary>Body of <c>POST /api/replay/holdout-registrations/{id}/run</c>: the owner's password, typed again for this act.</summary>
internal sealed record HoldOutPasswordRequest(string? Password = null);

/// <summary>Body of <c>PUT /api/replay/settings</c>: the password, typed again, and the switches to set (null leaves one as it is).</summary>
internal sealed record HoldOutAutoSettingsRequest(string? Password = null, bool? AutoRegister = null, bool? AutoRun = null);
