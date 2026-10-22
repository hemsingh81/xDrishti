using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>The expected failures of the learning use cases (P6-01).</summary>
public static class LearningErrors
{
    /// <summary>The command is not usable.</summary>
    public const string InvalidCode = "learning.invalid";

    /// <summary>A named run is not stored.</summary>
    public const string RunNotFoundCode = "learning.run_not_found";

    /// <summary>Two sources made the same cell.</summary>
    public const string CellDuplicateCode = "learning.cell_duplicate";

    /// <summary>A cell has trades with and without charges.</summary>
    public const string MixedBasisCode = "learning.mixed_basis";

    /// <summary>No cell with this key is stored.</summary>
    public const string CellNotFoundCode = "learning.cell_not_found";

    /// <summary>The problems found before anything was read.</summary>
    public static Error Invalid(IReadOnlyList<FieldError> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return new Error(InvalidCode, "The request cannot be used: " + string.Join("; ", fields.Select(f => f.Field + ": " + f.Message)), ErrorKind.Validation, Fields: fields);
    }

    /// <summary>A named run is not in the result store.</summary>
    public static Error RunNotFound(string runKey) => Error.NotFound(RunNotFoundCode, $"No run {runKey[..Math.Min(12, runKey.Length)]} is stored.");

    /// <summary>Two runs gave the same cell; their trades would be counted twice.</summary>
    public static Error CellDuplicate(string cellKey) => Error.Conflict(CellDuplicateCode, $"Two sources make the cell {cellKey}; name one run per cell.");

    /// <summary>Two runs hold the same strategy; the strategy-level prior would count its signals twice.</summary>
    public static Error StrategyDuplicate(string strategy) => Error.Conflict(CellDuplicateCode, $"Two sources hold the strategy {strategy}; name one run per strategy.");

    /// <summary>One cell would mix net-of-charges and gross R.</summary>
    public static Error MixedBasis(string cellKey) => Error.Conflict(MixedBasisCode, $"The cell {cellKey} mixes trades with and without charges; use runs costed the same way.");

    /// <summary>No stored cell has the key.</summary>
    public static Error CellNotFound(string cellKey) => Error.NotFound(CellNotFoundCode, $"No stored cell {cellKey}.");
}
