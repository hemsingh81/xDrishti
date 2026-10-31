namespace XDrishti.Api.Endpoints;

/// <summary>The owner's change of the global switches. The password is needed for every change; autoLiveEnabled true is always refused. The actor is never taken from the body.</summary>
internal sealed record ExecutionSettingsChangeBody(bool? ExecutionEnabled, bool? AutoLiveEnabled, string? Reason, string? Password);

/// <summary>Run drill for one account, with a fresh password. The actor is never taken from the body.</summary>
internal sealed record DrillRequestBody(Guid? AccountId, string? Password);
