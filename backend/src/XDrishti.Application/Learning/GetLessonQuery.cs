using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>One lesson with its history (P6-16).</summary>
/// <param name="Id">The lesson id.</param>
public sealed record GetLessonQuery(long Id) : IQuery<Result<LessonView>>;
