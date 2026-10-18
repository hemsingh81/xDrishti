using System.Reflection;

namespace XDrishti.SimExport.Tests.Support;

/// <summary>
/// The types reachable from a record through its public instance properties (generic arguments, nullable and collection element types included), found by reflection: how the tests learn which enums and which dictionaries the exported
/// record holds today, so that a member added by a later slice shows up here instead of being written by default.
/// </summary>
internal static class RecordGraph
{
    public static IReadOnlySet<Type> From(Type root)
    {
        var seen = new HashSet<Type>();
        var pending = new Stack<Type>([root]);
        while (pending.Count > 0)
        {
            var type = pending.Pop();
            if (!seen.Add(type))
            {
                continue;
            }

            if (type.IsGenericType)
            {
                foreach (var argument in type.GetGenericArguments())
                {
                    pending.Push(argument);
                }
            }

            if (type.IsArray)
            {
                pending.Push(type.GetElementType()!);
            }

            if (!IsOurs(type))
            {
                continue;
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                pending.Push(property.PropertyType);
            }
        }

        return seen;
    }

    public static IReadOnlyList<Type> Enums(Type root) => [.. From(root).Where(t => t.IsEnum).OrderBy(t => t.FullName, StringComparer.Ordinal)];

    /// <summary>The public instance properties, anywhere in the graph, whose type is a dictionary (generic or not).</summary>
    public static IReadOnlyList<string> DictionaryProperties(Type root) =>
    [
        .. From(root).Where(IsOurs).SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => IsDictionary(p.PropertyType)).Select(p => t.Name + "." + p.Name)).Order(StringComparer.Ordinal),
    ];

    private static bool IsOurs(Type type) => type.Namespace?.StartsWith("XDrishti.", StringComparison.Ordinal) == true && !type.IsEnum;

    private static bool IsDictionary(Type type) =>
        type == typeof(System.Collections.IDictionary)
        || type.GetInterfaces().Append(type).Any(i => i == typeof(System.Collections.IDictionary) || (i.IsGenericType && i.GetGenericTypeDefinition() is var d && (d == typeof(IDictionary<,>) || d == typeof(IReadOnlyDictionary<,>))));
}
