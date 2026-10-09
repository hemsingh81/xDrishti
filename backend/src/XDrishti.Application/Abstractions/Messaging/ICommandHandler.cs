using XDrishti.Domain.Common;

namespace XDrishti.Application.Abstractions.Messaging;

/// <summary>Marker for a request that changes state.</summary>
public interface ICommand;

/// <summary>Handles one command and reports expected failures through <see cref="Result"/>.</summary>
public interface ICommandHandler<in TCommand>
    where TCommand : ICommand
{
    ValueTask<Result> HandleAsync(TCommand command, CancellationToken cancellationToken);
}
