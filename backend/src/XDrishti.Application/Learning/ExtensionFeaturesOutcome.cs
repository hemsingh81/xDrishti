namespace XDrishti.Application.Learning;

/// <summary>What an extension extraction did (P15-05): the trades of the run, the rows stored now, how many values are unknown, and whether the extraction had been done before (then nothing was stored).</summary>
public sealed record ExtensionFeaturesOutcome(string Version, int Trades, int Stored, int UnknownValues, bool AlreadyExtracted);
