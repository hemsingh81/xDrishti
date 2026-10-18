namespace XDrishti.Application.Research.CostSheets;

/// <summary>What a load did to the row of the sheet.</summary>
public enum LoadStatus
{
    /// <summary>No row existed; one was added.</summary>
    Created,

    /// <summary>The rates differed and <c>--replace</c> was given; the row was overwritten.</summary>
    Replaced,

    /// <summary>The rates are the same; the label, source or checked note changed and was written.</summary>
    DescriptionUpdated,

    /// <summary>The stored sheet equals the file; nothing was written.</summary>
    Unchanged,
}
