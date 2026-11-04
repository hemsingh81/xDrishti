using XDrishti.Domain.Options.Remark;

namespace XDrishti.Domain.Options.Structures;

/// <summary>One condor entry of the range family: the first session of an expiry week and the weekly series it trades.</summary>
public sealed record CondorEntry(DateOnly Date, SeriesPlan Plan);
