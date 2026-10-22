using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Lab;

/// <summary>Stops a run (spec P5-02): a queued run is cancelled at once, a running one at its next safe point (between instruments). <c>lab.not_cancellable</c> for a finished run.</summary>
public sealed record CancelLabRunCommand(Guid Id) : ICommand;
