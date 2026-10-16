using System.Collections.Immutable;

namespace XDrishti.Strategies.Syntax;

/// <summary>Equality of the lists inside nodes: element by element, an uninitialised array counting as empty.</summary>
internal static class NodeEquality
{
    public static bool SequenceEqual(ImmutableArray<Argument> left, ImmutableArray<Argument> right)
    {
        var a = left.IsDefault ? [] : left;
        var b = right.IsDefault ? [] : right;
        if (a.Length != b.Length)
        {
            return false;
        }

        for (var i = 0; i < a.Length; i++)
        {
            if (!a[i].Equals(b[i]))
            {
                return false;
            }
        }

        return true;
    }

    public static int SequenceHash(ImmutableArray<Argument> items)
    {
        var hash = new HashCode();
        if (!items.IsDefault)
        {
            foreach (var item in items)
            {
                hash.Add(item);
            }
        }

        return hash.ToHashCode();
    }
}
