using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>The evaluations of one family. An unknown family is <c>extensions.invalid_query</c>.</summary>
public sealed record ListExtensionEvaluationsQuery(string? Family) : IQuery<Result<ExtensionEvaluationList>>;
