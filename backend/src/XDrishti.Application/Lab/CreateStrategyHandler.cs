using System.Text;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Lab;

/// <summary>
/// Stores a valid strategy text (spec P5-01, criteria 3 to 6). The version is the server's: the text may omit <c>version</c> or carry the number the new version will get, otherwise <c>lab.version_exists</c>. The same content again is the same
/// row. This path never sets <see cref="NewVersion.Seed"/>: a built-in is refused by the store with <c>lab.builtin_readonly</c> (409), whatever the request says.
/// </summary>
internal sealed class CreateStrategyHandler(IStrategyChecker checker, IStrategyStore store, ILogger<CreateStrategyHandler> logger) : ICommandHandler<CreateStrategyCommand, CreatedStrategy>
{
    public async ValueTask<Result<CreatedStrategy>> HandleAsync(CreateStrategyCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (Encoding.UTF8.GetByteCount(command.Text) > LabErrors.MaxTextBytes)
        {
            return Result.Failure<CreatedStrategy>(LabErrors.InvalidStrategy([LabErrors.TooLarge()]));
        }

        var check = checker.Check(command.Text);
        if (check.Description is not { } d)
        {
            return Result.Failure<CreatedStrategy>(LabErrors.InvalidStrategy(check.Issues));
        }

        var appended = await store.AppendAsync(
            new NewVersion(d.Id, d.Style, command.Text, d.CanonicalJson, d.Hash, command.ParentHash, command.Note, check.Version),
            cancellationToken);
        if (appended.IsFailure)
        {
            return Result.Failure<CreatedStrategy>(LabErrors.AsConflict(appended.Error));
        }

        var a = appended.Value;
        var stored = await store.GetVersionAsync(a.StrategyId, a.Version, cancellationToken);
        if (a.Created)
        {
            if (a.Version == 1)
            {
                LabLog.Created(logger, a.StrategyId, a.Version, a.Hash[..12]);
            }
            else
            {
                LabLog.Versioned(logger, a.StrategyId, a.Version, a.Hash[..12]);
            }
        }

        return Result.Success(new CreatedStrategy(a.StrategyId, a.Version, a.Hash, stored?.ParentHash, a.Created));
    }
}
