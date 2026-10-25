using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>The loss tags of the report population (P8-13). Read only.</summary>
/// <param name="Runs">Runs with their style and exit labels (1 to 50); empty means the default set of the reports, using the labels the runs record.</param>
public sealed record GetLossTagsQuery(IReadOnlyList<CellSource> Runs) : IQuery<Result<LossTagsView>>;
