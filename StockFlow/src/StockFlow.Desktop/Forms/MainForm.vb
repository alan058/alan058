Imports System.Diagnostics

''' <summary>
''' Main window: searchable product list with stock across all warehouses.
''' Receive and adjust stock from here, or open the order desk and reports.
''' </summary>
Friend NotInheritable Class MainForm
    Inherits Form

    Private ReadOnly _services As AppServices
    Private ReadOnly _grid As New DataGridView()
    Private ReadOnly _binding As New BindingSource()
    Private ReadOnly _searchBox As New TextBox()
    Private ReadOnly _categoryCombo As New ComboBox()
    Private ReadOnly _lowStockCheck As New CheckBox()
    Private ReadOnly _statusLabel As New ToolStripStatusLabel()
    Private ReadOnly _countLabel As New ToolStripStatusLabel()
    Private ReadOnly _searchDelay As New Timer() With {.Interval = 350}
    Private _lookups As New Lookups()

    ''' <summary>
    ''' Incremented for every load. Only the newest load may update the grid.
    ''' Fix for SF-087: typing quickly started several searches, and a slow
    ''' early one could finish last and overwrite the grid with stale results.
    ''' </summary>
    Private _loadVersion As Integer

    Friend Sub New(services As AppServices)
        _services = services
        BuildLayout()
    End Sub

    Private Sub BuildLayout()
        Text = "StockFlow - Stock Control"
        Size = New Size(1150, 720)
        MinimumSize = New Size(820, 480)
        StartPosition = FormStartPosition.CenterScreen
        Font = New Font("Segoe UI", 9.0F)

        ' --- Grid
        GridHelper.ConfigureReadOnly(_grid)
        GridHelper.AddColumn(_grid, NameOf(ProductSummary.Sku), "SKU", 90)
        GridHelper.AddColumn(_grid, NameOf(ProductSummary.Name), "Product", 260, fill:=True)
        GridHelper.AddColumn(_grid, NameOf(ProductSummary.CategoryName), "Category", 140)
        GridHelper.AddColumn(_grid, NameOf(ProductSummary.UnitPrice), "Price (R)", 90, "N2", alignRight:=True)
        GridHelper.AddColumn(_grid, NameOf(ProductSummary.QuantityOnHand), "On hand", 80, alignRight:=True)
        GridHelper.AddColumn(_grid, NameOf(ProductSummary.QuantityReserved), "Reserved", 80, alignRight:=True)
        GridHelper.AddColumn(_grid, NameOf(ProductSummary.QuantityAvailable), "Available", 80, alignRight:=True)
        GridHelper.AddColumn(_grid, NameOf(ProductSummary.ReorderLevel), "Reorder at", 80, alignRight:=True)
        _grid.DataSource = _binding
        AddHandler _grid.CellFormatting, AddressOf Grid_CellFormatting
        AddHandler _grid.CellDoubleClick, AddressOf Grid_CellDoubleClick

        ' --- Filter bar
        Dim filters As New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .Padding = New Padding(8, 8, 8, 4), .WrapContents = False}
        _searchBox.Width = 220
        _searchBox.PlaceholderText = "Search SKU or name"
        _categoryCombo.DropDownStyle = ComboBoxStyle.DropDownList
        _categoryCombo.Width = 180
        _lowStockCheck.Text = "Low stock only"
        _lowStockCheck.AutoSize = True
        _lowStockCheck.Margin = New Padding(12, 6, 12, 0)

        Dim receiveButton As New Button With {.Text = "&Receive stock...", .AutoSize = True}
        Dim adjustButton As New Button With {.Text = "&Adjust stock...", .AutoSize = True}
        Dim refreshButton As New Button With {.Text = "Refresh (F5)", .AutoSize = True}

        filters.Controls.Add(New Label With {.Text = "Search", .AutoSize = True, .Margin = New Padding(0, 7, 4, 0)})
        filters.Controls.Add(_searchBox)
        filters.Controls.Add(New Label With {.Text = "Category", .AutoSize = True, .Margin = New Padding(12, 7, 4, 0)})
        filters.Controls.Add(_categoryCombo)
        filters.Controls.Add(_lowStockCheck)
        filters.Controls.Add(receiveButton)
        filters.Controls.Add(adjustButton)
        filters.Controls.Add(refreshButton)

        AddHandler _searchBox.TextChanged, Sub(s, e) RestartSearchDelay()
        AddHandler _categoryCombo.SelectionChangeCommitted, AddressOf Filters_Changed
        AddHandler _lowStockCheck.CheckedChanged, AddressOf Filters_Changed
        AddHandler _searchDelay.Tick, AddressOf SearchDelay_Tick
        AddHandler receiveButton.Click, AddressOf ReceiveStock_Click
        AddHandler adjustButton.Click, AddressOf AdjustStock_Click
        AddHandler refreshButton.Click, AddressOf Filters_Changed

        ' --- Menu
        Dim menu As New MenuStrip()
        Dim fileMenu As New ToolStripMenuItem("&File")
        fileMenu.DropDownItems.Add("Open &log folder", Nothing, AddressOf OpenLogFolder_Click)
        fileMenu.DropDownItems.Add(New ToolStripSeparator())
        fileMenu.DropDownItems.Add("E&xit", Nothing, Sub(s, e) Close())

        Dim stockMenu As New ToolStripMenuItem("&Stock")
        stockMenu.DropDownItems.Add("&Receive stock...", Nothing, AddressOf ReceiveStock_Click)
        stockMenu.DropDownItems.Add("&Adjust stock...", Nothing, AddressOf AdjustStock_Click)

        Dim ordersMenu As New ToolStripMenuItem("&Orders")
        ordersMenu.DropDownItems.Add("Order &desk...", Nothing, AddressOf OrderDesk_Click)

        Dim reportsMenu As New ToolStripMenuItem("&Reports")
        reportsMenu.DropDownItems.Add("&Low stock...", Nothing, AddressOf LowStock_Click)

        Dim helpMenu As New ToolStripMenuItem("&Help")
        helpMenu.DropDownItems.Add("&About StockFlow", Nothing, AddressOf About_Click)

        menu.Items.AddRange(New ToolStripItem() {fileMenu, stockMenu, ordersMenu, reportsMenu, helpMenu})
        MainMenuStrip = menu

        ' --- Status bar
        Dim status As New StatusStrip()
        _statusLabel.Spring = True
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft
        status.Items.Add(_statusLabel)
        status.Items.Add(_countLabel)
        status.Items.Add(New ToolStripStatusLabel($"{_services.CurrentUser} | {_services.ProviderDescription}"))

        ' Docking order: the fill control is added first so the bars dock around it.
        Controls.Add(_grid)
        Controls.Add(filters)
        Controls.Add(menu)
        Controls.Add(status)

        KeyPreview = True
        AddHandler KeyDown, AddressOf MainForm_KeyDown
        AddHandler Shown, AddressOf MainForm_Shown
    End Sub

    Private Async Sub MainForm_Shown(sender As Object, e As EventArgs)
        Try
            SetBusy(True, "Loading...")
            _lookups = Await _services.Reports.GetLookupsAsync()

            _categoryCombo.Items.Add(New LookupItem(0, Nothing, "All categories"))
            For Each category In _lookups.Categories
                _categoryCombo.Items.Add(category)
            Next
            _categoryCombo.SelectedIndex = 0
        Catch ex As Exception
            ErrorHandler.Handle(ex, Me, "loading reference data")
        End Try

        Await LoadProductsAsync()
    End Sub

    Private Async Function LoadProductsAsync() As Task
        _loadVersion += 1
        Dim version = _loadVersion
        Dim query As New ProductQuery With {
            .SearchTerm = _searchBox.Text,
            .CategoryId = SelectedCategoryId(),
            .LowStockOnly = _lowStockCheck.Checked
        }

        SetBusy(True, "Loading products...")
        Try
            Dim products = Await _services.Products.SearchAsync(query)
            If version <> _loadVersion Then
                Return ' A newer search has started; its result wins.
            End If

            Dim previouslySelected = SelectedProduct()?.ProductId
            _binding.DataSource = products.ToList()
            Reselect(previouslySelected)

            Dim lowCount = products.Where(Function(p) p.IsLowStock).Count()
            _countLabel.Text = $"{products.Count} products, {lowCount} low on stock"
            SetBusy(False, "Ready")
        Catch ex As Exception
            SetBusy(False, "Load failed")
            ErrorHandler.Handle(ex, Me, "loading products")
        End Try
    End Function

    Private Sub RestartSearchDelay()
        ' Search after the user pauses typing instead of on every keystroke.
        _searchDelay.Stop()
        _searchDelay.Start()
    End Sub

    Private Async Sub SearchDelay_Tick(sender As Object, e As EventArgs)
        _searchDelay.Stop()
        Await LoadProductsAsync()
    End Sub

    Private Async Sub Filters_Changed(sender As Object, e As EventArgs)
        _searchDelay.Stop()
        Await LoadProductsAsync()
    End Sub

    Private Async Sub ReceiveStock_Click(sender As Object, e As EventArgs)
        Await ShowStockDialogAsync(StockDialogMode.Receive)
    End Sub

    Private Async Sub AdjustStock_Click(sender As Object, e As EventArgs)
        Await ShowStockDialogAsync(StockDialogMode.Adjust)
    End Sub

    Private Async Sub Grid_CellDoubleClick(sender As Object, e As DataGridViewCellEventArgs)
        If e.RowIndex >= 0 Then
            Await ShowStockDialogAsync(StockDialogMode.Receive)
        End If
    End Sub

    Private Async Function ShowStockDialogAsync(mode As StockDialogMode) As Task
        Dim product = SelectedProduct()
        If product Is Nothing Then
            MessageBox.Show(Me, "Select a product first.", "StockFlow", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        Dim message As String = Nothing
        Using dialog As New StockAdjustmentForm(_services, mode, product, _lookups.Warehouses)
            If dialog.ShowDialog(Me) = DialogResult.OK Then
                message = dialog.ResultMessage
            End If
        End Using

        If message IsNot Nothing Then
            Await LoadProductsAsync()
            _statusLabel.Text = message
        End If
    End Function

    Private Async Sub OrderDesk_Click(sender As Object, e As EventArgs)
        Using desk As New OrdersForm(_services)
            desk.ShowDialog(Me)
        End Using
        Await LoadProductsAsync() ' Allocations and dispatches change the stock figures.
    End Sub

    Private Sub LowStock_Click(sender As Object, e As EventArgs)
        Using report As New LowStockForm(_services)
            report.ShowDialog(Me)
        End Using
    End Sub

    Private Sub OpenLogFolder_Click(sender As Object, e As EventArgs)
        IO.Directory.CreateDirectory(AppLog.LogFolder)
        Process.Start(New ProcessStartInfo(AppLog.LogFolder) With {.UseShellExecute = True})
    End Sub

    Private Sub About_Click(sender As Object, e As EventArgs)
        Dim version = GetType(MainForm).Assembly.GetName().Version
        MessageBox.Show(Me,
                        $"StockFlow back office {version}{Environment.NewLine}" &
                        $"Data: {_services.ProviderDescription}{Environment.NewLine}" &
                        $"User: {_services.CurrentUser}{Environment.NewLine}" &
                        $"Log: {AppLog.CurrentLogFile}",
                        "About StockFlow", MessageBoxButtons.OK, MessageBoxIcon.Information)
    End Sub

    Private Async Sub MainForm_KeyDown(sender As Object, e As KeyEventArgs)
        If e.KeyCode = Keys.F5 Then
            e.Handled = True
            Await LoadProductsAsync()
        End If
    End Sub

    Private Sub Grid_CellFormatting(sender As Object, e As DataGridViewCellFormattingEventArgs)
        If e.RowIndex < 0 OrElse e.CellStyle Is Nothing Then
            Return
        End If

        Dim product = TryCast(_grid.Rows(e.RowIndex).DataBoundItem, ProductSummary)
        If product IsNot Nothing AndAlso product.IsLowStock Then
            e.CellStyle.ForeColor = Color.Firebrick
        End If
    End Sub

    Private Function SelectedProduct() As ProductSummary
        Return TryCast(_binding.Current, ProductSummary)
    End Function

    Private Function SelectedCategoryId() As Integer?
        Dim item = TryCast(_categoryCombo.SelectedItem, LookupItem)
        If item Is Nothing OrElse item.Id = 0 Then
            Return Nothing
        End If
        Return item.Id
    End Function

    Private Sub Reselect(productId As Integer?)
        If Not productId.HasValue Then
            Return
        End If

        For i = 0 To _binding.Count - 1
            If DirectCast(_binding(i), ProductSummary).ProductId = productId.Value Then
                _binding.Position = i
                Return
            End If
        Next
    End Sub

    Private Sub SetBusy(busy As Boolean, message As String)
        UseWaitCursor = busy
        _statusLabel.Text = message
    End Sub

End Class
