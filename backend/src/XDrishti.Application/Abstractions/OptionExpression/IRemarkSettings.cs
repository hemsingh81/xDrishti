namespace XDrishti.Application.Abstractions.OptionExpression;

/// <summary>The setting <c>Options:Remark:*</c> (P14-08): the oldest vendor IV, in minutes, a model-price fallback may use.</summary>
public interface IRemarkSettings
{
    int MaxIvAgeMinutes { get; }

    /// <summary>The stored charge sheet id the condor family is costed on (<c>Options:Remark:CondorCostSheetId</c>, P14-10); null for gross (a gross condor can neither be kept nor dropped).</summary>
    string? CondorCostSheetId { get; }
}
