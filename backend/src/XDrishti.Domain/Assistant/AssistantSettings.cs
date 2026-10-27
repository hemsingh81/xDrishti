namespace XDrishti.Domain.Assistant;

/// <summary>
/// The assistant's switches in force (spec P9-03, ADR 0137, 0138): the newest row of <c>ai.settings</c>. With no row the assistant is OFF and sees no portfolio data (<see cref="Defaults"/>, fail closed) and <see cref="Id"/> is null.
/// </summary>
/// <param name="Id">The row id, or null when nothing was ever stored.</param>
/// <param name="Enabled">Whether the assistant answers at all.</param>
/// <param name="PortfolioContext">Whether the portfolio views show data to the tools (only effective while <paramref name="Enabled"/>).</param>
/// <param name="MaxToolCalls">The most tool calls of one turn, 1 to 12.</param>
public sealed record AssistantSettings(long? Id, bool Enabled, bool PortfolioContext, int MaxToolCalls)
{
    /// <summary>The design default budget of tool calls per turn.</summary>
    public const int DefaultMaxToolCalls = 6;

    /// <summary>The fewest tool calls that can be set.</summary>
    public const int LowestMaxToolCalls = 1;

    /// <summary>The most tool calls that can be set.</summary>
    public const int HighestMaxToolCalls = 12;

    /// <summary>What applies when nothing is stored: off, no portfolio.</summary>
    public static AssistantSettings Defaults { get; } = new(null, false, false, DefaultMaxToolCalls);

    /// <summary>Whether the budget is inside the range the database also checks.</summary>
    public static bool ValuesAreValid(int maxToolCalls) => maxToolCalls is >= LowestMaxToolCalls and <= HighestMaxToolCalls;
}
