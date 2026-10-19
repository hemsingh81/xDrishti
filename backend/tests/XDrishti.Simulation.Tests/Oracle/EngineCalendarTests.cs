using XDrishti.Simulation.Walking;
using static XDrishti.Simulation.Tests.Oracle.Fx;

namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>Rows K1 to K7 of spec P3-03d, the engine's side: the due session and the due minute of the author's data (<c>CalendarTests.KRows</c>), through the adapter's calendar and the engine's <c>SwingHold</c>.</summary>
public sealed class EngineCalendarTests
{
    public static TheoryData<string, string, int, string, string, int> KRows() => CalendarTests.KRows();

    private static OracleCalendar CalendarOf(string kind) => kind switch
    {
        "holiday-tue" => Cal([Tue]),
        "special-sat" => Cal([], (Sat, "09:15", "12:30")),
        _ => OracleCalendar.Default,
    };

    [Theory]
    [MemberData(nameof(KRows))]
    public void The_engine_finds_the_due_session_and_the_due_minute_of_the_authors_rows(string id, string entry, int n, string calendar, string due, int dueMinute)
    {
        // the author's rows name no calendar for the default week: the adapter maps the oracle's default to the engine's, and a null calendar is the engine's default week too
        var hold = SwingHold.Create(n, calendar == "none" ? null : OracleAdapter.Calendar(CalendarOf(calendar))).Value;

        var found = hold.DueSession(D(entry));

        found.IsSuccess.ShouldBeTrue(id);
        found.Value.ShouldBe(D(due), id);
        hold.DueMinute(found.Value).ShouldBe(dueMinute, id);
    }
}
