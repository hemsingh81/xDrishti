using System.Text;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Infrastructure.Ai.Assistant;

namespace XDrishti.Infrastructure.FilingArchive;

/// <summary>
/// The files of the announcement eval (P15-07, <see cref="AnnouncementFile"/>). The sample and the labelled text live under the git-ignored <c>data/eval</c> (folders 0700, files 0600); the committed baseline is read only and the committed manifest is written 0644 into the repo's
/// <c>eval/sets</c>. A write goes to a temporary name in the same folder and is moved into place. Nothing is ever deleted and nothing is sent anywhere.
/// </summary>
internal sealed class AnnouncementEvalFiles(IOptions<EvalOptions> options) : IAnnouncementEvalFiles
{
    private const UnixFileMode OwnerFolder = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private const UnixFileMode OwnerFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private const UnixFileMode PublicFile = OwnerFile | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    private static readonly UTF8Encoding Utf8 = new(false);

    public async ValueTask<string?> ReadAsync(AnnouncementFile file, CancellationToken cancellationToken)
    {
        var path = PathOf(file);
        return path is not null && File.Exists(path) ? await File.ReadAllTextAsync(path, Utf8, cancellationToken) : null;
    }

    public async ValueTask WriteAsync(AnnouncementFile file, string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (file == AnnouncementFile.Baseline)
        {
            throw new InvalidOperationException("The baseline is committed by a person before the first sample; the app never writes it.");
        }

        var path = PathOf(file) ?? throw new InvalidOperationException("The eval folder was not found (set Eval:Folder).");
        var folder = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(folder))
        {
            if (OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(folder);
            }
            else
            {
                Directory.CreateDirectory(folder, OwnerFolder);
            }
        }

        var temp = Path.Combine(folder, ".tmp-" + Guid.NewGuid().ToString("N"));
        var create = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.Asynchronous };
        if (!OperatingSystem.IsWindows())
        {
            create.UnixCreateMode = file == AnnouncementFile.Manifest ? PublicFile : OwnerFile;
        }

        await using (var output = new FileStream(temp, create))
        {
            await output.WriteAsync(Utf8.GetBytes(text), cancellationToken);
            await output.FlushAsync(cancellationToken);
        }

        File.Move(temp, path, overwrite: true);
    }

    private string? PathOf(AnnouncementFile file)
    {
        var evalFolder = EvalPaths.Locate(options.Value.Folder);
        if (evalFolder is null)
        {
            return null;
        }

        var data = EvalPaths.DataFolder(evalFolder, options.Value.DataFolder);
        return file switch
        {
            AnnouncementFile.Baseline => Path.Combine(evalFolder, "sets", EvalPaths.BaselineName),
            AnnouncementFile.Manifest => Path.Combine(evalFolder, "sets", EvalPaths.ManifestName),
            AnnouncementFile.ToLabel => Path.Combine(data, "announcements", "to-label.csv"),
            AnnouncementFile.Sample => Path.Combine(data, "announcements", "sample.json"),
            _ => Path.Combine(data, "sets", EvalPaths.LabelledName),
        };
    }
}
