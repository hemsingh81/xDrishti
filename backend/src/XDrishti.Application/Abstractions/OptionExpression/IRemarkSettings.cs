namespace XDrishti.Application.Abstractions.OptionExpression;

/// <summary>The setting <c>Options:Remark:*</c> (P14-08): the oldest vendor IV, in minutes, a model-price fallback may use.</summary>
public interface IRemarkSettings
{
    int MaxIvAgeMinutes { get; }
}
