namespace XDrishti.Application.Platform.Services;

/// <summary>The ids the platform itself knows (part of the API: never rename one). The ids of the services of the other modules are declared by those modules (<c>EodService.Id</c>, <c>InstrumentMasterService.Id</c>).</summary>
public static class ServiceIds
{
    /// <summary>The per-account holdings sync, set under Accounts (ADR 0013); shown on the Services screen as a read-only row.</summary>
    public const string AccountSync = "account-sync";
}
