namespace XDrishti.Application.Reporting;

/// <summary>
/// Whether a net report can be made at all (P8-00 question 19): <c>value</c> when the owner's stored confirmation of the charge sheet exists, otherwise <c>unavailable</c> with the reason. The confirmation is read from storage; a request cannot supply it.
/// </summary>
/// <param name="State">value or unavailable.</param>
/// <param name="Reason">Why net figures are unavailable; null when they are not.</param>
/// <param name="Confirmed">What the owner confirmed, as text; null when nothing is confirmed.</param>
public sealed record NetBasisView(string State, string? Reason, string? Confirmed);
