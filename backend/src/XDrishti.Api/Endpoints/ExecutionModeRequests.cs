namespace XDrishti.Api.Endpoints;

/// <summary>The owner's request for the next mode. The actor is never taken from the body.</summary>
internal sealed record PromotionRequestBody(string? ToMode, string? Reason, string? Password);

/// <summary>The owner's confirmation: the password is checked afresh for this act.</summary>
internal sealed record ConfirmPromotionBody(string? Password);

/// <summary>The owner's step down.</summary>
internal sealed record DemotionBody(string? ToMode, string? Reason);
