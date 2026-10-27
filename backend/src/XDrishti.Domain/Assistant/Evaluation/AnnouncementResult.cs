namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>Precision and recall of the material class with their Wilson intervals; with fewer rows than needed there is no verdict and <see cref="Note"/> says why.</summary>
public sealed record AnnouncementResult(SetState State, int Rows, int TruePositive, int FalsePositive, int FalseNegative, double? Precision, (double Low, double High)? PrecisionInterval, double? Recall, (double Low, double High)? RecallInterval, string? Note);
