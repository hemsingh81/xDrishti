using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Learning;

/// <summary>Makes the validation rows of a <see cref="TrainingSet"/> (P6-05).</summary>
public static class ValidationRows
{
    /// <summary>One row per label of the set, in the order of its labels; a label without context is a failure.</summary>
    public static Result<IReadOnlyList<ValidationRow>> From(TrainingSet set, IReadOnlyDictionary<string, ValidationContext> contexts)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(contexts);
        var rows = new List<ValidationRow>(set.Labels.Count);
        foreach (var label in set.Labels)
        {
            if (!contexts.TryGetValue(label.Key, out var context))
            {
                return Result.Failure<IReadOnlyList<ValidationRow>>(Error.Validation("validation.no_context", "No trade date or cell was given for " + label.Key + "."));
            }

            rows.Add(ValidationRow.From(label, context));
        }

        return Result.Success<IReadOnlyList<ValidationRow>>(rows);
    }
}
