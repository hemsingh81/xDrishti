namespace XDrishti.Application.Abstractions.Learning;

/// <summary>The stored text of a strategy version by its hash (spec P8-18 Part B): the replay of a stored strategy needs the text and nothing else. Reads only.</summary>
public interface IReplayStrategySource
{
    /// <summary>The source text of the stored version with this hash, or null.</summary>
    ValueTask<string?> StoredSourceAsync(string hash, CancellationToken cancellationToken);
}

/// <summary>Whether another heavy job (a Lab run) is running now: the replay time is printed with that, because it is measured on one machine at one moment.</summary>
public interface IHeavyJobProbe
{
    ValueTask<bool> AnyOtherRunningAsync(CancellationToken cancellationToken);
}
