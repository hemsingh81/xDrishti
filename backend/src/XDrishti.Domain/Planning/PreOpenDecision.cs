namespace XDrishti.Domain.Planning;

/// <summary>The pre-open verdict on one approved ticket (spec P7-11): arm it, or invalidate it with a stable code and the numbers.</summary>
/// <param name="Arm">True to arm (watch with alerts); false to invalidate.</param>
/// <param name="Code">A <see cref="PreOpenCodes"/> value.</param>
/// <param name="Detail">The numbers or the missing input, in words.</param>
public sealed record PreOpenDecision(bool Arm, string Code, string Detail);
