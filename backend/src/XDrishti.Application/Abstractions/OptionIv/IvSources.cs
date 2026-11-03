namespace XDrishti.Application.Abstractions.OptionIv;

/// <summary>What an <c>iv-1</c> extraction depended on besides the trades: the highest finished fetch-log id over the ranges read (the archive watermark), the ids of the quality runs that decided which key-sessions were usable, and the IV unit label; <see cref="NiftyPresent"/> says whether NIFTY has any series in the archive (without it no trade has a market-wide rank).</summary>
public sealed record IvSources(long ArchiveWatermark, IReadOnlyList<long> QualityRunIds, string IvUnit, bool NiftyPresent = true);
