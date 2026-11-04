using XDrishti.Domain.Options.Reports;

namespace XDrishti.Application.OptionReports;

/// <summary>A count that is <c>ok</c> with its value, or <c>unavailable</c> with a reason word and no value: never 0 in the place of a non-value (P8-00 D3).</summary>
public sealed record ReportedCount(string State, int? Value, string? Reason)
{
    public static ReportedCount Of(int value) => new(ReportStates.Ok, value, null);

    public static ReportedCount None(string reason) => new(ReportStates.Unavailable, null, reason);
}

/// <summary>A share of 1 (or a plain measure in sessions) that is <c>ok</c> with its value, or <c>unavailable</c> with a reason word.</summary>
public sealed record ReportedShare(string State, double? Value, string? Reason)
{
    public static ReportedShare Of(double value) => new(ReportStates.Ok, value, null);

    public static ReportedShare None(string reason) => new(ReportStates.Unavailable, null, reason);
}

/// <summary>A date that is <c>ok</c> with its value, or <c>unavailable</c> with a reason word.</summary>
public sealed record ReportedDate(string State, DateOnly? Value, string? Reason)
{
    public static ReportedDate Of(DateOnly value) => new(ReportStates.Ok, value, null);

    public static ReportedDate None(string reason) => new(ReportStates.Unavailable, null, reason);
}
