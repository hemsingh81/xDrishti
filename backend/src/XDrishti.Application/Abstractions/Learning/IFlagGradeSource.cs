using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>Where protocol L reads the grade of the LLM announcement flag (P15-10): the set D result of the newest accepted eval run. Nothing implements it before P15-08: the registered default answers null (no grade), which is NOT_EVALUABLE, never a pass.</summary>
public interface IFlagGradeSource
{
    /// <summary>The newest accepted grade, or null.</summary>
    ValueTask<FlagGrade?> NewestAsync(CancellationToken cancellationToken);
}
