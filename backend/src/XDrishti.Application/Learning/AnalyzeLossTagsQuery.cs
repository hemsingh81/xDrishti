using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>Tags the losses of stored runs (P6-15): reads the closed trades into cells and applies the pure rules. Nothing is stored or applied.</summary>
/// <param name="Sources">One to 50 stored runs, one run per strategy, each with the style and exit label of its cell.</param>
/// <param name="Options">The thresholds.</param>
public sealed record AnalyzeLossTagsQuery(IReadOnlyList<CellSource> Sources, LossTagOptions Options) : IQuery<Result<LossTagReport>>;
