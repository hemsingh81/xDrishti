namespace XDrishti.Application.Abstractions.Learning;

/// <summary>The newest row of a lesson in a list, with when the lesson was opened (the opening row's time).</summary>
/// <param name="Latest">The newest row.</param>
/// <param name="OpenedAt">When the opening row was written.</param>
public sealed record LessonListed(LessonRow Latest, DateTimeOffset OpenedAt);
