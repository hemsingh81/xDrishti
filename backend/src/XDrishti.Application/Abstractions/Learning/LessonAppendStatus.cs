namespace XDrishti.Application.Abstractions.Learning;

/// <summary>What a lesson append did.</summary>
public enum LessonAppendStatus
{
    /// <summary>The row was written.</summary>
    Created,

    /// <summary>An opening matched a lesson already recorded for the same monitor, subject and evidence; nothing was written.</summary>
    Existing,

    /// <summary>The lesson moved meanwhile; nothing was written.</summary>
    Stale,
}
