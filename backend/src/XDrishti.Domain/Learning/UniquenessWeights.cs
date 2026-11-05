using XDrishti.Domain.Common;

namespace XDrishti.Domain.Learning;

/// <summary>
/// Sample uniqueness (CF-10 part 10c-1, ADR 0216, spec D5; Lopez de Prado, Advances in Financial Machine Learning, ch. 4): a label spans the bars from its decision to its resolution; <c>u = mean(1 / c_t)</c> over its bars with <c>c_t</c> the number of labels spanning bar <c>t</c>.
/// Weights are the uniqueness scaled to mean one; the sum of the uniqueness is the effective count shown next to the number of labels. Three labels over bars 1-2, 2-3 and 3-4 give <c>c = (1, 2, 2, 1)</c>, <c>u = (0.75, 0.5, 0.75)</c>, effective count 2.0, weights <c>(1.125, 0.75, 1.125)</c>.
/// Done by a sweep over the start and end points, <c>O(n log n)</c> whatever the size of the bar numbers. Evidence only here: nothing trains on the weights. Pure.
/// </summary>
public static class UniquenessWeights
{
    /// <summary>The failure code for a span that cannot be read.</summary>
    public const string InvalidCode = "uniqueness.invalid_span";

    /// <summary>The uniqueness of each label; a span is its first and last bar, both included (a one-bar label has first equal to last).</summary>
    public static Result<UniquenessResult> Compute(IReadOnlyList<(long FirstBar, long LastBar)> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);
        var n = spans.Count;
        if (n == 0)
        {
            return Result.Success(new UniquenessResult([], [], 0));
        }

        var points = new long[2 * n];
        for (var i = 0; i < n; i++)
        {
            var (first, last) = spans[i];
            if (last < first || last == long.MaxValue)
            {
                return Result.Failure<UniquenessResult>(Error.Validation(InvalidCode, "a label ends before it starts"));
            }

            points[2 * i] = first;
            points[(2 * i) + 1] = last + 1;
        }

        Array.Sort(points);
        var m = 0;
        for (var i = 0; i < points.Length; i++)
        {
            if (i == 0 || points[i] != points[i - 1])
            {
                points[m++] = points[i];
            }
        }

        var open = new int[m];
        foreach (var (first, last) in spans)
        {
            open[Array.BinarySearch(points, 0, m, first)]++;
            open[Array.BinarySearch(points, 0, m, last + 1)]--;
        }

        // cumulative[j] is the sum of 1 / c over every bar before point j; c is constant between two points
        var cumulative = new double[m];
        var concurrent = 0;
        for (var j = 0; j + 1 < m; j++)
        {
            concurrent += open[j];
            cumulative[j + 1] = cumulative[j] + (concurrent > 0 ? (double)(points[j + 1] - points[j]) / concurrent : 0);
        }

        var uniqueness = new double[n];
        double sum = 0;
        for (var i = 0; i < n; i++)
        {
            var (first, last) = spans[i];
            var from = Array.BinarySearch(points, 0, m, first);
            var to = Array.BinarySearch(points, 0, m, last + 1);
            uniqueness[i] = (cumulative[to] - cumulative[from]) / (last + 1 - first);
            sum += uniqueness[i];
        }

        var weights = new double[n];
        for (var i = 0; i < n; i++)
        {
            weights[i] = uniqueness[i] * n / sum;
        }

        return Result.Success(new UniquenessResult(uniqueness, weights, sum));
    }
}
