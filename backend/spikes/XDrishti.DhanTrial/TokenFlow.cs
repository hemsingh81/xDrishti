using System.Globalization;
using System.Text.Json;

namespace XDrishti.DhanTrial;

/// <summary>An access token. <see cref="ToString"/> never prints the value.</summary>
internal sealed record AccessToken(string Value, DateTimeOffset ExpiresAt, string ClientId, string Via, DateTimeOffset ObtainedAt)
{
    public TimeSpan Lifetime => ExpiresAt - ObtainedAt;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"AccessToken(via {Via}, expires {ExpiresAt:yyyy-MM-dd HH:mm zzz})");
}

internal sealed record ProfileInfo(string? TokenValidity, string? DataPlan, string? DataValidity, string? ActiveSegment)
{
    public bool DataPlanActive => string.Equals(DataPlan, "Active", StringComparison.OrdinalIgnoreCase);
}

internal sealed record RenewResult(bool Ok, AccessToken? Token, string? Error, TimeSpan RemainingBefore, bool TokenLost = false);

internal interface ITokenStore
{
    AccessToken? Load();

    void Save(AccessToken token);
}

/// <summary>Stores the token in the private secrets folder (mode 600), never inside the repository.</summary>
internal sealed class FileTokenStore(string path) : ITokenStore
{
    public AccessToken? Load()
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        return new AccessToken(
            root.GetProperty("value").GetString()!,
            root.GetProperty("expiresAt").GetDateTimeOffset(),
            root.GetProperty("clientId").GetString()!,
            root.GetProperty("via").GetString()!,
            root.GetProperty("obtainedAt").GetDateTimeOffset());
    }

    public void Save(AccessToken token)
    {
        var directory = Path.GetDirectoryName(path)!;
        var json = JsonSerializer.Serialize(new { value = token.Value, expiresAt = token.ExpiresAt, clientId = token.ClientId, via = token.Via, obtainedAt = token.ObtainedAt });
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, json);
            return;
        }

        // Created private from the start (no window where the file is world-readable).
        Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.Delete(path);
        using var stream = new FileStream(path, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite });
        stream.Write(System.Text.Encoding.UTF8.GetBytes(json));
    }
}

/// <summary>The token routes under trial: consent (API key + secret, browser login), PIN + TOTP, and renewal.</summary>
internal sealed class TokenFlow(IDhanApi api, ITokenStore store, IPrompt prompt, Redactor redactor, TimeProvider clock)
{
    public const string LoginUrl = "https://auth.dhan.co/login/consentApp-login?consentAppId=";

    public async Task<AccessToken> LoginWithConsentAsync(string clientId, string apiKey, string apiSecret, CancellationToken cancellationToken)
    {
        var consent = await api.GenerateConsentAsync(clientId, apiKey, apiSecret, cancellationToken);
        if (!consent.Ok)
        {
            throw new InvalidOperationException($"generate-consent failed: {consent.ErrorCode} {consent.ErrorMessage}");
        }

        var consentId = Field(consent.Body, "consentAppId") ?? throw new InvalidOperationException("generate-consent returned no consentAppId.");
        prompt.Info("Open this URL in your browser and log in (2FA):");
        prompt.Info(LoginUrl + consentId);
        prompt.Info("After login the browser is redirected to your registered redirect URL. Copy the value of the 'tokenId' parameter from the address bar.");
        var tokenId = ExtractTokenId(prompt.ReadHidden("tokenId, or the whole redirect URL (hidden): "));
        redactor.Add(tokenId);

        var consumed = await api.ConsumeConsentAsync(tokenId, apiKey, apiSecret, cancellationToken);
        if (!consumed.Ok)
        {
            throw new InvalidOperationException($"consumeApp-consent failed: {consumed.ErrorCode} {consumed.ErrorMessage}");
        }

        return Persist(consumed.Body, clientId, "consent flow (API key + secret + browser login)");
    }

    public async Task<AccessToken> LoginWithTotpAsync(string clientId, string pin, string totpSecret, CancellationToken cancellationToken)
    {
        var code = Totp.Generate(totpSecret, clock.GetUtcNow());
        redactor.Add(code);
        var response = await api.GenerateAccessTokenAsync(clientId, pin, code, cancellationToken);
        if (!response.Ok)
        {
            throw new InvalidOperationException($"generateAccessToken failed: {response.ErrorCode} {response.ErrorMessage}");
        }

        return Persist(response.Body, clientId, "PIN + TOTP (no browser)");
    }

    public async Task<RenewResult> RenewAsync(AccessToken current, CancellationToken cancellationToken)
    {
        var remaining = current.ExpiresAt - clock.GetUtcNow();
        var response = await api.RenewTokenAsync(current.Value, current.ClientId, cancellationToken);
        if (!response.Ok)
        {
            return new RenewResult(false, null, $"{response.ErrorCode} {response.ErrorMessage}".Trim(), remaining);
        }

        try
        {
            return new RenewResult(true, Persist(response.Body, current.ClientId, current.Via + " → renewed"), null, remaining);
        }
        catch (InvalidOperationException ex)
        {
            // RenewToken expires the old token; a success without a usable new token leaves nothing valid behind.
            return new RenewResult(false, null, ex.Message, remaining, TokenLost: true);
        }
    }

    public async Task<ProfileInfo> GetProfileAsync(AccessToken token, CancellationToken cancellationToken)
    {
        var response = await api.GetProfileAsync(token.Value, cancellationToken);
        if (!response.Ok)
        {
            throw new InvalidOperationException($"profile failed: {response.ErrorCode} {response.ErrorMessage}");
        }

        return new ProfileInfo(Field(response.Body, "tokenValidity"), Field(response.Body, "dataPlan"), Field(response.Body, "dataValidity"), Field(response.Body, "activeSegment"));
    }

    /// <summary>Accepts the bare tokenId or the whole redirect URL and returns the tokenId.</summary>
    public static string ExtractTokenId(string input)
    {
        var text = input.Trim();
        var marker = text.IndexOf("tokenId=", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return text;
        }

        var rest = text[(marker + "tokenId=".Length)..];
        var end = rest.IndexOfAny(['&', '#', ' ']);
        return Uri.UnescapeDataString(end < 0 ? rest : rest[..end]);
    }

    private AccessToken Persist(string body, string fallbackClientId, string via)
    {
        var value = Field(body, "accessToken") ?? throw new InvalidOperationException("Response contained no accessToken.");
        redactor.Add(value);
        var now = clock.GetUtcNow();
        var expiryKnown = IstTime.TryParse(Field(body, "expiryTime"), out var parsed);
        var expiry = expiryKnown ? parsed : now.AddHours(24);
        var token = new AccessToken(value, expiry, Field(body, "dhanClientId") ?? fallbackClientId, expiryKnown ? via : via + " (expiry not in response — 24 h ASSUMED)", now);
        store.Save(token);
        return token;
    }

    private static string? Field(string body, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value.ToString();
                }
            }
        }
        catch (JsonException)
        {
            // not a JSON object
        }

        return null;
    }
}
