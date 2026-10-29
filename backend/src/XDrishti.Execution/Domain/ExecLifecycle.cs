using System.Collections.Frozen;
using XDrishti.Domain.Common;

namespace XDrishti.Execution.Domain;

/// <summary>The legal moves between <see cref="ExecState"/>s: exactly 23 of the 196 ordered pairs. Rejected, cancelled, failed and reconciled are final.</summary>
public static class ExecLifecycle
{
    public const string IllegalMoveCode = "execution.illegal_move";

    private static readonly (ExecState From, ExecState To)[] Table =
    [
        (ExecState.Planned, ExecState.Armed), (ExecState.Planned, ExecState.Cancelled),
        (ExecState.Armed, ExecState.Checking), (ExecState.Armed, ExecState.Cancelled),
        (ExecState.Checking, ExecState.Rejected), (ExecState.Checking, ExecState.Submitting), (ExecState.Checking, ExecState.Cancelled),
        (ExecState.Submitting, ExecState.Open), (ExecState.Submitting, ExecState.Failed),
        (ExecState.Open, ExecState.Filled), (ExecState.Open, ExecState.Cancelled), (ExecState.Open, ExecState.Failed),
        (ExecState.Filled, ExecState.Protected), (ExecState.Filled, ExecState.Closing), (ExecState.Filled, ExecState.Closed),
        (ExecState.Protected, ExecState.Managing), (ExecState.Protected, ExecState.Closing), (ExecState.Protected, ExecState.Closed),
        (ExecState.Managing, ExecState.Closing), (ExecState.Managing, ExecState.Closed),
        (ExecState.Closing, ExecState.Closed), (ExecState.Closing, ExecState.Protected),
        (ExecState.Closed, ExecState.Reconciled),
    ];

    private static readonly FrozenSet<(ExecState, ExecState)> Set = Table.ToFrozenSet();

    public static IReadOnlyList<(ExecState From, ExecState To)> LegalMoves => Table;

    public static bool IsLegal(ExecState from, ExecState to) => Set.Contains((from, to));

    public static bool IsFinal(ExecState state) => Table.All(m => m.From != state);

    public static IReadOnlyList<ExecState> LegalFrom(ExecState from) => [.. Table.Where(m => m.From == from).Select(m => m.To)];

    /// <summary>Success for a legal move; otherwise a refusal naming the legal moves from <paramref name="from"/>.</summary>
    public static Result Check(ExecState from, ExecState to) =>
        IsLegal(from, to)
            ? Result.Success()
            : Result.Failure(Error.Conflict(IllegalMoveCode, $"{from} to {to} is not a legal move; legal from {from}: {(IsFinal(from) ? "none (final)" : string.Join(", ", LegalFrom(from)))}."));
}
