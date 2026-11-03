using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Application.Abstractions.OptionIv;

/// <summary>The <c>iv-1</c> cells of every trade by trade key, in the order of <see cref="IvFeatureCatalog.Names"/>, and what they were built from.</summary>
public sealed record IvExtraction(IReadOnlyDictionary<string, FeatureCell[]> Cells, IvSources Sources);
