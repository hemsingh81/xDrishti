using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.Cli;

/// <summary>The parsed command line of <c>xd learn designate</c> (spec P6-07). Nothing here reads a file or the database.</summary>
internal sealed record LearnDesignateOptions(long ModelId, ModelRole Role, string Reason)
{
    public const string Usage = """
        usage:
          xd learn designate --model ID --role champion|challenger|retired --reason TEXT
            appends a designation; only a model whose verdict is Eligible can be named champion, and naming one retires the previous champion
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    private static readonly string[] Known = ["--model", "--role", "--reason"];

    /// <summary>Parses the arguments after <c>learn designate</c>; the errors are listed when the options are null.</summary>
    public static (LearnDesignateOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var errors = new List<string>();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (!Known.Contains(arg, StringComparer.Ordinal))
            {
                errors.Add($"unknown argument '{SimulateOptions.Show(arg)}'");
            }
            else if (i + 1 >= args.Count)
            {
                errors.Add($"'{arg}' needs a value");
            }
            else if (!values.TryAdd(arg, args[++i]))
            {
                errors.Add($"'{arg}' is given twice");
            }
        }

        long id = 0;
        if (!values.TryGetValue("--model", out var idText) || !long.TryParse(idText, NumberStyles.None, CultureInfo.InvariantCulture, out id) || id < 1)
        {
            errors.Add("--model is the number of a registered model");
        }

        var role = values.TryGetValue("--role", out var roleText) ? ModelRoleExtensions.Parse(roleText) : null;
        if (role is null)
        {
            errors.Add("--role is champion, challenger or retired");
        }

        if (!values.TryGetValue("--reason", out var reason) || string.IsNullOrWhiteSpace(reason))
        {
            errors.Add("--reason is required");
        }

        return errors.Count > 0 ? (null, errors) : (new LearnDesignateOptions(id, role!.Value, reason!), errors);
    }
}
