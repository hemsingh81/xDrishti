namespace XDrishti.Domain.Platform.Services;

/// <summary>A set of days of the week.</summary>
[Flags]
public enum Weekdays
{
    None = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64,
    MondayToFriday = Monday | Tuesday | Wednesday | Thursday | Friday,
    EveryDay = MondayToFriday | Saturday | Sunday,
}
