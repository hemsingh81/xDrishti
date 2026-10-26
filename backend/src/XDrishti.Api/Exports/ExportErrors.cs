using System.Globalization;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Exports;

/// <summary>The refusals of an export that are its own (the report refusals pass through with their own codes). Codes starting <c>exports.</c> are answered 422, except <see cref="TooLargeCode"/> (413) and <see cref="AuditFailedCode"/> (500).</summary>
internal static class ExportErrors
{
    public const string KindUnknownCode = "exports.kind_unknown";
    public const string FormatUnknownCode = "exports.format_unknown";
    public const string TooLargeCode = "exports.too_large";
    public const string ChangedCode = "exports.changed";
    public const string AuditFailedCode = "exports.audit_failed";
    public const string Prefix = "exports.";

    public static Error KindUnknown() => Error.Validation(KindUnknownCode, "That table cannot be exported. The tables are: " + string.Join(", ", ExportKinds.All) + ".");

    public static Error FormatUnknown() => Error.Validation(FormatUnknownCode, "The only format is csv.");

    public static Error TooLarge(long rows, long maxRows, long maxBytes) =>
        Error.Validation(TooLargeCode, string.Create(CultureInfo.InvariantCulture, $"This table is larger than an export holds (at most {maxRows:N0} rows and {maxBytes / 1024:N0} KB; this one has {rows:N0} rows or more). Narrow the filters. Nothing was cut and no file was made."));

    public static Error Changed() => Error.Conflict(ChangedCode, "The stored runs changed while the file was being made, so it was not given. Try again.");

    public static Error AuditFailed() => Error.Validation(AuditFailedCode, "The export was not recorded, so no file was given. Try again.");
}
