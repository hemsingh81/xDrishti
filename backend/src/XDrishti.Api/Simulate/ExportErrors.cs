using XDrishti.Domain.Common;

namespace XDrishti.Api.Simulate;

/// <summary>
/// The refusals of the folder writer (spec P3-04b1, E7). A refusal is an expected failure and returns a <see cref="Result"/>; an I/O failure is an exception. <see cref="RootInvalid"/> is a configuration error (the verb exits 2); every
/// other code is a refusal (exit 1).
/// </summary>
internal static class ExportErrors
{
    public const string RootInvalidCode = "export.root_invalid";

    public const string PathInvalidCode = "export.path_invalid";

    public const string LinkRefusedCode = "export.link_refused";

    public const string NoValidManifestCode = "export.no_valid_manifest";

    public const string ExistsDifferentCode = "export.exists_different";

    public const string PreviousExistsCode = "export.previous_exists";

    public static Error RootInvalid(string reason) =>
        Error.Validation(RootInvalidCode, "The export directory is not usable: " + reason + ". It must be an absolute path whose parent folder exists.");

    public static Error PathInvalid(string reason) => Error.Validation(PathInvalidCode, "A name for the export is not allowed: " + reason + ".");

    public static Error LinkRefused(string folder) =>
        Error.Validation(LinkRefusedCode, "The folder " + folder + " is a symbolic link; the export is never written through one.");

    public static Error NoValidManifest(string folder, string reason) =>
        Error.Conflict(NoValidManifestCode, "The folder " + folder + " exists and does not hold a valid manifest (" + reason + "); it is never replaced, not even with force. Move it away by hand.");

    public static Error ExistsDifferent(string folder) =>
        Error.Conflict(ExistsDifferentCode, "The folder " + folder + " holds a different or modified export; run again with force to keep it as a .previous copy and write the new one.");

    public static Error PreviousExists(string previous) =>
        Error.Conflict(PreviousExistsCode, "The folder " + previous + " already exists; remove it by hand before forcing another replacement (one previous copy per run is kept and nothing is deleted for you).");
}
