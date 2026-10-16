namespace XDrishti.Strategies;

/// <summary>How serious a <see cref="Problem"/> is: an error stops the compile, a warning and an information do not.</summary>
public enum ProblemSeverity
{
    /// <summary>The condition cannot be used.</summary>
    Error = 0,

    /// <summary>The condition works but probably does not do what its author meant.</summary>
    Warning = 1,

    /// <summary>A fact the caller should act on that is not a fault (for example that the frame needs a market context).</summary>
    Information = 2,
}
