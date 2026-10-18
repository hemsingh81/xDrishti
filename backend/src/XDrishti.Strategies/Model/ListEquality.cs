namespace XDrishti.Strategies.Model;

/// <summary>Equality of lists by element, for the records of the model (a record compares a list by reference; the model wants value equality, ADR 0036). Null is a list of its own: not equal to an empty one.</summary>
internal static class ListEquality
{
    public static bool Equal<T>(IReadOnlyList<T>? first, IReadOnlyList<T>? second)
    {
        if (ReferenceEquals(first, second))
        {
            return true;
        }

        if (first is null || second is null || first.Count != second.Count)
        {
            return false;
        }

        var comparer = EqualityComparer<T>.Default;
        for (var i = 0; i < first.Count; i++)
        {
            if (!comparer.Equals(first[i], second[i]))
            {
                return false;
            }
        }

        return true;
    }

    public static int Hash<T>(IReadOnlyList<T>? list)
    {
        if (list is null)
        {
            return 0;
        }

        var hash = new HashCode();
        hash.Add(list.Count);
        foreach (var item in list)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }
}
