using XDrishti.Domain.Learning;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>The joined rows of protocol X: <c>tf-1</c> values then the extension values, the width of the base part, the id of the training data and the label rule version.</summary>
public sealed record ExtensionTrainingData(IReadOnlyList<ValidationRow> Rows, int BaseWidth, string TrainingDataId, string LabelRuleVersion);
