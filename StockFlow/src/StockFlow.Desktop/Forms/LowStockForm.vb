Imports System.IO
Imports System.Text
Imports StockFlow.Core.Reporting

''' <summary>Products at or below their reorder level, with a CSV export for the buying team.</summary>
Friend NotInheritable Class LowStockForm
    Inherits Form

    Private ReadOnly _services As AppServices
    Private ReadOnly _grid As New DataGridView()
    Private ReadOnly _binding As New BindingSource()
    Private ReadOnly _summary As New Label()
    Private _items As IReadOnlyList(Of LowStockItem) = Array.Empty(Of LowStockItem)()

    Friend Sub New(services As AppServices)
        _services = services
        BuildLayout()
    End Sub

    Private Sub BuildLayout()
        Text = "StockFlow - Low stock report"
        Size = New Size(980, 560)
        StartPosition = FormStartPosition.CenterParent
        Font = New Font("Segoe UI", 9.0F)

        GridHelper.ConfigureReadOnly(_grid)
        GridHelper.AddColumn(_grid, NameOf(LowStockItem.Sku), "SKU", 90)
        GridHelper.AddColumn(_grid, NameOf(LowStockItem.Name), "Product", 240, fill:=True)
        GridHelper.AddColumn(_grid, NameOf(LowStockItem.CategoryName), "Category", 140)
        GridHelper.AddColumn(_grid, NameOf(LowStockItem.QuantityOnHand), "On hand", 75, alignRight:=True)
        GridHelper.AddColumn(_grid, NameOf(LowStockItem.QuantityReserved), "Reserved", 75, alignRight:=True)
        GridHelper.AddColumn(_grid, NameOf(LowStockItem.QuantityAvailable), "Available", 75, alignRight:=True)
        GridHelper.AddColumn(_grid, NameOf(LowStockItem.ReorderLevel), "Reorder at", 80, alignRight:=True)
        GridHelper.AddColumn(_grid, NameOf(LowStockItem.SuggestedOrderQuantity), "Suggested order", 110, alignRight:=True)
        _grid.DataSource = _binding

        Dim bar As New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .Padding = New Padding(8), .WrapContents = False}
        Dim exportButton As New Button With {.Text = "&Export CSV...", .AutoSize = True}
        Dim refreshButton As New Button With {.Text = "&Refresh", .AutoSize = True}
        _summary.AutoSize = True
        _summary.Margin = New Padding(12, 7, 0, 0)
        bar.Controls.AddRange(New Control() {exportButton, refreshButton, _summary})

        Controls.Add(_grid)
        Controls.Add(bar)

        AddHandler Shown, AddressOf Refresh_Click
        AddHandler refreshButton.Click, AddressOf Refresh_Click
        AddHandler exportButton.Click, AddressOf Export_Click
    End Sub

    Private Async Sub Refresh_Click(sender As Object, e As EventArgs)
        UseWaitCursor = True
        Try
            _items = Await _services.Reports.GetLowStockAsync()
            _binding.DataSource = _items.ToList()
            _summary.Text = If(_items.Count = 0, "Nothing needs reordering.", $"{_items.Count} products need reordering.")
        Catch ex As Exception
            ErrorHandler.Handle(ex, Me, "loading the low stock report")
        Finally
            UseWaitCursor = False
        End Try
    End Sub

    Private Sub Export_Click(sender As Object, e As EventArgs)
        Using dialog As New SaveFileDialog With {
            .Filter = "CSV files (*.csv)|*.csv",
            .FileName = $"low-stock-{Date.Today:yyyy-MM-dd}.csv"
        }
            If dialog.ShowDialog(Me) <> DialogResult.OK Then
                Return
            End If

            Try
                Dim content As New StringBuilder()
                content.AppendLine("SKU,Product,Category,Reorder level,On hand,Reserved,Available,Suggested order")
                For Each i In _items
                    content.AppendLine(Csv.Line(i.Sku, i.Name, i.CategoryName, i.ReorderLevel, i.QuantityOnHand, i.QuantityReserved, i.QuantityAvailable, i.SuggestedOrderQuantity))
                Next

                ' UTF-8 with BOM so Excel shows accented characters correctly.
                File.WriteAllText(dialog.FileName, content.ToString(), New UTF8Encoding(encoderShouldEmitUTF8Identifier:=True))
                AppLog.Info($"Low stock report exported to {dialog.FileName}")
                MessageBox.Show(Me, $"Saved {_items.Count} rows to {dialog.FileName}", Text, MessageBoxButtons.OK, MessageBoxIcon.Information)
            Catch ex As IOException
                ErrorHandler.Handle(ex, Me, "saving the CSV file")
            Catch ex As UnauthorizedAccessException
                ErrorHandler.Handle(ex, Me, "saving the CSV file")
            End Try
        End Using
    End Sub

End Class
