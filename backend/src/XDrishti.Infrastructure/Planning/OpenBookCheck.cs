using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Planning;

namespace XDrishti.Infrastructure.Planning;

/// <summary>The two open-book switches as options, validated at host start (spec P10-02, criterion 6): taking the book as empty and reading it from the paper ledger contradict each other, so both on stops the host.</summary>
internal sealed class OpenBookCheck
{
    /// <summary>The message of the failed start.</summary>
    public const string Message = "Planning:OpenBook:AssumedEmpty and Planning:OpenBook:UsePaperLedger are exclusive: set at most one of them.";

    public bool AssumedEmpty { get; set; }

    public bool UsePaperLedger { get; set; }

    public static bool IsValid(OpenBookCheck options) => options is { AssumedEmpty: false } or { UsePaperLedger: false };

    /// <summary>Registers the open-book settings and the start-time check that they are not both on.</summary>
    internal static IServiceCollection AddOpenBook(IServiceCollection services)
    {
        services.AddOptions<OpenBookCheck>().Configure<IOpenBookSettings>((o, s) => (o.AssumedEmpty, o.UsePaperLedger) = (s.AssumedEmpty, s.UsePaperLedger)).Validate(IsValid, Message).ValidateOnStart();
        return services.AddSingleton<IOpenBookSettings, OpenBookSettings>();
    }
}
