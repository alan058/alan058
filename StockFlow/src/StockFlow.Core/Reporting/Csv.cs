using System.Globalization;

namespace StockFlow.Core.Reporting;

/// <summary>CSV helpers shared by the web and desktop exports.</summary>
public static class Csv
{
    /// <summary>
    /// Quotes a field and neutralises a leading =, +, - or @ so a spreadsheet
    /// does not run the value as a formula (CSV injection).
    /// </summary>
    public static string Escape(string? value)
    {
        value ??= string.Empty;
        if (value.Length > 0 && "=+-@".Contains(value[0], StringComparison.Ordinal))
        {
            value = "'" + value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    /// <summary>Builds one CSV line. Text is quoted; numbers are written in invariant culture.</summary>
    public static string Line(params object?[] values) =>
        string.Join(',', values.Select(v => v switch
        {
            null => string.Empty,
            string s => Escape(s),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => Escape(v.ToString()),
        }));
}
