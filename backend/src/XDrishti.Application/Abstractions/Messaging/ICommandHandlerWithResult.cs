using XDrishti.Domain.Common;

namespace XDrishti.Application.Abstractions.Messaging;

/// <summary>Handles one command that returns a value on success; expected failures come back as a failed <see cref="Result{T}"/>.</summary>
public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand
{
    ValueTask<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken);
}
