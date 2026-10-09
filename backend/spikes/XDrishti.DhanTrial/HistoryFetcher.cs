namespace XDrishti.DhanTrial;

/// <summary>Caches raw API responses on disk so the analysis can be re-run offline and the same report produced.</summary>
internal interface IRawStore
{
    string? Read(string key);

    void Write(string key, string content);
}

internal sealed class FileRawStore(string root) : IRawStore
{
    public string? Read(string key)
    {
        var path = Path.Combine(root, key);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public void Write(string key, string content)
    {
        var path = Path.Combine(root, key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}

internal sealed record FetchResult(IReadOnlyList<Bar> Bars, int Chunks, int FromCache, IReadOnlyList<string> Errors, int MaxChunkBars = 0);

/// <summary>Fetches 1-minute (chunked to the 90-day request limit) and daily candles, using and filling the raw cache.</summary>
internal sealed class HistoryFetcher(IDhanApi api, IRawStore store, Func<string> accessToken, bool offline, int maxChunkDays = 90)
{
    public async Task<FetchResult> FetchIntradayAsync(string symbol, InstrumentKey key, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var chunks = DateChunker.Split(from, to, maxChunkDays);
        var bars = new List<Bar>();
        var errors = new List<string>();
        var cached = 0;
        var maxChunk = 0;
        foreach (var range in chunks)
        {
            var cacheKey = $"intraday/{symbol}/{range.From:yyyy-MM-dd}_{range.To:yyyy-MM-dd}.json";
            var body = store.Read(cacheKey);
            if (body is not null)
            {
                cached++;
            }
            else if (offline)
            {
                errors.Add($"{range.From:yyyy-MM-dd}..{range.To:yyyy-MM-dd}: not cached (offline)");
                continue;
            }
            else
            {
                var response = await api.GetIntradayAsync(accessToken(), key, range, cancellationToken);
                if (!response.Ok)
                {
                    errors.Add($"{range.From:yyyy-MM-dd}..{range.To:yyyy-MM-dd}: {response.ErrorCode} {response.ErrorMessage}".Trim());
                    continue;
                }

                body = response.Body;
                store.Write(cacheKey, body);
            }

            var parsed = CandleParser.Parse(body);
            maxChunk = Math.Max(maxChunk, parsed.Count);
            bars.AddRange(parsed);
        }

        return new FetchResult(bars, chunks.Count, cached, errors, maxChunk);
    }

    public async Task<FetchResult> FetchDailyAsync(string symbol, InstrumentKey key, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var cacheKey = $"daily/{symbol}/{from:yyyy-MM-dd}_{to:yyyy-MM-dd}.json";
        var body = store.Read(cacheKey);
        var cached = body is null ? 0 : 1;
        if (body is null)
        {
            if (offline)
            {
                return new FetchResult([], 1, 0, [$"daily {from:yyyy-MM-dd}..{to:yyyy-MM-dd}: not cached (offline)"]);
            }

            var response = await api.GetDailyAsync(accessToken(), key, new DateRange(from, to), cancellationToken);
            if (!response.Ok)
            {
                return new FetchResult([], 1, 0, [$"daily: {response.ErrorCode} {response.ErrorMessage}".Trim()]);
            }

            body = response.Body;
            store.Write(cacheKey, body);
        }

        return new FetchResult(CandleParser.Parse(body), 1, cached, []);
    }
}
