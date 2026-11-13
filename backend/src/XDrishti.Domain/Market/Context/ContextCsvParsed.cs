namespace XDrishti.Domain.Market.Context;

/// <summary>The result of reading one CSV: the accepted rows, the refused rows, and a file-level refusal code (null when the file was readable at all).</summary>
public sealed record ContextCsvParsed(ContextCsvKind Kind, IReadOnlyList<ContextCsvRow> Rows, IReadOnlyList<ContextCsvReject> Rejects, string? FileReject);
