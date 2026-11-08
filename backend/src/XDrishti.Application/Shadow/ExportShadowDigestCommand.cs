using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Shadow;

/// <summary>Copies the chain head to the digest log outside the database (spec CF-08 review fix 4, ADR 0232). Writes nothing in the database.</summary>
public sealed record ExportShadowDigestCommand : ICommand;

/// <summary>What an export did.</summary>
/// <param name="Exported">Whether the switch is ON and the chain is intact and has a session (otherwise nothing is exported).</param>
/// <param name="Written">Whether a new line was appended (false when the newest line already had this digest).</param>
/// <param name="Summary">One sentence for the service run.</param>
public sealed record ShadowDigestExported(bool Exported, bool Written, string Summary)
{
    /// <summary>What the summary of a successful export starts with (the run history is searched for it).</summary>
    public const string ExportedPrefix = "Digest of ";
}
