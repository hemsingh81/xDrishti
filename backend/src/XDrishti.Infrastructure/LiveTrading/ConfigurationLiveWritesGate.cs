using Microsoft.Extensions.Configuration;
using XDrishti.Application.Abstractions.LiveTrading;

namespace XDrishti.Infrastructure.LiveTrading;

/// <summary>The capability gate of the live writes: the deployment setting <c>Live:Writes:Enabled</c> (environment <c>Live__Writes__Enabled</c>), OFF unless it reads exactly <c>true</c>. Changed with a restart; nothing in the app changes it.</summary>
internal sealed class ConfigurationLiveWritesGate(IConfiguration configuration) : ILiveWritesGate
{
    public const string Key = "Live:Writes:Enabled";

    public bool Enabled => bool.TryParse(configuration[Key], out var on) && on;
}
