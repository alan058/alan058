namespace StockFlow.Core.Exceptions;

/// <summary>
/// Base class for expected, business-level failures. The message is safe to
/// show to a user. Anything that is NOT a StockFlowException is treated as an
/// unexpected fault: logged with full detail and shown as a generic message.
/// </summary>
public abstract class StockFlowException : Exception
{
    protected StockFlowException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>A record that was asked for does not exist.</summary>
public sealed class EntityNotFoundException : StockFlowException
{
    public EntityNotFoundException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }

    public static EntityNotFoundException For(string entityName, object id) =>
        new($"{entityName} {id} was not found.");
}

/// <summary>There is not enough available stock to complete the operation.</summary>
public sealed class InsufficientStockException : StockFlowException
{
    public InsufficientStockException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>The order is not in a state that allows the requested action.</summary>
public sealed class InvalidOrderStateException : StockFlowException
{
    public InvalidOrderStateException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>Someone else changed the record after it was loaded.</summary>
public sealed class ConcurrencyException : StockFlowException
{
    public ConcurrencyException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>Input failed one or more business rules. <see cref="Errors"/> is keyed by field name.</summary>
public sealed class BusinessValidationException : StockFlowException
{
    public BusinessValidationException(string message, Exception? innerException = null)
        : this(new Dictionary<string, string[]> { [string.Empty] = new[] { message } }, innerException)
    {
    }

    public BusinessValidationException(IReadOnlyDictionary<string, string[]> errors, Exception? innerException = null)
        : base(BuildMessage(errors), innerException)
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }

    private static string BuildMessage(IReadOnlyDictionary<string, string[]> errors) =>
        string.Join(" ", errors.SelectMany(e => e.Value));
}

/// <summary>
/// An infrastructure failure talking to the database (not a business rule).
/// <see cref="IsTransient"/> tells the caller whether trying again may work.
/// </summary>
public sealed class DataAccessException : Exception
{
    public DataAccessException(string message, bool isTransient, Exception? innerException = null)
        : base(message, innerException)
    {
        IsTransient = isTransient;
    }

    public bool IsTransient { get; }
}
