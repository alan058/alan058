using StockFlow.Core.Exceptions;
using StockFlow.Core.Models;
using StockFlow.Core.Services;
using StockFlow.Data.InMemory;

namespace StockFlow.Tests.Services;

public sealed class ProductServiceTests
{
    private readonly ProductService _service = new(InMemoryStockFlowStore.CreateWithReferenceData());

    private static Product ValidProduct() => new()
    {
        Sku = "TST-001",
        Name = "Test product",
        CategoryId = 1,
        UnitPrice = 99.99m,
        ReorderLevel = 5,
    };

    [Fact]
    public async Task Save_NormalisesSkuToUpperCaseWithoutSpaces()
    {
        var product = ValidProduct();
        product.Sku = "  tst-001 ";

        var id = await _service.SaveAsync(product);

        var saved = await _service.GetAsync(id);
        Assert.Equal("TST-001", saved.Product.Sku);
    }

    [Theory]
    [InlineData("")]
    [InlineData("AB")]
    [InlineData("HAS SPACE")]
    [InlineData("-LEADINGDASH")]
    [InlineData("BAD/CHAR")]
    public void Validate_RejectsInvalidSkus(string sku)
    {
        var product = ValidProduct();
        product.Sku = sku;

        var ex = Assert.Throws<BusinessValidationException>(() => ProductService.Validate(product));
        Assert.True(ex.Errors.ContainsKey(nameof(Product.Sku)));
    }

    [Fact]
    public void Validate_ReportsEveryProblemAtOnce()
    {
        var product = new Product { Sku = "", Name = "", CategoryId = 0, UnitPrice = -1, ReorderLevel = -1 };

        var ex = Assert.Throws<BusinessValidationException>(() => ProductService.Validate(product));

        Assert.Equal(
            new[] { nameof(Product.Sku), nameof(Product.Name), nameof(Product.CategoryId), nameof(Product.UnitPrice), nameof(Product.ReorderLevel) }.OrderBy(x => x),
            ex.Errors.Keys.OrderBy(x => x));
    }

    [Fact]
    public void Validate_RejectsPricesWithMoreThanTwoDecimals()
    {
        var product = ValidProduct();
        product.UnitPrice = 10.555m;

        var ex = Assert.Throws<BusinessValidationException>(() => ProductService.Validate(product));
        Assert.True(ex.Errors.ContainsKey(nameof(Product.UnitPrice)));
    }

    [Fact]
    public async Task Get_UnknownProduct_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<EntityNotFoundException>(() => _service.GetAsync(999_999));
    }
}
