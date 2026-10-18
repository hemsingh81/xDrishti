using System.Reflection;
using XDrishti.Domain.Research;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.Tests.Model;

/// <summary>
/// Criterion 1: the model of schema 1 is a stored contract (ADR 0002). The text rendered from the code (the public types of <c>XDrishti.Strategies.Model</c> and <c>Side</c>, the exit tables, the filter names and the
/// problem codes) must equal the literal written by hand in <see cref="ModelSchemaSnapshot"/>.
/// </summary>
public sealed class ModelSnapshotTests
{
    private static readonly Assembly Library = typeof(StrategyDefinition).Assembly;

    [Fact]
    public void The_model_the_exit_tables_the_filters_and_the_codes_equal_the_snapshot_and_a_change_fails_with_a_message_about_a_stored_contract()
    {
        var rendered = Render();

        var expected = ModelSchemaSnapshot.Text.ReplaceLineEndings("\n");
        if (rendered != expected)
        {
            var differences = rendered.Split('\n').Except(expected.Split('\n')).Select(l => "  + " + l).Concat(expected.Split('\n').Except(rendered.Split('\n')).Select(l => "  - " + l));
            throw new ShouldAssertException("The model of schema 1 is a stored contract (ADR 0002): a stored strategy would break. To change it deliberately, raise StrategyDefinition.CurrentSchema, write the ADR and update ModelSchemaSnapshot. Differences (+ in the code, - in the snapshot):\n" + string.Join("\n", differences));
        }
    }

    [Fact]
    public void The_current_schema_is_one()
    {
        StrategyDefinition.CurrentSchema.ShouldBe(1);
    }

    [Fact]
    public void Every_model_type_is_a_sealed_record_a_readonly_record_struct_or_an_enum_and_the_namespace_has_one_public_type_per_file()
    {
        var types = Library.GetExportedTypes().Where(t => t.Namespace == "XDrishti.Strategies.Model").ToList();

        types.ShouldNotBeEmpty();
        foreach (var type in types)
        {
            var isRecord = type.GetMethod("<Clone>$") is not null || (type.IsValueType && type.GetMethod("PrintMembers", BindingFlags.NonPublic | BindingFlags.Instance) is not null);
            (type.IsEnum || (isRecord && (type.IsSealed || type.IsValueType))).ShouldBeTrue(type.FullName + " is neither an enum nor a sealed record nor a readonly record struct");
            if (type.IsValueType && !type.IsEnum)
            {
                type.GetCustomAttribute<System.Runtime.CompilerServices.IsReadOnlyAttribute>().ShouldNotBeNull(type.FullName + " is a struct that is not readonly");
            }
        }
    }

    private static string Render()
    {
        var lines = new List<string> { "schema " + StrategyDefinition.CurrentSchema };
        lines.Add(EnumLine("XDrishti.Domain.Research.Side", typeof(Side)));
        var model = Library.GetExportedTypes().Where(t => t.Namespace == "XDrishti.Strategies.Model").OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
        lines.AddRange(model.Where(t => t.IsEnum).Select(t => EnumLine(t.Name, t)));
        foreach (var type in model.Where(t => !t.IsEnum))
        {
            lines.Add("record " + type.Name + "(" + string.Join(", ", ConstructorOf(type).GetParameters().Select(p => TypeName(p.ParameterType, Nullability(p)) + " " + p.Name)) + ")");
            lines.AddRange(type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => "const " + type.Name + "." + f.Name + " = " + f.GetRawConstantValue()));
        }

        foreach (var (policy, parameters) in ExitTables.Checked.OrderBy(e => e.Key))
        {
            lines.AddRange(parameters.Select(p => "exit " + ExitTables.PolicyName(policy) + ": " + p.Name + " " + (p.Shape == ParameterShape.List ? "list" : "scalar") + (p.Required ? string.Empty : " optional") + " " + p.Interval));
        }

        lines.Add("exit values: at most " + ExitTables.MaxListValues + " per list");
        lines.Add("filters: " + string.Join(" ", FilterNames.All));
        var codes = typeof(ModelCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).Order(StringComparer.Ordinal);
        lines.AddRange(codes.Select(c => "code " + c));
        return string.Join("\n", lines);
    }

    private static string EnumLine(string name, Type type) =>
        "enum " + name + ": " + string.Join(" ", Enum.GetValues(type).Cast<object>().Select(v => v + "=" + Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture)));

    private static ConstructorInfo ConstructorOf(Type type) =>
        type.GetConstructors().Where(c => !c.GetParameters().Any(p => p.ParameterType == type)).OrderByDescending(c => c.GetParameters().Length).First();

    private static bool Nullability(ParameterInfo parameter) => new NullabilityInfoContext().Create(parameter).ReadState == NullabilityState.Nullable;

    private static string TypeName(Type type, bool nullableReference)
    {
        var suffix = nullableReference && !type.IsValueType ? "?" : string.Empty;
        if (Nullable.GetUnderlyingType(type) is { } inner)
        {
            return TypeName(inner, false) + "?";
        }

        if (type.IsGenericType)
        {
            return type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)] + "<" + string.Join(", ", type.GetGenericArguments().Select(a => TypeName(a, false))) + ">" + suffix;
        }

        return type switch
        {
            _ when type == typeof(int) => "int",
            _ when type == typeof(double) => "double",
            _ when type == typeof(string) => "string" + suffix,
            _ => type.Name + suffix,
        };
    }
}
