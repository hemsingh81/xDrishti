using System.Runtime.CompilerServices;
using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;

namespace XDrishti.Strategies.Compilation;

/// <summary>
/// The small functions the compiled conditions are made of (spec P3-02, <i>Meaning</i>): Kleene's three-valued tables, the comparisons that answer "unknown" for a NaN, the lagged builtins, the division that is NaN
/// instead of infinite, and the one place that reads the feature frame. The expression tree of a condition only calls these (and does arithmetic on doubles), so each rule is written once, in plain code that a
/// test can walk through exhaustively, and the JIT inlines the calls. A truth value is an <see cref="int"/>: <see cref="True"/> 1, <see cref="False"/> 0, <see cref="Unknown"/> -1 (the numbers of
/// <see cref="Truth"/>); the functions assume their truth arguments are one of the three. A number that is not ready is <see cref="double.NaN"/>. Nothing here allocates, throws for a bad value or keeps state.
/// <para>
/// Public members of an internal type, so that the compiler of the expression trees finds them by name; nothing outside the library can call them.
/// </para>
/// </summary>
internal static class Kernel
{
    /// <summary>The condition does not hold.</summary>
    public const int False = 0;

    /// <summary>The condition holds.</summary>
    public const int True = 1;

    /// <summary>The condition cannot be decided: an operand is not ready.</summary>
    public const int Unknown = -1;

    /// <summary>The "not ready" bit of the state of a run (<see cref="StepRising"/>).</summary>
    public const int RunMissing = 2;

    /// <summary>The "a step failed" bit of the state of a run.</summary>
    public const int RunBroken = 1;

    private const long SecondsPerMinute = 60;

    // ---- Kleene's tables ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary><c>not x</c>: true and false swap, unknown stays unknown.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Not(int x) => x < 0 ? Unknown : 1 - x;

    /// <summary><c>a and b</c>: false if either is false (whatever the other is), else unknown if either is unknown, else true.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int And(int a, int b) => a == False || b == False ? False : (a | b) < 0 ? Unknown : True;

    /// <summary><c>a or b</c>: true if either is true (whatever the other is), else unknown if either is unknown, else false.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Or(int a, int b) => a == True || b == True ? True : (a | b) < 0 ? Unknown : False;

    /// <summary><c>a == b</c> on two true/false values: unknown when either is.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SameTruth(int a, int b) => (a | b) < 0 ? Unknown : a == b ? True : False;

    /// <summary><c>a != b</c> on two true/false values: unknown when either is.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int DifferentTruth(int a, int b) => (a | b) < 0 ? Unknown : a != b ? True : False;

    // ---- comparisons of numbers: unknown when either is NaN (not ready) -------------------------------------------------------------------------------------------------------------------------------

    /// <summary><c>a &lt; b</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Less(double a, double b) => a < b ? True : a >= b ? False : Unknown;

    /// <summary><c>a &lt;= b</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int LessOrEqual(double a, double b) => a <= b ? True : a > b ? False : Unknown;

    /// <summary><c>a &gt; b</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Greater(double a, double b) => a > b ? True : a <= b ? False : Unknown;

    /// <summary><c>a &gt;= b</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GreaterOrEqual(double a, double b) => a >= b ? True : a < b ? False : Unknown;

    /// <summary><c>a == b</c>: exact equality of the doubles (the codes of the calendar and of the regime are whole numbers).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Equal(double a, double b) => a == b ? True : double.IsNaN(a) || double.IsNaN(b) ? Unknown : False;

    /// <summary><c>a != b</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int NotEqual(double a, double b) => a == b ? False : double.IsNaN(a) || double.IsNaN(b) ? Unknown : True;

    // ---- the lagged builtins: each is one test over all its values, unknown when any of them is NaN --------------------------------------------------------------------------------------------------

    /// <summary><c>crosses_above(a, b)</c> from the values now (<paramref name="a0"/>, <paramref name="b0"/>) and one bar earlier: <c>a0 &gt; b0 and a1 &lt;= b1</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CrossesAbove(double a0, double b0, double a1, double b1) =>
        double.IsNaN(a0) || double.IsNaN(b0) || double.IsNaN(a1) || double.IsNaN(b1) ? Unknown : a0 > b0 && a1 <= b1 ? True : False;

    /// <summary><c>crosses_below(a, b)</c>: <c>a0 &lt; b0 and a1 &gt;= b1</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CrossesBelow(double a0, double b0, double a1, double b1) =>
        double.IsNaN(a0) || double.IsNaN(b0) || double.IsNaN(a1) || double.IsNaN(b1) ? Unknown : a0 < b0 && a1 >= b1 ? True : False;

    /// <summary><c>between(x, low, high)</c>, both ends included.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Between(double x, double low, double high) =>
        double.IsNaN(x) || double.IsNaN(low) || double.IsNaN(high) ? Unknown : low <= x && x <= high ? True : False;

    /// <summary>The state of a run after its first value: <see cref="RunMissing"/> when that value is not ready, else 0.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int RunStart(double first) => double.IsNaN(first) ? RunMissing : 0;

    /// <summary>The state a step of <c>rising</c> adds: <see cref="RunMissing"/> if either value is not ready, <see cref="RunBroken"/> unless <c>newer &gt; older</c> (strictly), else 0.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int StepRising(double newer, double older) => double.IsNaN(newer) || double.IsNaN(older) ? RunMissing : newer > older ? 0 : RunBroken;

    /// <summary>The state a step of <c>falling</c> adds: <see cref="RunMissing"/> if either value is not ready, <see cref="RunBroken"/> unless <c>newer &lt; older</c> (strictly), else 0.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int StepFalling(double newer, double older) => double.IsNaN(newer) || double.IsNaN(older) ? RunMissing : newer < older ? 0 : RunBroken;

    /// <summary>The truth of a run from its state: unknown if any value was not ready, else true unless a step failed.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int RunResult(int state) => (state & RunMissing) != 0 ? Unknown : state == 0 ? True : False;

    // ---- numbers ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary><c>a / b</c>, but NaN (not ready) when <paramref name="b"/> is zero, never an infinity or an exception.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Divide(double a, double b) => b == 0 ? double.NaN : a / b;

    // ---- the frame -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>The value of the feature as of the last closed bar of its timeframe (NaN while it is not ready).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Value(FeatureFrame frame, int handle) => frame[handle];

    /// <summary>The value the feature had <paramref name="lag"/> closed bars of its own timeframe ago (NaN while it was not ready).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ValueAt(FeatureFrame frame, int handle, int lag) => frame.At(handle, lag);

    /// <summary>
    /// <c>time</c>: the India clock minute (0 to 1439) at which the evaluated bar closed, which is the start of the last accepted 1-minute bar plus one minute — a bar that starts at 09:44 closes at 09:45 —, as a
    /// double so that the comparisons are the ones of the numbers. NaN (unknown) before the frame has accepted a bar. The clock is the domain's <see cref="SessionClock"/>, so that the offset to India time (a fixed
    /// UTC+05:30) and the wrap at midnight are written once for the whole system.
    /// </summary>
    public static double CloseMinute(FeatureFrame frame) => frame.Steps == 0 ? double.NaN : SessionClock.MinuteOfDay(frame.LastBarTime + SecondsPerMinute);
}
