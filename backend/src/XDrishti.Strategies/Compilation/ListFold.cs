using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.Compilation;

/// <summary>The truth of a list from the truths of its items, by Kleene's tables (the same functions the compiled conditions use): <c>all</c> is the AND of the items, <c>any</c> the OR; the fold of none is true for <c>all</c> and false for <c>any</c> (the model forbids empty lists, so it is internal).</summary>
internal static class ListFold
{
    public static Truth Fold(ReadOnlySpan<Truth> items, ListMode mode)
    {
        if (mode == ListMode.Any)
        {
            var any = Kernel.False;
            foreach (var item in items)
            {
                any = Kernel.Or(any, (int)item);
            }

            return (Truth)any;
        }

        var all = Kernel.True;
        foreach (var item in items)
        {
            all = Kernel.And(all, (int)item);
        }

        return (Truth)all;
    }
}
