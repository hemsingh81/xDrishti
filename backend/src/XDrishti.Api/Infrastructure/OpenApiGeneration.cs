using System.Reflection;

namespace XDrishti.Api.Infrastructure;

/// <summary>
/// Detects the build-time OpenAPI document generation (<c>scripts/gen-api.sh</c>): the generator starts the app in a tool process
/// (entry assembly <c>GetDocument.Insider</c>) without any secrets, only to read the endpoint metadata.
/// </summary>
internal static class OpenApiGeneration
{
    public static bool IsRunning { get; } = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";
}
