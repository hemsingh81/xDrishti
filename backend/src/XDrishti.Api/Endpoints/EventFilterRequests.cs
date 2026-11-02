namespace XDrishti.Api.Endpoints;

/// <summary>The owner's change of the event-risk filter. The password is needed for every change. The actor is never taken from the body.</summary>
internal sealed record EventFilterChangeBody(bool? Enabled, int? ResultsWithinDays, string? Approval, string? Reason, string? Password);
