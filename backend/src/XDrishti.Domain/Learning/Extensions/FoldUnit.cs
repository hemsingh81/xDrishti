namespace XDrishti.Domain.Learning.Extensions;

/// <summary>The calendar unit of a test fold (P13-05): a month (P13) or a calendar quarter starting in January, April, July or October (P15).</summary>
public enum FoldUnit
{
    /// <summary>A calendar month.</summary>
    Month,

    /// <summary>A calendar quarter.</summary>
    Quarter,
}
