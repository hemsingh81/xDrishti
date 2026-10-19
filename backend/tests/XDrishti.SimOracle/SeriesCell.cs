namespace XDrishti.SimOracle;

/// <summary>One minute slot of a generated series.</summary>
internal sealed class SeriesCell
{
    internal long Epoch { get; init; }

    internal DateOnly Date { get; init; }

    internal int Minute { get; init; }

    internal bool Present { get; set; } = true;

    internal long Volume { get; set; }

    internal decimal Open { get; set; }

    internal decimal High { get; set; }

    internal decimal Low { get; set; }

    internal decimal Close { get; set; }
}
