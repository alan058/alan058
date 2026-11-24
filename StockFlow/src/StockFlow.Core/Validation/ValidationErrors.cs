using StockFlow.Core.Exceptions;

namespace StockFlow.Core.Validation;

/// <summary>Collects every validation failure so the user sees them all at once, not one per attempt.</summary>
public sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool HasErrors => _errors.Count > 0;

    public ValidationErrors Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var list))
        {
            list = new List<string>();
            _errors[field] = list;
        }

        list.Add(message);
        return this;
    }

    public ValidationErrors AddIf(bool condition, string field, string message) =>
        condition ? Add(field, message) : this;

    public void ThrowIfAny()
    {
        if (HasErrors)
        {
            throw new BusinessValidationException(
                _errors.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal));
        }
    }
}
