namespace XDrishti.Strategies.Compilation;

/// <summary>
/// The three values of a condition (spec P3-02, <i>Three-valued logic</i>). A comparison, <c>crosses_*</c>, <c>rising</c>, <c>falling</c>, <c>between</c> or <c>time</c> whose data is not ready is
/// <see cref="Unknown"/>, not <see cref="False"/>; <c>and</c>, <c>or</c> and <c>not</c> follow Kleene's tables (<c>false and unknown</c> is false, <c>true or unknown</c> is true, <c>not unknown</c> is unknown). A
/// decision (<see cref="BoundCondition.Evaluate"/>) is true only for <see cref="True"/>. The numbers are the ones the compiled code works with: <c>(Truth)(-1)</c> is unknown.
/// </summary>
public enum Truth : sbyte
{
    /// <summary>The condition does not hold; every operand it needed was ready (or another part decided it).</summary>
    False = 0,

    /// <summary>The condition holds.</summary>
    True = 1,

    /// <summary>The condition cannot be decided yet: an operand it needed is not ready (NaN, not enough history, no market context for the date).</summary>
    Unknown = -1,
}
