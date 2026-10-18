namespace XDrishti.Domain.Research;

/// <summary>How an entry limit order filled (P3-03b E3, E4).</summary>
public enum EntryFill : byte
{
    AtOpen = 0,
    AtLimit = 1,
}
