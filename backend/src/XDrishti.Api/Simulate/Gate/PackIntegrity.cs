using System.Globalization;
using System.Text.Json;
using XDrishti.SimExport.Manifest;
using XDrishti.SimExport.Verification;

namespace XDrishti.Api.Simulate.Gate;

/// <summary>
/// The completeness of the pack the gate judges (spec P3-09d D6), pure. A pack that was cut or edited must not pass because what is left is clean: <c>pack.json</c> must be the file the manifest lists, its <c>pack_key</c> and <c>count</c> must be the manifest's,
/// its rows and spot lines must be exactly the manifest's <c>pack.row=</c> and <c>pack.spot=</c> lines, and the trades of <c>engine-values.csv</c> must be exactly the rows of <c>pack.json</c>. Every problem is a text; none is a pass.
/// </summary>
internal static class PackIntegrity
{
    public static List<string> Check(byte[] packJson, string packJsonSha256, byte[]? manifestBytes, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> engine)
    {
        ArgumentNullException.ThrowIfNull(packJson);
        ArgumentNullException.ThrowIfNull(engine);
        var problems = new List<string>();
        List<string> rows;
        List<string> spots;
        string? packKey;
        int count;
        try
        {
            using var doc = JsonDocument.Parse(packJson);
            var root = doc.RootElement;
            rows = [.. root.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("seq").GetInt32().ToString(CultureInfo.InvariantCulture) + "|" + r.GetProperty("trade_key").GetString())];
            spots = [.. root.GetProperty("spots").EnumerateArray().Select(r => r.GetProperty("seq").GetInt32().ToString(CultureInfo.InvariantCulture) + "|" + r.GetProperty("key").GetString())];
            packKey = root.GetProperty("pack_key").GetString();
            count = root.GetProperty("count").GetInt32();
        }
        catch (Exception bad) when (bad is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return ["pack.json cannot be read for the completeness check"];
        }

        var parsed = manifestBytes is null ? null : ExportManifest.ParseBytes(manifestBytes);
        if (parsed is not { IsSuccess: true })
        {
            problems.Add("manifest.json is missing or cannot be read: the pack cannot be shown complete");
        }
        else
        {
            var manifest = parsed.Value;
            if (manifest.Entries.FirstOrDefault(e => e.Name == PackFiles.PackJsonName)?.Sha256 != packJsonSha256)
            {
                problems.Add("pack.json differs from manifest.json: the pack was edited after it was written");
            }

            if (!string.Equals(manifest.RunKey, packKey, StringComparison.Ordinal))
            {
                problems.Add("pack_key of pack.json is not the pack key of manifest.json");
            }

            string Extra(string prefix) => string.Join('\n', manifest.ExtraLines.Where(l => l.StartsWith(prefix, StringComparison.Ordinal)).Select(l => l[prefix.Length..]));
            if (Extra("pack.count=") != count.ToString(CultureInfo.InvariantCulture))
            {
                problems.Add("count of pack.json is not the pack.count of manifest.json");
            }

            if (!manifest.ExtraLines.Where(l => l.StartsWith("pack.row=", StringComparison.Ordinal)).Select(l => l["pack.row=".Length..]).Order(StringComparer.Ordinal).SequenceEqual(rows.Order(StringComparer.Ordinal)))
            {
                problems.Add("the rows of pack.json are not the rows of manifest.json (the pack was cut or changed)");
            }

            if (!manifest.ExtraLines.Where(l => l.StartsWith("pack.spot=", StringComparison.Ordinal)).Select(l => l["pack.spot=".Length..]).Order(StringComparer.Ordinal).SequenceEqual(spots.Order(StringComparer.Ordinal)))
            {
                problems.Add("the spot lines of pack.json are not those of manifest.json (the pack was cut or changed)");
            }
        }

        var packKeys = rows.Select(r => r[(r.IndexOf('|', StringComparison.Ordinal) + 1)..]).ToHashSet(StringComparer.Ordinal);
        var engineKeys = engine.Where(e => e.Value.GetValueOrDefault("source") != PackPlanner.SpotSource).Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var key in engineKeys.Except(packKeys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            problems.Add("engine-values.csv has a trade that pack.json does not list: " + key);
        }

        foreach (var key in packKeys.Except(engineKeys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            problems.Add("pack.json lists a trade that engine-values.csv does not have: " + key);
        }

        return problems;
    }
}
