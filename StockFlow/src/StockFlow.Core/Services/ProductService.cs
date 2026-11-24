using System.Text.RegularExpressions;
using StockFlow.Core.Abstractions;
using StockFlow.Core.Exceptions;
using StockFlow.Core.Models;
using StockFlow.Core.Validation;

namespace StockFlow.Core.Services;

public sealed partial class ProductService
{
    public const int MaxNameLength = 200;
    public const decimal MaxUnitPrice = 1_000_000m;

    private readonly IProductRepository _products;

    public ProductService(IProductRepository products)
    {
        _products = products ?? throw new ArgumentNullException(nameof(products));
    }

    public Task<IReadOnlyList<ProductSummary>> SearchAsync(ProductQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return _products.SearchAsync(query, cancellationToken);
    }

    public async Task<ProductDetail> GetAsync(int productId, CancellationToken cancellationToken = default)
    {
        return await _products.GetByIdAsync(productId, cancellationToken).ConfigureAwait(false)
            ?? throw EntityNotFoundException.For("Product", productId);
    }

    public Task<IReadOnlyList<StockMovement>> GetMovementsAsync(int productId, int top = 50, CancellationToken cancellationToken = default)
    {
        return _products.GetMovementsAsync(productId, Math.Clamp(top, 1, 500), cancellationToken);
    }

    /// <summary>Validates, normalises and saves the product. Returns the product id.</summary>
    public Task<int> SaveAsync(Product product, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);

        product.Sku = NormaliseSku(product.Sku);
        product.Name = (product.Name ?? string.Empty).Trim();

        Validate(product);

        return _products.SaveAsync(product, cancellationToken);
    }

    /// <summary>SKUs are stored upper-case with no surrounding spaces, so "aud-001 " and "AUD-001" are the same product.</summary>
    public static string NormaliseSku(string? sku) => (sku ?? string.Empty).Trim().ToUpperInvariant();

    public static void Validate(Product product)
    {
        var errors = new ValidationErrors();

        if (string.IsNullOrWhiteSpace(product.Sku))
        {
            errors.Add(nameof(Product.Sku), "SKU is required.");
        }
        else if (!SkuPattern().IsMatch(product.Sku))
        {
            errors.Add(nameof(Product.Sku), "SKU must be 3 to 32 characters: letters, numbers and dashes only.");
        }

        errors.AddIf(string.IsNullOrWhiteSpace(product.Name), nameof(Product.Name), "Name is required.");
        errors.AddIf(product.Name.Length > MaxNameLength, nameof(Product.Name), $"Name must be {MaxNameLength} characters or fewer.");
        errors.AddIf(product.CategoryId <= 0, nameof(Product.CategoryId), "Choose a category.");
        errors.AddIf(product.UnitPrice < 0, nameof(Product.UnitPrice), "Unit price cannot be negative.");
        errors.AddIf(product.UnitPrice > MaxUnitPrice, nameof(Product.UnitPrice), "Unit price looks too high. Check the value.");
        errors.AddIf(decimal.Round(product.UnitPrice, 2) != product.UnitPrice, nameof(Product.UnitPrice), "Unit price can have at most 2 decimal places.");
        errors.AddIf(product.ReorderLevel < 0, nameof(Product.ReorderLevel), "Reorder level cannot be negative.");

        errors.ThrowIfAny();
    }

    [GeneratedRegex("^[A-Z0-9][A-Z0-9-]{2,31}$")]
    private static partial Regex SkuPattern();
}
