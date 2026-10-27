namespace XDrishti.Application.Abstractions.Ai;

/// <summary>The read-only database could not be reached or refused the login (roles without a password, a wrong one, the server down). Carries no connection detail.</summary>
public sealed class AiDatabaseUnavailableException : Exception
{
    public AiDatabaseUnavailableException()
        : base("The read-only database is not available.")
    {
    }

    public AiDatabaseUnavailableException(string message)
        : base(message)
    {
    }

    public AiDatabaseUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
