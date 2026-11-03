using System.Security.Cryptography;
using System.Text;
using XDrishti.Application.Abstractions.Market.Eod;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Market.Eod;

/// <summary>Step 0 of CF-04 Part A: fetch the raw files of these dates once, keep them under the git-ignored <c>data/</c> folder and report their header lines (never any other content).</summary>
public sealed record EodProbeCommand(IReadOnlyList<DateOnly> Dates) : ICommand;

/// <summary>The report lines of the probe: one per file, naming the saved path, size, SHA-256 and the header line, and whether a known layout matches.</summary>
public sealed record EodProbeReport(IReadOnlyList<string> Lines);

internal sealed class EodProbeHandler(IEodSettings settings, IEodSource source, IEodRawFiles files, TimeProvider clock) : ICommandHandler<EodProbeCommand, EodProbeReport>
{
    public const int MaxDates = 10;

    private const int MaxHeaderChars = 600;

    public async ValueTask<Result<EodProbeReport>> HandleAsync(EodProbeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!settings.Enabled)
        {
            return Result.Failure<EodProbeReport>(EodErrors.Disabled());
        }

        if (command.Dates.Count is 0 or > MaxDates)
        {
            return Result.Failure<EodProbeReport>(Error.Validation("history.bad_dates", $"Give 1 to {MaxDates} dates."));
        }

        var lines = new List<string>();
        var first = true;
        foreach (var date in command.Dates.Distinct())
        {
            foreach (var kind in new[] { EodFileKind.Bhavcopy, EodFileKind.Delivery })
            {
                if (!first)
                {
                    await Task.Delay(settings.Delay, clock, cancellationToken);
                }

                first = false;
                var fetch = await source.FetchAsync(kind, date, cancellationToken);
                switch (fetch.Status)
                {
                    case EodFetchStatus.NotAllowed:
                        return Result.Failure<EodProbeReport>(EodErrors.NotAllowed(fetch.Message ?? "The endpoint is not allowed."));
                    case EodFetchStatus.Ok:
                        var name = $"eod-{kind.ToString().ToLowerInvariant()}-{date:yyyyMMdd}.txt";
                        var path = await files.SaveAsync(name, fetch.Content!, cancellationToken);
                        lines.Add($"{date:yyyy-MM-dd} {kind}: saved {path} ({fetch.Content!.Length} bytes, sha256 {Convert.ToHexStringLower(SHA256.HashData(fetch.Content))})");
                        lines.Add("  header: " + Header(fetch.Content, kind) + "  layout: " + Layout(fetch.Content, kind));
                        break;
                    case EodFetchStatus.Absent:
                        lines.Add($"{date:yyyy-MM-dd} {kind}: no such file at NSE");
                        break;
                    default:
                        lines.Add($"{date:yyyy-MM-dd} {kind}: fetch failed ({fetch.Message})");
                        break;
                }
            }
        }

        return Result.Success(new EodProbeReport(lines));
    }

    private static string HeaderLine(byte[] content, EodFileKind kind)
    {
        using var reader = new StringReader(Encoding.UTF8.GetString(content).TrimStart('﻿'));
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0 || (kind == EodFileKind.Delivery && !line.StartsWith(EodFormats.DeliveryHeader.Record, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            return line;
        }

        return string.Empty;
    }

    private static string Header(byte[] content, EodFileKind kind)
    {
        var line = HeaderLine(content, kind);
        return line.Length > MaxHeaderChars ? line[..MaxHeaderChars] : line;
    }

    private static string Layout(byte[] content, EodFileKind kind)
    {
        var header = HeaderLine(content, kind).Split(',').Select(h => h.Trim()).ToArray();
        if (kind == EodFileKind.Delivery)
        {
            var h = EodFormats.DeliveryHeader;
            return new[] { h.Symbol, h.Series, h.Traded, h.Delivered, h.Percent }.All(p => header.Any(c => c.StartsWith(p, StringComparison.OrdinalIgnoreCase))) ? "known (mto)" : "UNKNOWN";
        }

        var known = EodFormats.Layouts.FirstOrDefault(l => l.Columns.Where(c => !l.Optional.Contains(c.Key)).All(c => header.Contains(c.Value, StringComparer.OrdinalIgnoreCase)));
        return known is null ? "UNKNOWN" : "known (" + known.Name + ")";
    }
}
