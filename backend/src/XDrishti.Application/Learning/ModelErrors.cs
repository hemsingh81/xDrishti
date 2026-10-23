using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>The expected failures of the model registry (P6-07).</summary>
public static class ModelErrors
{
    /// <summary>No model with this id.</summary>
    public const string NotFoundCode = "models.not_found";

    /// <summary>The verdict does not allow the role.</summary>
    public const string NotEligibleCode = "models.not_eligible";

    /// <summary>The model file is not there.</summary>
    public const string FileMissingCode = "models.file_missing";

    /// <summary>The model file does not hash to its name, or the name is not a hash.</summary>
    public const string FileCorruptCode = "models.file_corrupt";

    /// <summary>No model has this id.</summary>
    public static Error NotFound(long id) => Error.NotFound(NotFoundCode, $"No model {id} is registered.");

    /// <summary>Only an eligible model may be champion.</summary>
    public static Error NotEligible(long id, IReadOnlyList<string> reasons) =>
        Error.Conflict(NotEligibleCode, $"Model {id} may not be named champion: {string.Join(" ", reasons)}");

    /// <summary>The file of a model is missing.</summary>
    public static Error FileMissing(string sha256) => Error.NotFound(FileMissingCode, $"The file of model {Short(sha256)} is missing.");

    /// <summary>The file of a model no longer hashes to its name.</summary>
    public static Error FileCorrupt(string sha256) => Error.Conflict(FileCorruptCode, $"The file of model {Short(sha256)} does not hash to its name; it is not used.");

    private static string Short(string sha256) => sha256.Length > 12 ? sha256[..12] : sha256;
}
