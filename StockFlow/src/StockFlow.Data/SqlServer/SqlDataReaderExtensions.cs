using Microsoft.Data.SqlClient;

namespace StockFlow.Data.SqlServer;

/// <summary>Null-safe, name-based column access. Reading by name keeps mapping code robust to column order changes.</summary>
internal static class SqlDataReaderExtensions
{
    public static int GetInt32(this SqlDataReader reader, string column) => reader.GetInt32(reader.GetOrdinal(column));

    public static long GetInt64(this SqlDataReader reader, string column) => reader.GetInt64(reader.GetOrdinal(column));

    public static byte GetByte(this SqlDataReader reader, string column) => reader.GetByte(reader.GetOrdinal(column));

    public static bool GetBoolean(this SqlDataReader reader, string column) => reader.GetBoolean(reader.GetOrdinal(column));

    public static decimal GetDecimal(this SqlDataReader reader, string column) => reader.GetDecimal(reader.GetOrdinal(column));

    public static DateTime GetDateTime(this SqlDataReader reader, string column) => reader.GetDateTime(reader.GetOrdinal(column));

    public static string GetString(this SqlDataReader reader, string column) => reader.GetString(reader.GetOrdinal(column));

    public static string? GetNullableString(this SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    public static DateTime? GetNullableDateTime(this SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
    }

    public static byte[] GetBytes(this SqlDataReader reader, string column) => (byte[])reader.GetValue(reader.GetOrdinal(column));

    public static async Task<List<T>> ReadAllAsync<T>(this SqlDataReader reader, Func<SqlDataReader, T> map, CancellationToken cancellationToken)
    {
        var results = new List<T>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(map(reader));
        }

        return results;
    }

    public static object ToDbValue(this string? value) => string.IsNullOrEmpty(value) ? DBNull.Value : value;
}
