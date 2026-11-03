namespace XDrishti.Application.Abstractions.OptionQuality;

/// <summary>One newest stored rolling result with the id of the quality run that wrote it (P14-11: the run ids an extraction depended on are part of its record).</summary>
public sealed record RunQualityRow(OptionQualityRow Row, long RunId);
