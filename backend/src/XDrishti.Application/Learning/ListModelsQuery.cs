using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>Lists registered models, newest first (P6-07).</summary>
/// <param name="Role">Only models whose current role is this (challenger, champion, retired); null for all.</param>
/// <param name="Limit">1 to 200.</param>
public sealed record ListModelsQuery(string? Role, int Limit) : IQuery<Result<IReadOnlyList<ModelView>>>;
