namespace XDrishti.Domain.Market.Context;

/// <summary>Why a day was not written: the index has no usable row for it (<see cref="IndexMissing"/>, <see cref="IndexThin"/>, <see cref="IndexInvalid"/>), the date is no session of the calendar, or the VIX leg is thin (evidence only).</summary>
public enum ContextSkipReason
{
    IndexMissing = 0,
    IndexThin = 1,
    IndexInvalid = 2,
    NotASession = 3,
    VixThin = 4,
}

/// <summary>The wire spelling of <see cref="ContextSkipReason"/>.</summary>
public static class ContextSkipReasonCodes
{
    public static string ToCode(this ContextSkipReason reason) => reason switch
    {
        ContextSkipReason.IndexMissing => "index_missing",
        ContextSkipReason.IndexThin => "index_thin",
        ContextSkipReason.IndexInvalid => "index_invalid",
        ContextSkipReason.NotASession => "not_a_session",
        ContextSkipReason.VixThin => "vix_thin",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };
}
