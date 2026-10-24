using XDrishti.Domain.Common;

namespace XDrishti.Domain.Reporting;

/// <summary>The evidence of a whole report: the population block, the limits that always apply, the inputs that do not exist and how many repeated records were dropped. A screen shows these fields, not its own prose.</summary>
/// <param name="Population">The block over every trade of the population.</param>
/// <param name="Limits">The honest limits, as text.</param>
/// <param name="UnavailableInputs">Inputs a figure would need that do not exist, each with the reason.</param>
/// <param name="DuplicateTradesDropped">Identical records of one trade in one run, kept once.</param>
/// <param name="NetBasisConfirmation">What the owner confirmed, for a net report; null for a gross one.</param>
public sealed record ReportEvidence(EvidenceBlock Population, ValueList<string> Limits, ValueList<string> UnavailableInputs, int DuplicateTradesDropped, ChargeSheetConfirmation? NetBasisConfirmation = null);
