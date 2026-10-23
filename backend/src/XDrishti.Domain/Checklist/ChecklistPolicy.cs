using XDrishti.Domain.Common;

namespace XDrishti.Domain.Checklist;

/// <summary>Per-check policy of a failure. Defaults follow doc 16 (<c>data_quality: block_with_confirm, holdings_conflict: warn, sector_limit: warn</c>, where <c>sector_limit</c> is the code <c>sector_exposure</c> here); every other check blocks with confirmation (the safe side).</summary>
public sealed class ChecklistPolicy
{
    private readonly Dictionary<string, CheckPolicy> _overrides;

    private ChecklistPolicy(Dictionary<string, CheckPolicy> overrides) => _overrides = overrides;

    /// <summary>The defaults.</summary>
    public static ChecklistPolicy Default { get; } = new([]);

    /// <summary>Defaults plus overrides by check code. An unknown code, the account gate or an undefined policy value is refused.</summary>
    public static Result<ChecklistPolicy> Create(IReadOnlyDictionary<string, CheckPolicy> overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        foreach (var (code, policy) in overrides)
        {
            if (!ChecklistCodes.IsConfirmable(code))
            {
                return Result.Failure<ChecklistPolicy>(Error.Validation("checklist.unknown_code", "'" + code + "' is not a check that has a policy."));
            }

            if (!Enum.IsDefined(policy))
            {
                return Result.Failure<ChecklistPolicy>(Error.Validation("checklist.unknown_policy", "The policy of '" + code + "' is not warn or block_with_confirm."));
            }
        }

        return Result.Success(new ChecklistPolicy(new Dictionary<string, CheckPolicy>(overrides)));
    }

    /// <summary>The policy of a check.</summary>
    public CheckPolicy Of(string code) =>
        _overrides.TryGetValue(code, out var p) ? p : code switch
        {
            ChecklistCodes.HoldingsConflict or ChecklistCodes.SectorExposure => CheckPolicy.Warn,
            _ => CheckPolicy.BlockWithConfirm,
        };
}
