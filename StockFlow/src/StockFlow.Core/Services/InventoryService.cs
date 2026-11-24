using StockFlow.Core.Abstractions;
using StockFlow.Core.Models;
using StockFlow.Core.Validation;

namespace StockFlow.Core.Services;

public sealed class InventoryService
{
    /// <summary>
    /// Guard against keying errors (e.g. a barcode scanned into the quantity box).
    /// Larger movements must be split or done by an administrator in SQL.
    /// </summary>
    public const int MaxQuantityPerMovement = 100_000;

    private readonly IInventoryRepository _inventory;

    public InventoryService(IInventoryRepository inventory)
    {
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
    }

    /// <summary>Books stock in from a supplier delivery.</summary>
    public Task<StockLevel> ReceiveAsync(int productId, int warehouseId, int quantity, string? reference, string user, CancellationToken cancellationToken = default)
    {
        var errors = new ValidationErrors()
            .AddIf(quantity <= 0, "Quantity", "Quantity received must be greater than zero.")
            .AddIf(quantity > MaxQuantityPerMovement, "Quantity", $"Quantity cannot exceed {MaxQuantityPerMovement:N0} in one receipt.");
        AddCommonErrors(errors, productId, warehouseId, user);
        errors.ThrowIfAny();

        return _inventory.AdjustAsync(new StockAdjustment
        {
            ProductId = productId,
            WarehouseId = warehouseId,
            MovementType = MovementType.Receipt,
            QuantityChange = quantity,
            Reference = Clean(reference),
            Reason = "Goods received",
        }, user, cancellationToken);
    }

    /// <summary>Corrects stock after a count, damage or loss. A reason is mandatory for the audit trail.</summary>
    public Task<StockLevel> AdjustAsync(int productId, int warehouseId, int quantityChange, string? reason, string user, CancellationToken cancellationToken = default)
    {
        var errors = new ValidationErrors()
            .AddIf(quantityChange == 0, "QuantityChange", "Enter a positive or negative quantity.")
            .AddIf(Math.Abs((long)quantityChange) > MaxQuantityPerMovement, "QuantityChange", $"An adjustment cannot exceed {MaxQuantityPerMovement:N0} units.")
            .AddIf(string.IsNullOrWhiteSpace(reason), "Reason", "A reason is required for stock adjustments.")
            .AddIf(reason?.Trim().Length > 200, "Reason", "Reason must be 200 characters or fewer.");
        AddCommonErrors(errors, productId, warehouseId, user);
        errors.ThrowIfAny();

        return _inventory.AdjustAsync(new StockAdjustment
        {
            ProductId = productId,
            WarehouseId = warehouseId,
            MovementType = MovementType.Adjustment,
            QuantityChange = quantityChange,
            Reason = Clean(reason),
        }, user, cancellationToken);
    }

    private static void AddCommonErrors(ValidationErrors errors, int productId, int warehouseId, string user)
    {
        errors.AddIf(productId <= 0, "ProductId", "Choose a product.")
              .AddIf(warehouseId <= 0, "WarehouseId", "Choose a warehouse.")
              .AddIf(string.IsNullOrWhiteSpace(user), "User", "The current user is unknown.");
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
