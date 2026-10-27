namespace XDrishti.Domain.Assistant;

/// <summary>What a column of an <c>aiv</c> view holds, for the tool runner (spec P9-02 AC 7). Only <see cref="Text"/> cells are cleaned and scrubbed; every other kind is delivered untouched, so a run key, a hash or an amount is never redacted.</summary>
public enum ColumnKind
{
    /// <summary>Free text (class U): control, zero-width and bidirectional characters are stripped, the text is escaped, scrubbed and cut.</summary>
    Text,

    /// <summary>An identifier: symbol, ISIN, strategy id, plan or trade id.</summary>
    Id,

    /// <summary>A hash (SHA-256, 64 hex).</summary>
    Hash,

    /// <summary>A key: run key, plan key, cell key, model version key.</summary>
    Key,

    /// <summary>A money amount.</summary>
    Amount,

    /// <summary>Any other number.</summary>
    Number,

    /// <summary>A calendar date.</summary>
    Date,

    /// <summary>A moment or a time of day.</summary>
    Timestamp,

    /// <summary>A true/false flag.</summary>
    Flag,

    /// <summary>A short value from a fixed vocabulary written by the system (a state, a side, a status).</summary>
    Code,
}
