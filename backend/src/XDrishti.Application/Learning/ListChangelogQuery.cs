using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>The changelog, newest first (P6-08).</summary>
/// <param name="SubjectKind">cell or model, or null for both.</param>
/// <param name="Subject">One cell key or model:ID, or null.</param>
/// <param name="Limit">1 to 200.</param>
public sealed record ListChangelogQuery(string? SubjectKind, string? Subject, int Limit) : IQuery<Result<IReadOnlyList<ChangelogEntryView>>>;
