namespace XDrishti.Application.Abstractions.Ai;

/// <summary>The model server failed or refused a request. Carries no address, key or response text.</summary>
public sealed class ModelUnavailableException : Exception
{
    public ModelUnavailableException()
        : base("The model server did not answer.")
    {
    }

    public ModelUnavailableException(string message)
        : base(message)
    {
    }

    public ModelUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
