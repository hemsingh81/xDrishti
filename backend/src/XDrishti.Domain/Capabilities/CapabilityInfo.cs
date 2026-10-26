namespace XDrishti.Domain.Capabilities;

/// <summary>What the owner is told about one capability: its stable name (a code constant), a label and what switching it on allows and does not allow.</summary>
/// <param name="Name">The stored name, lower case with underscores.</param>
/// <param name="Label">A few words.</param>
/// <param name="Meaning">One or two sentences: what ON allows, and that OFF is the default.</param>
public sealed record CapabilityInfo(string Name, string Label, string Meaning);
