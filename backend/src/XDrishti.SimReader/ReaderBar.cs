namespace XDrishti.SimReader;

/// <summary>One 1-minute bar. <paramref name="EpochSeconds"/> is the bar START (UTC seconds; India time is UTC+5:30).</summary>
public sealed record ReaderBar(long EpochSeconds, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);
