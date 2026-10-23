using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>One hypothesis with its history (P6-10).</summary>
/// <param name="Id">The hypothesis id.</param>
public sealed record GetHypothesisQuery(long Id) : IQuery<Result<HypothesisView>>;
