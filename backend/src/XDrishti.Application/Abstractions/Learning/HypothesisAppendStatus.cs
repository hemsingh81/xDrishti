namespace XDrishti.Application.Abstractions.Learning;

/// <summary>What an append did.</summary>
public enum HypothesisAppendStatus
{
    /// <summary>The row was written.</summary>
    Created,

    /// <summary>A proposal was refused: the open limit is reached.</summary>
    OpenLimit,

    /// <summary>The hypothesis moved meanwhile; nothing was written.</summary>
    Stale,
}
