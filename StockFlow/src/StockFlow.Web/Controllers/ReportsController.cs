using System.Text;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Core.Abstractions;
using StockFlow.Core.Reporting;

namespace StockFlow.Web.Controllers;

public sealed class ReportsController : StockFlowController
{
    private readonly IReportRepository _reports;

    public ReportsController(IReportRepository reports) => _reports = reports;

    public async Task<IActionResult> LowStock(CancellationToken cancellationToken)
    {
        return View(await _reports.GetLowStockAsync(cancellationToken));
    }

    /// <summary>CSV for the buying team to open in Excel.</summary>
    public async Task<IActionResult> LowStockCsv(CancellationToken cancellationToken)
    {
        var items = await _reports.GetLowStockAsync(cancellationToken);
        var csv = new StringBuilder();
        csv.AppendLine("SKU,Product,Category,Reorder level,On hand,Reserved,Available,Suggested order");
        foreach (var i in items)
        {
            csv.AppendLine(Csv.Line(i.Sku, i.Name, i.CategoryName, i.ReorderLevel, i.QuantityOnHand, i.QuantityReserved, i.QuantityAvailable, i.SuggestedOrderQuantity));
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return File(bytes, "text/csv", $"low-stock-{DateTime.Today:yyyy-MM-dd}.csv");
    }
}
