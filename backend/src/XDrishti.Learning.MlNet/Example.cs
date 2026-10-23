using Microsoft.ML;
using Microsoft.ML.Data;

namespace XDrishti.Learning.MlNet;

/// <summary>One row as ML.NET reads it: a dense float vector and the label.</summary>
internal sealed class Example
{
    public float[] Features { get; set; } = [];

    public bool Label { get; set; }

    /// <summary>A data view with a fixed vector width (the width is a property of the schema, not of the rows).</summary>
    public static IDataView View(MLContext ml, IEnumerable<Example> rows, int width)
    {
        var schema = SchemaDefinition.Create(typeof(Example));
        schema[nameof(Features)].ColumnType = new VectorDataViewType(NumberDataViewType.Single, width);
        return ml.Data.LoadFromEnumerable(rows, schema);
    }
}
