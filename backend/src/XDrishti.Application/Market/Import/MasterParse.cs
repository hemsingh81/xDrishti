using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Options.Import;

namespace XDrishti.Application.Market.Import;

/// <summary>
/// The in-scope rows of Dhan's file, how many rows were read, what to tell the owner, and the Dhan ids of in-scope rows that had to be skipped
/// (<paramref name="Protected"/>: their instruments must not be retired just because the row could not be read), and the option rows of the listed
/// underlyings (<paramref name="Options"/>: null when no underlying was listed).
/// </summary>
internal sealed record MasterParse(
    IReadOnlyList<MasterRow> Rows,
    long RowsRead,
    long OutOfScope,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<BrokerIdentity> Protected,
    OptionParse? Options = null);
