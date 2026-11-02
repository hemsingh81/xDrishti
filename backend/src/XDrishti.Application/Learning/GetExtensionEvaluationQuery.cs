using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>One evaluation by id. Unknown is <c>extensions.not_found</c>.</summary>
public sealed record GetExtensionEvaluationQuery(string Id) : IQuery<Result<ExtensionEvaluationView>>;
