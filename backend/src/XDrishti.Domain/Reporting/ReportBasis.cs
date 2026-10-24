namespace XDrishti.Domain.Reporting;

/// <summary>Which R a report is computed on. A report is never both.</summary>
public enum ReportBasis
{
    /// <summary>R from the filled prices, no charges taken off.</summary>
    Gross,

    /// <summary>R net of the stored charge sheet; allowed only once the owner has confirmed the sheet (P8-00 question 19).</summary>
    Net,
}
