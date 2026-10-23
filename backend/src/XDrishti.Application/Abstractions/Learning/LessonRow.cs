namespace XDrishti.Application.Abstractions.Learning;

/// <summary>A lesson row read back.</summary>
/// <param name="Id">The row id.</param>
/// <param name="Row">The row.</param>
public sealed record LessonRow(long Id, NewLessonRow Row)
{
    /// <summary>The id of the lesson: the opening row's id.</summary>
    public long LessonId => Row.LessonId ?? Id;
}
