namespace XDrishti.Application.Ai;

/// <summary>The number of tool calls a turn may make (the setting, 1 to 12). A call asks for a place with <see cref="TryTake"/>; the calls beyond the budget are refused with a result, never made.</summary>
public sealed class ToolBudget(int max)
{
    private int _taken;

    /// <summary>The calls asked for so far (granted or not): the call number.</summary>
    public int Asked { get; private set; }

    /// <summary>The calls granted so far.</summary>
    public int Taken => _taken;

    /// <summary>The most calls.</summary>
    public int Max => max;

    /// <summary>Counts one more asked call and says whether it may be made.</summary>
    public bool TryTake()
    {
        Asked++;
        if (_taken >= max)
        {
            return false;
        }

        _taken++;
        return true;
    }
}
