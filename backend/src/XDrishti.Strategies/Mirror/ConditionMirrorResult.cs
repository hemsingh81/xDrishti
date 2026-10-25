using XDrishti.Strategies.Syntax;

namespace XDrishti.Strategies.Mirror;

/// <summary>
/// What mirroring one condition gives: the mirrored <see cref="Tree"/> and its canonical <see cref="Text"/>, or the <see cref="Problem"/> that says why there is no single mirror. <see cref="Unreadable"/> is true when the
/// condition does not parse or uses a name or shape the language itself refuses: there is no mirror problem to report then, because the compile of the original reports its own.
/// </summary>
public sealed record ConditionMirrorResult(Expression? Tree, string? Text, Problem? Problem, bool Unreadable)
{
    /// <summary>True when the condition was mirrored.</summary>
    public bool IsSuccess => Tree is not null;
}
