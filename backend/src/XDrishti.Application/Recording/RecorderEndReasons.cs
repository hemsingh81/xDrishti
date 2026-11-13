namespace XDrishti.Application.Recording;

/// <summary>How a recorder's day ended (the value of the <c>end_reason</c> column of a recorder's sessions table).</summary>
public static class RecorderEndReasons
{
    public const string WindowEnd = "window_end";

    public const string Paused = "paused";

    public const string DiskBudget = "disk_budget";

    public const string DiskFree = "disk_free";

    public const string TokenRefused = "token_refused";

    public const string NotSubscribed = "not_subscribed";

    public const string Flapping = "flapping";

    public const string Crashed = "crashed";
}
