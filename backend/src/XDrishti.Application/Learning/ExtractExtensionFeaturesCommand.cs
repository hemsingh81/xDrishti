using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Learning;

/// <summary>Extracts and keeps the extension features (<c>ev-1</c> or <c>fu-1</c>) of the trades of a stored run (P15-05). Running it again writes nothing new.</summary>
/// <param name="RunKey">The 64-hex key of a stored run.</param>
/// <param name="Version">The extension version: <c>ev-1</c> or <c>fu-1</c>.</param>
public sealed record ExtractExtensionFeaturesCommand(string RunKey, string Version) : ICommand;
