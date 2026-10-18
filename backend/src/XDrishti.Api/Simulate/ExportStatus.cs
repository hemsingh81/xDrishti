namespace XDrishti.Api.Simulate;

/// <summary>What the folder writer did (spec P3-04b1, E7).</summary>
internal enum ExportStatus
{
    /// <summary>The run folder did not exist and now holds the export.</summary>
    Written,

    /// <summary>The identical export was already there; every file hashed to its recorded value; nothing was renamed.</summary>
    Unchanged,

    /// <summary>The export was there but different; with force, it was renamed to <c>&lt;run-id&gt;.previous</c> and the new one put in its place.</summary>
    ReplacedPrevious,
}
