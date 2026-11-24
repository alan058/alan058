namespace StockFlow.Data;

public enum DataProvider
{
    /// <summary>In-process demo data. Nothing is saved when the app closes.</summary>
    InMemory,

    /// <summary>Microsoft SQL Server via the stored procedures in /database.</summary>
    SqlServer,
}

/// <summary>Bound from the "StockFlow:Data" configuration section.</summary>
public sealed class DataOptions
{
    public const string SectionName = "StockFlow:Data";

    public DataProvider Provider { get; set; } = DataProvider.InMemory;

    public string? ConnectionString { get; set; }

    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>Extra attempts after a transient failure (deadlock, dropped connection).</summary>
    public int MaxRetries { get; set; } = 3;

    public void Validate()
    {
        if (Provider == DataProvider.SqlServer && string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException(
                "StockFlow:Data:Provider is SqlServer but no ConnectionString is configured.");
        }

        if (CommandTimeoutSeconds <= 0)
        {
            throw new InvalidOperationException("CommandTimeoutSeconds must be greater than zero.");
        }

        if (MaxRetries < 0)
        {
            throw new InvalidOperationException("MaxRetries cannot be negative.");
        }
    }
}
