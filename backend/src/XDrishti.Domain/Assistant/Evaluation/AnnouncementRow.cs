namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>One labelled announcement judged by the model: the label of the file and the class the model gave. Only the class matters to the grader (spec P9-09 AC 7).</summary>
public sealed record AnnouncementRow(string Id, bool LabelMaterial, bool PredictedMaterial);
