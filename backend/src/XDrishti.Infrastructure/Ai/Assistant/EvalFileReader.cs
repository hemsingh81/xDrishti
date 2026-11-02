using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Assistant.Evaluation;
using XDrishti.Domain.Common;

namespace XDrishti.Infrastructure.Ai.Assistant;

/// <summary>Reads the files of the eval from the repo's <c>eval/</c> folder (spec P9-09 Scope, D7): the configured folder, else the first <c>eval/</c> with a <c>thresholds.json</c> above the program or the working directory. Files only; nothing is written.</summary>
internal sealed class EvalFileReader(IOptions<EvalOptions> options) : IEvalFiles
{
    public async ValueTask<Result<EvalFileSet>> ReadAsync(CancellationToken cancellationToken)
    {
        var folder = EvalPaths.Locate(options.Value.Folder);
        if (folder is null)
        {
            return Result.Failure<EvalFileSet>(EvalErrors.FilesMissing("the eval folder was not found (set Eval:Folder)"));
        }

        try
        {
            return Result.Success(new EvalFileSet(
                await File.ReadAllTextAsync(Path.Combine(folder, "fixture", "fixture.json"), cancellationToken),
                await File.ReadAllTextAsync(Path.Combine(folder, "sets", "data-questions.jsonl"), cancellationToken),
                await File.ReadAllTextAsync(Path.Combine(folder, "sets", "plans.jsonl"), cancellationToken),
                await File.ReadAllTextAsync(Path.Combine(folder, "sets", "ideas.jsonl"), cancellationToken),
                await File.ReadAllTextAsync(Path.Combine(folder, "thresholds.json"), cancellationToken),
                await ReadOptionalAsync(Path.Combine(EvalPaths.DataFolder(folder, options.Value.DataFolder), "sets", EvalPaths.LabelledName), cancellationToken),
                await ReadOptionalAsync(Path.Combine(folder, "sets", EvalPaths.ManifestName), cancellationToken),
                await ReadOptionalAsync(Path.Combine(folder, "sets", EvalPaths.BaselineName), cancellationToken)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure<EvalFileSet>(EvalErrors.FilesMissing("a file of the eval folder cannot be read"));
        }
    }

    private static async Task<string?> ReadOptionalAsync(string path, CancellationToken cancellationToken) => File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;
}
