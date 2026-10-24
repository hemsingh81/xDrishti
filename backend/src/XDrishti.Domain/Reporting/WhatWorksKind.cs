namespace XDrishti.Domain.Reporting;

/// <summary>The two honest answers to "what works".</summary>
public enum WhatWorksKind
{
    /// <summary>At least one cell has the minimum sample; those are ranked.</summary>
    Ranked,

    /// <summary>No cell has enough evidence to rank: "nothing has enough evidence yet", with the counts. This is an answer, not an error.</summary>
    NothingQualifies,
}
