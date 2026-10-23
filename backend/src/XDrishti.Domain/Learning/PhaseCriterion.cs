namespace XDrishti.Domain.Learning;

/// <summary>One exit criterion with the call and the numbers behind it (P6-18).</summary>
/// <param name="Number">1 to 3.</param>
/// <param name="Name">The criterion in the roadmap's words.</param>
/// <param name="Verdict">The call.</param>
/// <param name="Detail">The numbers and the reason, one line.</param>
/// <param name="Caveat">What a MET rests on without being verified here, printed beside the verdict; null when there is none.</param>
public sealed record PhaseCriterion(int Number, string Name, PhaseVerdict Verdict, string Detail, string? Caveat = null);
