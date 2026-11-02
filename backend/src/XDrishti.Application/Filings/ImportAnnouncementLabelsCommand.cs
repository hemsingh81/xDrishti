using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Filings;

/// <summary>Checks the owner's labelled file (its text) against the sample and writes the labelled set and the manifest (P15-07).</summary>
public sealed record ImportAnnouncementLabelsCommand(string LabelFileText) : ICommand;
