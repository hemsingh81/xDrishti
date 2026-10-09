using System.Globalization;
using System.Text.Json;

namespace XDrishti.DhanTrial;

internal sealed record TrialOptions(bool Offline, bool TryTotp, bool ForceLogin, bool Burst, string OutDir, string SymbolsFile, DateOnly Today);

internal sealed record SymbolSpec(string Name, SymbolKind Kind, IReadOnlyList<string> Aliases);

internal sealed record SymbolPlan(int RecentDays, int DeepMonths, IReadOnlyList<string> Deep, IReadOnlyList<SymbolSpec> Symbols)
{
    public static SymbolPlan Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        return new SymbolPlan(
            root.GetProperty("recentDays").GetInt32(),
            root.GetProperty("deepMonths").GetInt32(),
            [.. root.GetProperty("deep").EnumerateArray().Select(e => e.GetString()!)],
            [.. root.GetProperty("symbols").EnumerateArray().Select(e => new SymbolSpec(
                e.GetProperty("name").GetString()!,
                string.Equals(e.GetProperty("kind").GetString(), "index", StringComparison.OrdinalIgnoreCase) ? SymbolKind.Index : SymbolKind.Equity,
                e.TryGetProperty("aliases", out var a) ? [.. a.EnumerateArray().Select(x => x.GetString()!)] : []))]);
    }
}

/// <summary>What a live run must remember so that an offline re-analysis reproduces the same report.</summary>
internal sealed record RunManifest(string Today, ProfileInfo? Profile, BurstResult? Burst, EodEstimate? Estimate, List<EndpointStats> Endpoints, List<ErrorProbe> Probes, string? IntervalForm, int MaxBarsPerRequest);

