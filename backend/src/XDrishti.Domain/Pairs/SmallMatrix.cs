namespace XDrishti.Domain.Pairs;

/// <summary>Inversion of the small symmetric normal-equation matrices of the test regressions (Gauss-Jordan, partial pivoting, fixed order, so the result is deterministic).</summary>
internal static class SmallMatrix
{
    /// <summary>The inverse of the row-major <paramref name="a"/> (k x k), or null when it is singular to working precision.</summary>
    internal static double[]? Invert(double[] a, int k)
    {
        var m = (double[])a.Clone();
        var inv = new double[k * k];
        double scale = 0;
        for (var i = 0; i < k; i++)
        {
            inv[(i * k) + i] = 1.0;
            scale = Math.Max(scale, Math.Abs(m[(i * k) + i]));
        }

        if (!(scale > 0))
        {
            return null;
        }

        for (var c = 0; c < k; c++)
        {
            var pivotRow = c;
            for (var r = c + 1; r < k; r++)
            {
                if (Math.Abs(m[(r * k) + c]) > Math.Abs(m[(pivotRow * k) + c]))
                {
                    pivotRow = r;
                }
            }

            if (Math.Abs(m[(pivotRow * k) + c]) <= 1e-12 * scale)
            {
                return null;
            }

            if (pivotRow != c)
            {
                for (var j = 0; j < k; j++)
                {
                    (m[(c * k) + j], m[(pivotRow * k) + j]) = (m[(pivotRow * k) + j], m[(c * k) + j]);
                    (inv[(c * k) + j], inv[(pivotRow * k) + j]) = (inv[(pivotRow * k) + j], inv[(c * k) + j]);
                }
            }

            var pivot = m[(c * k) + c];
            for (var j = 0; j < k; j++)
            {
                m[(c * k) + j] /= pivot;
                inv[(c * k) + j] /= pivot;
            }

            for (var r = 0; r < k; r++)
            {
                if (r == c)
                {
                    continue;
                }

                var f = m[(r * k) + c];
                if (f == 0)
                {
                    continue;
                }

                for (var j = 0; j < k; j++)
                {
                    m[(r * k) + j] -= f * m[(c * k) + j];
                    inv[(r * k) + j] -= f * inv[(c * k) + j];
                }
            }
        }

        return inv;
    }
}
