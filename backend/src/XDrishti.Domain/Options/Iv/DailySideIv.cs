namespace XDrishti.Domain.Options.Iv;

/// <summary>The offset-0 IV of one side of one series at the last bar of one India session (P14-11): the vendor value as stored (null when the bar carries none) and whether the bar's key-session passed the archive quality checks.</summary>
public readonly record struct DailySideIv(DateOnly Date, int Code, string Side, double? VendorIv, bool Usable);
