namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>
/// One labelled announcement judged by the model: the label of the file and the class the model gave (P9-09 AC 7). <paramref name="BaselineMaterial"/> is the pre-registered category baseline's class (null: no baseline, so the set cannot pass), <paramref name="Weight"/> the inclusion
/// weight recomputed from the labelled rows of its stratum (P15-08; 0 = unknown, which the weighted precision reports as n/a), and <paramref name="Unstable"/> true when the repeats disagreed (the prediction is then <c>not material</c>).
/// </summary>
public sealed record AnnouncementRow(string Id, bool LabelMaterial, bool PredictedMaterial, bool? BaselineMaterial = null, double Weight = 0.0, bool Unstable = false);
