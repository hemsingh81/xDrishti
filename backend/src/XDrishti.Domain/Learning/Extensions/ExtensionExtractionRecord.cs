namespace XDrishti.Domain.Learning.Extensions;

/// <summary>
/// The record of one extraction of an extension for a stored run (P15-05, ADR 0185): what the rows were built from, so P15-10 can tell when the inputs changed. One per (run key, extension, version); insert-only. <see cref="NanCountsJson"/> holds, per feature, how many values are NaN per
/// closed reason (<see cref="FeatureReasons"/>) and how many known values were built on facts of a synthetic parser.
/// </summary>
/// <param name="RunKey">The simulation run (64 lower-case hex).</param>
/// <param name="Extension">The extension id (<c>ev</c> or <c>fu</c>).</param>
/// <param name="Version">The extension version (<c>ev-1</c> or <c>fu-1</c>).</param>
/// <param name="LagMinutes">The lag applied to every broadcast and intimation time.</param>
/// <param name="ParserVersion">The parser version of the facts read; null for an extension that reads none.</param>
/// <param name="CalendarSha256">The hash of the results-calendar snapshot read (lower-case hex).</param>
/// <param name="FilingsWatermark">The newest broadcast time among the stored results filings read; null when there were none.</param>
/// <param name="Rows">Rows extracted.</param>
/// <param name="NanCountsJson">The counts as JSON.</param>
/// <param name="ExtractedAt">When the extraction ran.</param>
public sealed record ExtensionExtractionRecord(string RunKey, string Extension, string Version, int LagMinutes, string? ParserVersion, string CalendarSha256, DateTimeOffset? FilingsWatermark, int Rows, string NanCountsJson, DateTimeOffset ExtractedAt);
