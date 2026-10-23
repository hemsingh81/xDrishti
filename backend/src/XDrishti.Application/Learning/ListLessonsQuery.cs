using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>The newest view of each lesson, newest first (P6-16).</summary>
/// <param name="State">Only this state (open, linked, closed, dismissed), or null.</param>
/// <param name="Limit">1 to 200.</param>
public sealed record ListLessonsQuery(string? State, int Limit) : IQuery<Result<IReadOnlyList<LessonView>>>;
