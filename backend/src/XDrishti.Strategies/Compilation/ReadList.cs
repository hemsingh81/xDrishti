using XDrishti.Indicators;
using XDrishti.Strategies.Binding;

namespace XDrishti.Strategies.Compilation;

/// <summary>The reads of a bound tree in text order, with the lag each is read at, cut at <see cref="CompiledCondition.MaxReads"/>.</summary>
internal sealed class ReadList
{
    private readonly List<ConditionRead> _items = [];
    private readonly IReadOnlyList<FeatureSpec> _features;

    private ReadList(IReadOnlyList<FeatureSpec> features) => _features = features;

    public IReadOnlyList<ConditionRead> Items => _items;

    public bool Truncated { get; private set; }

    public static ReadList Of(IBoundExpression tree, IReadOnlyList<FeatureSpec> features)
    {
        var list = new ReadList(features);
        list.Collect(tree, 0);
        return list;
    }

    private void Collect(IBoundExpression node, int offset)
    {
        switch (node)
        {
            case BoundFeature feature:
                Add(feature, offset);
                break;
            case BoundCrossing crossing:
                // the pair at the latest bar, then the pair one bar back (the evaluation order of the kernel)
                Collect(crossing.Left, offset);
                Collect(crossing.Right, offset);
                Collect(crossing.Left, offset + 1);
                Collect(crossing.Right, offset + 1);
                break;
            case BoundRun run:
                for (var lag = 0; lag <= run.Count && !Truncated; lag++)
                {
                    Collect(run.Operand, offset + lag);
                }

                break;
            default:
                foreach (var child in BoundTree.Children(node))
                {
                    Collect(child, offset);
                }

                break;
        }
    }

    private void Add(BoundFeature feature, int offset)
    {
        if (_items.Count >= CompiledCondition.MaxReads)
        {
            Truncated = true;
            return;
        }

        var index = -1;
        for (var i = 0; i < _features.Count; i++)
        {
            if (_features[i].Equals(feature.Spec))
            {
                index = i;
                break;
            }
        }

        _items.Add(new ConditionRead(index, CompiledCondition.Describe(feature.Spec), feature.Spec.Timeframe, feature.Lag + offset));
    }
}
