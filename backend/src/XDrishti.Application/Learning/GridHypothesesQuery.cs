using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>The deterministic grid values around a parameter of a stored baseline run (P6-10). Nothing is written.</summary>
/// <param name="BaselineRunKey">The stored run.</param>
/// <param name="Parameter">The parameter's name.</param>
/// <param name="Grid">The grid rules.</param>
public sealed record GridHypothesesQuery(string BaselineRunKey, string Parameter, HypothesisGrid Grid) : IQuery<Result<GridHypothesesView>>;
