namespace XDrishti.Application.Abstractions.Ai;

/// <summary>How an ad-hoc query ended.</summary>
public enum SqlRunStatus
{
    Ok,

    /// <summary>The database refused the privilege (42501): a table, schema or function the role may not touch.</summary>
    Denied,

    /// <summary>The statement timeout or the overall guard stopped it.</summary>
    Timeout,

    /// <summary>Any other refusal of the database (a syntax error, an unknown column, a refused construct); <see cref="SqlRunOutcome.ErrorText"/> says which.</summary>
    Error,

    /// <summary>The database could not be reached.</summary>
    Unavailable,
}
