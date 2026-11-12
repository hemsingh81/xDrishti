using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using XDrishti.Application.Abstractions.Evidence;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Evidence;

/// <summary>The host facts of <c>xd evidence preflight</c> (EV-01). Reads the compiled migration list, <c>deploy/compose.yaml</c> (found by walking up from the working folder and the program folder) and this host's configuration key <c>Data:History:Nse:Enabled</c>. Writes nothing.</summary>
internal sealed class EvidencePreflightHost(XDrishtiDbContext db, IConfiguration configuration) : IEvidencePreflightHost
{
    public string? NewestCompiledMigration() => db.Database.GetMigrations().LastOrDefault();

    public long? ComposeApiMemLimitMib() => FindCompose() is { } path ? ComposeMemLimit.Mib(File.ReadAllText(path), "xd-api") : null;

    public bool NseHistoryEnabled() => configuration.GetValue<bool>("Data:History:Nse:Enabled"); // the key alone: the history slice's own options type is reserved to that slice (architecture rule)

    private static string? FindCompose()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "deploy", "compose.yaml");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