/// <summary>Runs the whole read-only trial: token → profile → renew → master → history → probes → analysis → report. TOTP experiment last, on its own token.</summary>
internal sealed class TrialRunner(
    IDhanApi api,
    IDhanApi burstApi,
    TokenFlow flow,
    TokenFlow totpFlow,
    ITokenStore tokens,
    ISecretSource secrets,
    IRawStore raw,
    TrialOptions options,
    TimeProvider clock,
    Redactor redactor,
    Action<string> log)
{
    private const double DocumentedRequestsPerSecond = 5;
    private readonly TrialResults _results = new() { GeneratedAt = clock.GetUtcNow(), Offline = options.Offline, BurstRequested = options.Burst };
    private AccessToken? _token;
    private DateOnly _today = options.Today;

    public async Task<string> RunAsync(CancellationToken cancellationToken)
    {
        var plan = SymbolPlan.Load(options.SymbolsFile);
        LoadEvents();
        if (options.Offline)
        {
            RestoreManifest();
        }
        else
        {
            await EnsureTokenAsync(cancellationToken);
            await CheckProfileAsync(cancellationToken);
            await TryRenewAsync(cancellationToken);
        }

        var master = await LoadMasterAsync(cancellationToken);
        ResolveSymbols(plan, master);
        await FetchAndAnalyseAsync(plan, cancellationToken);
        if (!options.Offline)
        {
            await ProbeErrorsAsync(cancellationToken);
            await MeasureLimitsAsync(cancellationToken);
            if (options.TryTotp)
            {
                await TryTotpAsync(cancellationToken);
            }

            _results.IntervalForm = api.IntervalForm;
            _results.Endpoints.AddRange(Stats.ByEndpoint(api.Calls.Concat(burstApi.Calls)));
            SaveManifest();
        }

        return ReportBuilder.Build(_results, redactor);
    }

    public async Task<string> RenewOnlyAsync(CancellationToken cancellationToken)
    {
        LoadEvents();
        _token = tokens.Load() ?? throw new InvalidOperationException("No stored token — run 'login' first.");
        redactor.Add(_token.Value);
        await TryRenewAsync(cancellationToken);
        await CheckProfileAsync(cancellationToken);
        return ReportBuilder.Build(_results, redactor);
    }

    public async Task LoginOnlyAsync(CancellationToken cancellationToken)
    {
        LoadEvents();
        await LoginAsync(cancellationToken);
        await CheckProfileAsync(cancellationToken);
    }

    private async Task EnsureTokenAsync(CancellationToken cancellationToken)
    {
        _token = tokens.Load();
        if (_token is not null)
        {
            redactor.Add(_token.Value);
        }

        if (!options.ForceLogin && _token is not null && _token.ExpiresAt > clock.GetUtcNow().AddMinutes(30))
        {
            Record("Reuse stored token", true, $"{_token}; remaining {(_token.ExpiresAt - clock.GetUtcNow()).TotalHours:F1} h");
            return;
        }

        await LoginAsync(cancellationToken);
    }

    private async Task LoginAsync(CancellationToken cancellationToken)
    {
        var clientId = Required("client-id");
        var apiKey = Required("api-key");
        var apiSecret = Required("api-secret");
        try
        {
            _token = await flow.LoginWithConsentAsync(clientId, apiKey, apiSecret, cancellationToken);
            Record("Login (consent flow)", true, $"{_token}; lifetime {_token.Lifetime.TotalHours:F1} h");
        }
        catch (InvalidOperationException ex)
        {
            Record("Login (consent flow)", false, redactor.Redact(ex.Message));
            throw;
        }
    }

    private async Task CheckProfileAsync(CancellationToken cancellationToken)
    {
        try
        {
            _results.Profile = await flow.GetProfileAsync(_token!, cancellationToken);
            Record("Profile", true, $"tokenValidity {_results.Profile.TokenValidity}; dataPlan {_results.Profile.DataPlan}; dataValidity {_results.Profile.DataValidity}");
        }
        catch (InvalidOperationException ex)
        {
            Record("Profile", false, redactor.Redact(ex.Message));
        }
    }

    private async Task TryRenewAsync(CancellationToken cancellationToken)
    {
        var result = await flow.RenewAsync(_token!, cancellationToken);
        if (result is { Ok: true, Token: not null })
        {
            _token = result.Token;
            Record("RenewToken", true, $"renewed with {result.RemainingBefore.TotalHours:F1} h remaining; new {result.Token}");
            return;
        }

        Record("RenewToken", false, $"with {result.RemainingBefore.TotalHours:F1} h remaining: {redactor.Redact(result.Error)}");
        if (result.TokenLost)
        {
            throw new InvalidOperationException("RenewToken succeeded but returned no usable token, so the previous token is probably invalid. Run 'login' again.");
        }
    }

    private async Task TryTotpAsync(CancellationToken cancellationToken)
    {
        var pin = secrets.Get("pin");
        var totp = secrets.Get("totp-secret");
        if (pin is null || totp is null)
        {
            Record("PIN + TOTP token", false, "pin / totp-secret not in the Keychain — skipped");
            return;
        }

        redactor.Add(pin);
        redactor.Add(totp);
        try
        {
            // Own token file: this experiment must not replace the consent-flow token whose renewal is under test.
            var token = await totpFlow.LoginWithTotpAsync(Required("client-id"), pin, totp, cancellationToken);
            Record("PIN + TOTP token", true, $"{token}; lifetime {token.Lifetime.TotalHours:F1} h — fully unattended route");
        }
        catch (InvalidOperationException ex)
        {
            Record("PIN + TOTP token", false, redactor.Redact(ex.Message));
        }
    }

    private async Task<InstrumentMaster> LoadMasterAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(options.OutDir, "raw", "api-scrip-master.csv");
        if (!options.Offline)
        {
            log("Downloading instrument master…");
            var bytes = await api.DownloadMasterAsync(path, cancellationToken);
            log($"  {bytes / 1_000_000.0:F1} MB");
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"No cached instrument master at {path} — run the live trial first.");
        }

        using var reader = File.OpenText(path);
        var master = InstrumentMaster.Parse(reader);
        _results.MasterHeader = master.Header;
        _results.MasterRows = master.TotalRows;
        _results.MasterNseRows = master.Rows.Count;
        return master;
    }

    private void ResolveSymbols(SymbolPlan plan, InstrumentMaster master)
    {
        foreach (var spec in plan.Symbols)
        {
            _results.Resolutions.Add(master.Resolve(spec.Name, spec.Kind, spec.Aliases));
        }
    }

    private async Task FetchAndAnalyseAsync(SymbolPlan plan, CancellationToken cancellationToken)
    {
        var today = _today;
        var recentFrom = today.AddDays(-plan.RecentDays);
        var deepFrom = today.AddMonths(-plan.DeepMonths);
        var fetcher = new HistoryFetcher(api, raw, () => _token!.Value, options.Offline);
        var fetched = new List<(Resolution Res, bool Deep, DateOnly From, FetchResult Minutes, FetchResult Daily)>();
        foreach (var res in _results.Resolutions.Where(r => r.Key is not null))
        {
            var deep = plan.Deep.Contains(res.Requested, StringComparer.OrdinalIgnoreCase);
            var from = deep ? deepFrom : recentFrom;
            log($"Fetching {res.Requested} ({(deep ? $"{plan.DeepMonths} months" : $"{plan.RecentDays} days")})…");
            var symbol = Safe(res.Requested);
            var minutes = await fetcher.FetchIntradayAsync(symbol, res.Key!.Value, from, today, cancellationToken);
            var daily = await fetcher.FetchDailyAsync(symbol, res.Key.Value, from, today, cancellationToken);
            fetched.Add((res, deep, from, minutes, daily));
        }

        _results.MaxBarsPerRequest = fetched.Count == 0 ? 0 : fetched.Max(f => f.Minutes.MaxChunkBars);
        var semantics = TimestampAnalyzer.Classify([.. fetched.SelectMany(f => f.Minutes.Bars)]);
        _results.Semantics = semantics;
        var usable = semantics.Epoch == EpochBase.Unknown ? new TimestampSemantics(EpochBase.TrueUtc, BarLabel.Start, 0, "assumed") : semantics;
        if (semantics.Epoch == EpochBase.Unknown)
        {
            _results.Notes.Add("Timestamp semantics could not be determined; the quality tables assume epoch=UTC and start-labelled bars and are unreliable.");
        }

        var firstDaily = fetched.SelectMany(f => f.Daily.Bars).Select(b => b.Epoch).DefaultIfEmpty(0).Min();
        _results.DailyEpochNote = firstDaily == 0 ? null : string.Create(CultureInfo.InvariantCulture, $"first epoch {firstDaily} → {(firstDaily % 86400 == 0 ? "UTC midnight (date = IST calendar date stored as UTC)" : "not a UTC midnight (real instant; date derived with +05:30)")}");

        var expected = BarAnalyzer.ExpectedByDay(fetched.Select(f => f.Minutes.Bars), usable);
        var zone = usable.Epoch == EpochBase.IstAsUtc ? TimeSpan.Zero : IstTime.Offset;
        foreach (var (res, deep, from, minutes, daily) in fetched)
        {
            var quality = minutes.Bars.Count == 0 ? null : BarAnalyzer.Analyze(res.Requested, minutes.Bars, usable, expected, from, today);
            var cross = daily.Bars.Count == 0 || minutes.Bars.Count == 0 ? null : DailyCrossCheck.Compare(daily.Bars, minutes.Bars, usable);
            DateTimeOffset? first = minutes.Bars.Count == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(minutes.Bars.Min(b => b.Epoch)).ToOffset(zone);
            DateTimeOffset? last = minutes.Bars.Count == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(minutes.Bars.Max(b => b.Epoch)).ToOffset(zone);
            _results.History.Add(new SymbolHistory(
                res.Requested, res.Kind, res.Key, deep ? $"{plan.DeepMonths} mo" : $"{plan.RecentDays} d", minutes.Chunks, minutes.FromCache,
                [.. minutes.Errors, .. daily.Errors], quality, cross, DailyCrossCheck.PriceDiscontinuities(daily.Bars), first, last, minutes.Bars.Count, daily.Bars.Count));
        }

        await ProbeDepthAsync(today, cancellationToken);
        SaveSamples(fetched.Select(f => f.Res.Requested).FirstOrDefault(), fetched.Any(f => f.Minutes.Bars.Count > 0));
    }

    private async Task ProbeDepthAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var res = _results.Resolutions.FirstOrDefault(r => r.Key is not null && r.Kind == SymbolKind.Equity);
        if (res is null)
        {
            return;
        }

        for (var years = 1; years <= 6; years++)
        {
            var to = today.AddYears(-years);
            var key = $"depth/{Safe(res.Requested)}/{years}y.json";
            var body = raw.Read(key);
            string? error = null;
            if (body is null && !options.Offline)
            {
                var response = await api.GetIntradayAsync(_token!.Value, res.Key!.Value, new DateRange(to.AddDays(-30), to), cancellationToken);
                if (response.Ok)
                {
                    body = response.Body;
                    raw.Write(key, body);
                }
                else
                {
                    error = $"{response.ErrorCode} {response.ErrorMessage}".Trim();
                    if (response.ErrorCode == "DH-907")
                    {
                        // A definitive "no data" is a finding worth caching; auth, throttling and network errors are transient and are not.
                        raw.Write(key, JsonSerializer.Serialize(new { error }));
                    }
                }
            }

            if (error is null && body is not null)
            {
                using var doc = JsonDocument.Parse(body);
                error = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
                _results.Depth.Add(new DepthProbe(years, error is null ? CandleParser.Parse(body).Count : 0, error));
            }
            else if (error is not null)
            {
                _results.Depth.Add(new DepthProbe(years, 0, error));
            }
        }
    }

    /// <summary>Deliberate failures that show the server's error shapes: an invalid token, a request beyond the 90-day limit, and a window with no data.</summary>
    private async Task ProbeErrorsAsync(CancellationToken cancellationToken)
    {
        var res = _results.Resolutions.FirstOrDefault(r => r.Key is not null && r.Kind == SymbolKind.Equity);
        var invalid = await api.GetProfileAsync("invalid-token-probe", cancellationToken);
        _results.Probes.Add(new ErrorProbe("profile with an invalid token", invalid.Status, invalid.ErrorCode, invalid.ErrorMessage, 0));
        if (res is null || _token is null)
        {
            return;
        }

        var tooLong = await api.GetIntradayAsync(_token.Value, res.Key!.Value, new DateRange(_today.AddDays(-100), _today), cancellationToken);
        _results.Probes.Add(new ErrorProbe("1-minute range of 101 days (limit documented as 90)", tooLong.Status, tooLong.ErrorCode, tooLong.ErrorMessage, tooLong.Ok ? CandleParser.Parse(tooLong.Body).Count : 0));
        var future = await api.GetIntradayAsync(_token.Value, res.Key.Value, new DateRange(_today.AddDays(7), _today.AddDays(8)), cancellationToken);
        _results.Probes.Add(new ErrorProbe("1-minute range in the future (no data)", future.Status, future.ErrorCode, future.ErrorMessage, future.Ok ? CandleParser.Parse(future.Body).Count : 0));
    }

    private async Task MeasureLimitsAsync(CancellationToken cancellationToken)
    {
        var latest = _results.History.Where(h => h.LastBar is not null && h.Key is not null && h.Kind == SymbolKind.Equity).ToArray();
        if (latest.Length == 0 || _token is null)
        {
            return;
        }

        var day = DateOnly.FromDateTime(latest.Max(h => h.LastBar!.Value).DateTime);
        var range = new DateRange(day, day);
        var oneDay = new List<double>();
        var dailyMs = new List<double>();
        foreach (var h in latest.Take(5))
        {
            oneDay.Add((await api.GetIntradayAsync(_token.Value, h.Key!.Value, range, cancellationToken)).Elapsed.TotalMilliseconds);
            dailyMs.Add((await api.GetDailyAsync(_token.Value, h.Key.Value, range, cancellationToken)).Elapsed.TotalMilliseconds);
        }

        if (options.Burst)
        {
            // Opt-in and small: Dhan warns that repeated over-limit calls may get the user blocked, so no retries.
            var burst = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => burstApi.GetDailyAsync(_token.Value, latest[0].Key!.Value, range, cancellationToken)));
            var errors = burst.Where(b => b.ErrorCode is not null).GroupBy(b => b.ErrorCode!).ToDictionary(g => g.Key, g => g.Count());
            _results.Burst = new BurstResult(burst.Length, burst.Count(b => b.Ok), burst.Count(b => DhanErrors.IsThrottle(b.Status, b.ErrorCode)), errors);
        }

        var chunks = (int)Math.Ceiling(365 * 5 / 90.0);
        _results.Estimate = Stats.Estimate(200, Stats.Percentile(oneDay, 0.5), Stats.Percentile(dailyMs, 0.5), DocumentedRequestsPerSecond, chunks);
    }

    private void SaveSamples(string? firstSymbol, bool anyBars)
    {
        if (!anyBars || firstSymbol is null)
        {
            return;
        }

        var dir = Path.Combine(options.OutDir, "samples");
        Directory.CreateDirectory(dir);
        foreach (var (kind, name) in new[] { ("intraday", "intraday-1m.json"), ("daily", "daily.json") })
        {
            var folder = Path.Combine(options.OutDir, "raw", kind, Safe(firstSymbol));
            var cached = Directory.Exists(folder) ? Directory.EnumerateFiles(folder).Order(StringComparer.Ordinal).FirstOrDefault() : null;
            if (cached is not null)
            {
                File.WriteAllText(Path.Combine(dir, name), Sanitizer.Sanitize(File.ReadAllText(cached), redactor));
            }
        }
    }

    private string ManifestPath => Path.Combine(options.OutDir, "run.json");

    private void SaveManifest()
    {
        var manifest = new RunManifest(_today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), _results.Profile, _results.Burst, _results.Estimate, _results.Endpoints, _results.Probes, _results.IntervalForm, _results.MaxBarsPerRequest);
        File.WriteAllText(ManifestPath, JsonSerializer.Serialize(manifest));
    }

    private void RestoreManifest()
    {
        if (!File.Exists(ManifestPath))
        {
            _results.Notes.Add("No run.json from a live run: using today's date, so cached windows may not match.");
            return;
        }

        var manifest = JsonSerializer.Deserialize<RunManifest>(File.ReadAllText(ManifestPath))!;
        _today = DateOnly.ParseExact(manifest.Today, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        _results.Profile = manifest.Profile;
        _results.Burst = manifest.Burst;
        _results.Estimate = manifest.Estimate;
        _results.Endpoints.AddRange(manifest.Endpoints);
        _results.Probes.AddRange(manifest.Probes);
        _results.IntervalForm = manifest.IntervalForm;
    }

    private string Required(string name) => RegisterSecret(secrets.Get(name) ?? throw new InvalidOperationException($"Secret '{name}' is not in the Keychain. Run scripts/dhan-secrets.sh first."));

    private string RegisterSecret(string value)
    {
        redactor.Add(value);
        return value;
    }

    private void Record(string step, bool ok, string detail)
    {
        var entry = new TokenEvent(clock.GetUtcNow(), step, ok, redactor.Redact(detail));
        _results.TokenEvents.Add(entry);
        log($"{(ok ? "✓" : "✗")} {step}: {entry.Detail}");
        File.AppendAllText(EventsPath, JsonSerializer.Serialize(new { at = entry.At, step = entry.Step, ok = entry.Ok, detail = entry.Detail }) + "\n");
    }

    private string EventsPath => Path.Combine(options.OutDir, "token-events.jsonl");

    private void LoadEvents()
    {
        Directory.CreateDirectory(options.OutDir);
        if (!File.Exists(EventsPath))
        {
            return;
        }

        foreach (var line in File.ReadLines(EventsPath).Where(l => l.Length > 0))
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            _results.TokenEvents.Add(new TokenEvent(r.GetProperty("at").GetDateTimeOffset(), r.GetProperty("step").GetString()!, r.GetProperty("ok").GetBoolean(), r.GetProperty("detail").GetString()!));
        }
    }

    private static string Safe(string name) => string.Concat(name.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
}
