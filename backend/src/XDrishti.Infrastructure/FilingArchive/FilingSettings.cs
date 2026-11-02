using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Infrastructure.Filings;

namespace XDrishti.Infrastructure.FilingArchive;

internal sealed class FilingSettings(IOptions<FilingsOptions> options) : IFilingSettings
{
    private const long Gb = 1024L * 1024 * 1024;

    public int LagMinutes => options.Value.LagMinutes;

    public long MaxFilesBytes => options.Value.MaxFilesGb * Gb;

    public long MinFreeBytes => options.Value.MinFreeGb * Gb;
}
