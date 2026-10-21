using XDrishti.SimReader;

namespace XDrishti.Api.Simulate.Assist;

/// <summary>The stored 1-minute bars of one instrument and one India date, with the master tick in rupees. <see cref="Bars"/> is empty when the instrument or the day is not stored.</summary>
internal sealed record StoredDay(decimal Tick, IReadOnlyList<ReaderBar> Bars, bool TickFallback = false);

/// <summary>What a second source gave for one (symbol, date): the bars, or the reason there are none (<see cref="Unavailable"/> is set exactly when <see cref="Bars"/> is null).</summary>
internal sealed record SecondDay(IReadOnlyList<ReaderBar>? Bars, string? Unavailable)
{
    public static SecondDay Missing(string reason) => new(null, reason);
}

/// <summary>The stored bars (read from the database by the host; a fake in tests).</summary>
internal interface IStoredDays
{
    ValueTask<StoredDay> GetAsync(string symbol, DateOnly date, CancellationToken cancellationToken);
}

/// <summary>A second source of 1-minute bars (spec P3-09c D1, D2). Never throws for a source problem: it returns <see cref="SecondDay.Missing"/>.</summary>
internal interface ISecondSource
{
    /// <summary>The label written to the log as <c>auto:&lt;Name&gt;</c>.</summary>
    string Name { get; }

    ValueTask<SecondDay> GetAsync(string symbol, DateOnly date, CancellationToken cancellationToken);
}

/// <summary>The files of the pack folder the assisted check touches. Reads and writes are by file name; the order of calls is what the blindness test records.</summary>
internal interface IPackFolder
{
    /// <summary>The bytes of the file, or null when it is not there.</summary>
    byte[]? Read(string name);

    void Write(string name, byte[] bytes);
}
