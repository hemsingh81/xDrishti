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
        var folder = Locate(options.Value.Folder);
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
                await File.ReadAllTextAsync(Path.Combine(folder, "thresholds.json"), cancellationToken)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure<EvalFileSet>(EvalErrors.FilesMissing("a file of the eval folder cannot be read"));
        }
    }

    private static string? Locate(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return File.Exists(Path.Combine(configured, "thresholds.json")) ? configured : null;
        }

        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "eval");
                if (File.Exists(Path.Combine(candidate, "thresholds.json")))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
