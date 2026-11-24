using Microsoft.Data.SqlClient;
using StockFlow.Core.Exceptions;

namespace StockFlow.Data.SqlServer;

/// <summary>
/// Turns SqlException into the application's exception types, so callers never
/// need to know SQL Server error numbers. Custom numbers (5000x) are raised by
/// the stored procedures with THROW.
/// </summary>
public static class SqlErrorTranslator
{
    public const int InsufficientStock = 50001;
    public const int InvalidOrderState = 50002;
    public const int ConcurrencyConflict = 50003;
    public const int NotFound = 50004;
    public const int ValidationFailed = 50005;

    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;
    private const int ForeignKeyOrCheckViolation = 547;

    /// <summary>
    /// Errors where the statement did not take effect and trying again is safe:
    /// deadlock victim (1205), and connection-level failures that happen before
    /// or instead of execution. Command timeouts (-2) are deliberately NOT
    /// retried: the command may still be running, and retrying a slow query
    /// only adds load. Timeouts need investigating, not hiding.
    /// </summary>
    private static readonly HashSet<int> TransientErrors = new()
    {
        1205,   // Deadlock victim
        4060,   // Cannot open database
        40197,  // Service error processing request (Azure SQL)
        40501,  // Service busy (Azure SQL)
        40613,  // Database unavailable (Azure SQL)
        49918, 49919, 49920,
        233,    // Connection closed by server
        64,     // Connection dropped
        10053, 10054, 10060, // Transport-level errors
    };

    private const int DeadlockVictim = 1205;

    /// <summary>
    /// Decides whether a failed call can be repeated without side effects.
    /// A deadlock victim's transaction is always rolled back, so it is safe at
    /// any point. Other transient errors are only safe while the connection was
    /// still being opened: once a write has been sent, a dropped connection may
    /// have happened AFTER the commit, and repeating it could, for example,
    /// create the same order twice.
    /// </summary>
    public static bool IsSafeToRetry(SqlException exception, bool connectionOpened)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.Number == DeadlockVictim || (!connectionOpened && IsTransient(exception));
    }

    public static bool IsTransient(SqlException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.Errors.Cast<SqlError>().Any(e => TransientErrors.Contains(e.Number));
    }

    public static Exception Translate(SqlException exception, string operation)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.Number switch
        {
            InsufficientStock => new InsufficientStockException(exception.Message, exception),
            InvalidOrderState => new InvalidOrderStateException(exception.Message, exception),
            ConcurrencyConflict => new ConcurrencyException(exception.Message, exception),
            NotFound => new EntityNotFoundException(exception.Message, exception),
            ValidationFailed => new BusinessValidationException(exception.Message, exception),
            UniqueIndexViolation or UniqueConstraintViolation =>
                new BusinessValidationException("A record with the same key already exists.", exception),
            ForeignKeyOrCheckViolation =>
                new BusinessValidationException("The change was rejected because it would break a data rule (a related record is missing or a value is out of range).", exception),
            _ => new DataAccessException(
                $"Database error during {operation} (SQL error {exception.Number}).",
                IsTransient(exception),
                exception),
        };
    }
}
