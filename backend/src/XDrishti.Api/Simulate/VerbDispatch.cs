namespace XDrishti.Api.Simulate;

/// <summary>
/// Which verb the arguments select (spec P3-04b2, D2). Pure. <c>simulate</c> is selected only when it is the first argument and is tested first, so that a symbol list (<c>simulate --symbols migrate</c>) or a stray <c>--healthcheck</c> never selects
/// another verb; the older verbs keep their rule (<c>--healthcheck</c>, <c>migrate</c> and <c>owner-reset</c> match anywhere in the arguments, in that order).
/// </summary>
internal static class VerbDispatch
{
    public const string SimulateName = "simulate";

    public const string CostSheetName = "cost-sheet";

    public const string VerifyPackName = "verify-pack";

    public static Verb Of(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Count > 0 && string.Equals(args[0], SimulateName, StringComparison.Ordinal))
        {
            return Verb.Simulate;
        }

        if (args.Count > 0 && string.Equals(args[0], CostSheetName, StringComparison.Ordinal))
        {
            return Verb.CostSheet;
        }

        if (args.Count > 0 && string.Equals(args[0], VerifyPackName, StringComparison.Ordinal))
        {
            return Verb.VerifyPack;
        }

        if (args.Contains("--healthcheck", StringComparer.Ordinal))
        {
            return Verb.HealthCheck;
        }

        if (args.Contains("migrate", StringComparer.Ordinal))
        {
            return Verb.Migrate;
        }

        return args.Contains("owner-reset", StringComparer.Ordinal) ? Verb.OwnerReset : Verb.Serve;
    }
}
