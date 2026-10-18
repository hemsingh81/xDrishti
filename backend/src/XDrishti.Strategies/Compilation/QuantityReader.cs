using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.Compilation;

/// <summary>Reads a <see cref="Quantity"/> of a field: a literal, or the value of a declared parameter; the problems of a quantity (<c>bad_quantity</c>, <c>unknown_param</c>) are reported at the path of the field.</summary>
internal sealed class QuantityReader(DeclaredParams declared, ProblemSink sink)
{
    /// <summary>The number the quantity stands for; false (with the problem reported) when it is neither a finite literal nor a declared parameter.</summary>
    public bool TryRead(Quantity quantity, string path, out double value)
    {
        value = 0;
        var hasNumber = quantity.Number.HasValue;
        if (hasNumber == (quantity.Param is not null))
        {
            sink.Error(path, ModelCodes.BadQuantity, "A quantity is either a number or the name of a parameter; here " + (hasNumber ? "it is both" : "it is neither") + ".");
            return false;
        }

        if (quantity.Param is { } name)
        {
            if (!declared.TryGet(name, out value))
            {
                var close = declared.Closest(name);
                sink.Error(
                    path,
                    ModelCodes.UnknownParam,
                    "`" + name + "` is not a parameter of the strategy." + (close is null ? string.Empty : " Did you mean `" + close + "`?"),
                    close);
                return false;
            }

            return true;
        }

        value = quantity.Number!.Value;
        if (!double.IsFinite(value))
        {
            sink.Error(path, ModelCodes.BadQuantity, "A quantity must be a finite number.");
            return false;
        }

        return true;
    }
}
