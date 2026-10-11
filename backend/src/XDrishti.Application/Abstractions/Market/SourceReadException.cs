namespace XDrishti.Application.Abstractions.Market;

/// <summary>A source file could not be read to its end (too large, too slow, or the connection broke). The message is safe to show to the owner.</summary>
public sealed class SourceReadException : IOException
{
    public SourceReadException()
        : this("The download was interrupted.")
    {
    }

    public SourceReadException(string message)
        : base(message)
    {
    }

    public SourceReadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
