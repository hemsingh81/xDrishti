using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Learning;

/// <summary>Extracts and keeps the decision-time features of the closed trades of a stored run (P6-04). Running it again writes nothing new.</summary>
/// <param name="RunKey">The 64-hex key of a stored run.</param>
public sealed record ExtractTradeFeaturesCommand(string RunKey) : ICommand;
