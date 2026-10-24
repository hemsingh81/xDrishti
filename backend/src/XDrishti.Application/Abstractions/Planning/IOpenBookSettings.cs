namespace XDrishti.Application.Abstractions.Planning;

/// <summary>Whether the owner has said the open book may be taken as empty (P7-09, ADR 0092). Off by default: the book is unavailable and the portfolio fill takes nothing. Changed only by editing the configuration, by no endpoint.</summary>
public interface IOpenBookSettings
{
    /// <summary>True to take the open book as empty (positions held elsewhere are not counted); false (the default) for unavailable.</summary>
    bool AssumedEmpty { get; }
}
