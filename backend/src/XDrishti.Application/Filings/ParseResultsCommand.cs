using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Filings;

/// <summary>Parses the stored results filings that have no parse row for the current parser version (P15-03). Local files and the filings tables only: no network, no broker call.</summary>
public sealed record ParseResultsCommand : ICommand;
