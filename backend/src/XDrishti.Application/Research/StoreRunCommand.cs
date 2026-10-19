using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Research;

/// <summary>Stores a finished run in the result store (spec P3-10, D8). The record text and its two digests come from the caller (the verb), who made them from the run it hands over.</summary>
/// <param name="Run">The run.</param>
/// <param name="Record">The canonical record text without trades and the digests.</param>
/// <param name="Actor">Who stores it (<c>cli:simulate</c> for the verb).</param>
public sealed record StoreRunCommand(SimulationRun? Run, RunRecordText Record, string Actor) : ICommand;
