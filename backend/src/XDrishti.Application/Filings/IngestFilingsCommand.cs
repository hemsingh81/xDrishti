using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Filings;

/// <summary>Reads the filings inbox through the port and stores every accepted file once (P15-02). Local files and the filings tables only: no network, no broker call.</summary>
public sealed record IngestFilingsCommand(bool Manual) : ICommand;
