using XDrishti.Domain.Platform.Services;
using static XDrishti.Domain.Tests.Platform.Services.ServiceTestData;

namespace XDrishti.Domain.Tests.Platform.Services;

public sealed class ScheduleWordsTests
{
    private static string Describe(Schedule schedule, bool afterSession = false) => ScheduleWords.Describe(schedule, id => id == "eod" ? "End-of-day data run" : id, afterSession);

    [Fact]
    public void Every_schedule_type_has_words()
    {
        Describe(Schedule.Daily(T(15, 50))).ShouldBe("Every trading day at 15:50 IST");
        Describe(Schedule.Daily(T(15, 50)), afterSession: true).ShouldBe("Every trading day at 15:50 IST, never before the session has ended");
        Describe(Schedule.Daily(T(8, 45), false, Weekdays.EveryDay)).ShouldBe("Every day at 08:45 IST");
        Describe(Schedule.Daily(T(10, 0), false, Weekdays.MondayToFriday)).ShouldBe("Monday to Friday at 10:00 IST");
        Describe(Schedule.Daily(T(10, 0), false, Weekdays.Monday | Weekdays.Wednesday)).ShouldBe("Monday, Wednesday at 10:00 IST");
        Describe(Schedule.Weekly(DayOfWeek.Saturday, T(10, 0))).ShouldBe("Every Saturday at 10:00 IST");
        Describe(Schedule.Weekly(DayOfWeek.Monday, T(9, 0), true)).ShouldBe("Every Monday at 09:00 IST (if it is a trading day)");
        Describe(Schedule.AfterService("eod")).ShouldBe("Each time End-of-day data run finishes successfully");
        Describe(Schedule.Window(T(9, 0), T(15, 35))).ShouldBe("Up from 09:00 to 15:35 IST on trading days");
        Describe(Schedule.Interval(5, T(9, 15), T(15, 30))).ShouldBe("Every 5 min from 09:15 to 15:30 IST on trading days");
        Describe(Schedule.Continuous()).ShouldBe("Always on");
    }
}
