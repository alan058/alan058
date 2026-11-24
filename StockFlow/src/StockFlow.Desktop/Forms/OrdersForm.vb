Imports StockFlow.Core.Services

''' <summary>
''' Warehouse order desk: review orders, allocate stock, dispatch and cancel.
''' Buttons are enabled from OrderStatusRules, the same rules the web app and
''' the stored procedures use.
''' </summary>
Friend NotInheritable Class OrdersForm
    Inherits Form

    Private ReadOnly _services As AppServices
    Private ReadOnly _ordersGrid As New DataGridView()
    Private ReadOnly _linesGrid As New DataGridView()
    Private ReadOnly _ordersBinding As New BindingSource()
    Private ReadOnly _linesBinding As New BindingSource()
    Private ReadOnly _statusFilter As New ComboBox()
    Private ReadOnly _searchBox As New TextBox()
    Private ReadOnly _headerLabel As New Label()
    Private ReadOnly _allocateButton As New Button()
    Private ReadOnly _dispatchButton As New Button()
    Private ReadOnly _cancelOrderButton As New Button()
    Private ReadOnly _statusLabel As New ToolStripStatusLabel()
    Private _current As SalesOrder
    Private _listVersion As Integer
    Private _detailVersion As Integer

    Friend Sub New(services As AppServices)
        _services = services
        BuildLayout()
    End Sub

    Private Sub BuildLayout()
        Text = "StockFlow - Order desk"
        Size = New Size(1100, 720)
        MinimumSize = New Size(800, 500)
        StartPosition = FormStartPosition.CenterParent
        Font = New Font("Segoe UI", 9.0F)

        ' --- Filter bar
        Dim filters As New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .Padding = New Padding(8, 8, 8, 4), .WrapContents = False}
        _statusFilter.DropDownStyle = ComboBoxStyle.DropDownList
        _statusFilter.Width = 140
        _statusFilter.Items.Add(New StatusFilterItem(Nothing))
        For Each orderStatus In [Enum].GetValues(GetType(OrderStatus)).Cast(Of OrderStatus)()
            _statusFilter.Items.Add(New StatusFilterItem(orderStatus))
        Next
        _statusFilter.SelectedIndex = 1 ' Open orders first: that is the work queue.
        _searchBox.Width = 220
        _searchBox.PlaceholderText = "Order number or customer"
        Dim searchButton As New Button With {.Text = "Search", .AutoSize = True}

        filters.Controls.Add(New Label With {.Text = "Status", .AutoSize = True, .Margin = New Padding(0, 7, 4, 0)})
        filters.Controls.Add(_statusFilter)
        filters.Controls.Add(New Label With {.Text = "Find", .AutoSize = True, .Margin = New Padding(12, 7, 4, 0)})
        filters.Controls.Add(_searchBox)
        filters.Controls.Add(searchButton)

        ' --- Orders grid (top)
        GridHelper.ConfigureReadOnly(_ordersGrid)
        GridHelper.AddColumn(_ordersGrid, NameOf(SalesOrderSummary.OrderNumber), "Order", 100)
        GridHelper.AddColumn(_ordersGrid, NameOf(SalesOrderSummary.OrderDate), "Date (UTC)", 120, "yyyy-MM-dd HH:mm")
        GridHelper.AddColumn(_ordersGrid, NameOf(SalesOrderSummary.CustomerName), "Customer", 220, fill:=True)
        GridHelper.AddColumn(_ordersGrid, NameOf(SalesOrderSummary.WarehouseCode), "From", 60)
        GridHelper.AddColumn(_ordersGrid, NameOf(SalesOrderSummary.Status), "Status", 90)
        GridHelper.AddColumn(_ordersGrid, NameOf(SalesOrderSummary.LineCount), "Lines", 60, alignRight:=True)
        GridHelper.AddColumn(_ordersGrid, NameOf(SalesOrderSummary.TotalQuantity), "Units", 70, alignRight:=True)
        GridHelper.AddColumn(_ordersGrid, NameOf(SalesOrderSummary.TotalValue), "Value (R)", 110, "N2", alignRight:=True)
        _ordersGrid.DataSource = _ordersBinding

        ' --- Detail panel (bottom): header, actions, lines
        _headerLabel.AutoSize = True
        _headerLabel.Font = New Font(Font.FontFamily, 10.0F, FontStyle.Bold)
        _headerLabel.Margin = New Padding(0, 6, 24, 0)

        _allocateButton.Text = "&Allocate stock"
        _dispatchButton.Text = "&Dispatch"
        _cancelOrderButton.Text = "&Cancel order"
        For Each button In {_allocateButton, _dispatchButton, _cancelOrderButton}
            button.AutoSize = True
            button.Enabled = False
        Next

        Dim actions As New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .Padding = New Padding(8, 6, 8, 6), .WrapContents = False}
        actions.Controls.AddRange(New Control() {_headerLabel, _allocateButton, _dispatchButton, _cancelOrderButton})

        GridHelper.ConfigureReadOnly(_linesGrid)
        GridHelper.AddColumn(_linesGrid, NameOf(SalesOrderLine.Sku), "SKU", 90)
        GridHelper.AddColumn(_linesGrid, NameOf(SalesOrderLine.ProductName), "Product", 260, fill:=True)
        GridHelper.AddColumn(_linesGrid, NameOf(SalesOrderLine.Quantity), "Qty", 70, alignRight:=True)
        GridHelper.AddColumn(_linesGrid, NameOf(SalesOrderLine.UnitPrice), "Unit price", 90, "N2", alignRight:=True)
        GridHelper.AddColumn(_linesGrid, NameOf(SalesOrderLine.LineTotal), "Line total", 100, "N2", alignRight:=True)
        GridHelper.AddColumn(_linesGrid, NameOf(SalesOrderLine.QuantityAvailable), "Available", 80, alignRight:=True)
        _linesGrid.DataSource = _linesBinding

        Dim split As New SplitContainer With {.Dock = DockStyle.Fill, .Orientation = Orientation.Horizontal, .SplitterDistance = 320}
        split.Panel1.Controls.Add(_ordersGrid)
        split.Panel2.Controls.Add(_linesGrid)
        split.Panel2.Controls.Add(actions)

        Dim status As New StatusStrip()
        _statusLabel.Spring = True
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft
        status.Items.Add(_statusLabel)

        Controls.Add(split)
        Controls.Add(filters)
        Controls.Add(status)

        AddHandler Shown, AddressOf Filters_Changed
        AddHandler _statusFilter.SelectionChangeCommitted, AddressOf Filters_Changed
        AddHandler searchButton.Click, AddressOf Filters_Changed
        AddHandler _searchBox.KeyDown, AddressOf SearchBox_KeyDown
        AddHandler _ordersBinding.CurrentChanged, AddressOf Orders_CurrentChanged
        AddHandler _linesGrid.CellFormatting, AddressOf Lines_CellFormatting
        AddHandler _allocateButton.Click, AddressOf Allocate_Click
        AddHandler _dispatchButton.Click, AddressOf Dispatch_Click
        AddHandler _cancelOrderButton.Click, AddressOf CancelOrder_Click
    End Sub

    Private Async Sub Filters_Changed(sender As Object, e As EventArgs)
        Await LoadOrdersAsync()
    End Sub

    Private Async Sub SearchBox_KeyDown(sender As Object, e As KeyEventArgs)
        If e.KeyCode = Keys.Enter Then
            e.SuppressKeyPress = True
            Await LoadOrdersAsync()
        End If
    End Sub

    Private Async Function LoadOrdersAsync() As Task
        _listVersion += 1
        Dim version = _listVersion
        Dim filter = TryCast(_statusFilter.SelectedItem, StatusFilterItem)
        Dim previouslySelected = _current?.SalesOrderId

        _statusLabel.Text = "Loading orders..."
        Try
            Dim orders = Await _services.Orders.SearchAsync(New OrderQuery With {
                .Status = filter?.Status,
                .SearchTerm = _searchBox.Text
            })
            If version <> _listVersion Then
                Return
            End If

            _ordersBinding.DataSource = orders.ToList()
            If previouslySelected.HasValue Then
                Dim index = orders.ToList().FindIndex(Function(o) o.SalesOrderId = previouslySelected.Value)
                If index >= 0 Then
                    _ordersBinding.Position = index
                End If
            End If

            If orders.Count = 0 Then
                ShowOrder(Nothing)
            End If
            _statusLabel.Text = $"{orders.Count} orders"
        Catch ex As Exception
            _statusLabel.Text = "Load failed"
            ErrorHandler.Handle(ex, Me, "loading orders")
        End Try
    End Function

    Private Async Sub Orders_CurrentChanged(sender As Object, e As EventArgs)
        Dim summary = TryCast(_ordersBinding.Current, SalesOrderSummary)
        If summary Is Nothing Then
            ShowOrder(Nothing)
            Return
        End If

        _detailVersion += 1
        Dim version = _detailVersion
        Try
            Dim order = Await _services.Orders.GetAsync(summary.SalesOrderId)
            If version = _detailVersion Then
                ShowOrder(order)
            End If
        Catch ex As Exception
            ErrorHandler.Handle(ex, Me, "loading the order")
        End Try
    End Sub

    Private Sub ShowOrder(order As SalesOrder)
        _current = order
        If order Is Nothing Then
            _headerLabel.Text = "No order selected"
            _linesBinding.DataSource = New List(Of SalesOrderLine)()
        Else
            Dim required = If(order.RequiredDate.HasValue, $", required {order.RequiredDate.Value:yyyy-MM-dd}", String.Empty)
            _headerLabel.Text = $"{order.OrderNumber}  {order.CustomerName}  ({OrderStatusRules.Describe(order.Status)}, from {order.WarehouseCode}{required})"
            _linesBinding.DataSource = order.Lines
        End If

        Dim status = If(order?.Status, CType(Nothing, OrderStatus?))
        _allocateButton.Enabled = status.HasValue AndAlso OrderStatusRules.CanAllocate(status.Value)
        _dispatchButton.Enabled = status.HasValue AndAlso OrderStatusRules.CanDispatch(status.Value)
        _cancelOrderButton.Enabled = status.HasValue AndAlso OrderStatusRules.CanCancel(status.Value)
    End Sub

    Private Async Sub Allocate_Click(sender As Object, e As EventArgs)
        Await RunOrderActionAsync("allocating stock",
                                  Function(id) _services.Orders.AllocateAsync(id, _services.CurrentUser),
                                  "stock allocated.")
    End Sub

    Private Async Sub Dispatch_Click(sender As Object, e As EventArgs)
        Await RunOrderActionAsync("dispatching the order",
                                  Function(id) _services.Orders.DispatchAsync(id, _services.CurrentUser),
                                  "dispatched.")
    End Sub

    Private Async Sub CancelOrder_Click(sender As Object, e As EventArgs)
        If _current Is Nothing Then
            Return
        End If

        Dim answer = MessageBox.Show(Me, $"Cancel {_current.OrderNumber} for {_current.CustomerName}? Any reserved stock will be released.",
                                     "Cancel order", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2)
        If answer <> DialogResult.Yes Then
            Return
        End If

        Await RunOrderActionAsync("cancelling the order",
                                  Function(id) _services.Orders.CancelAsync(id, _services.CurrentUser),
                                  "cancelled.")
    End Sub

    ''' <summary>Runs a status change, reports the result, then reloads so the screen shows what the database now holds.</summary>
    Private Async Function RunOrderActionAsync(action As String, operation As Func(Of Integer, Task), successMessage As String) As Task
        Dim order = _current
        If order Is Nothing Then
            Return
        End If

        UseWaitCursor = True
        For Each button In {_allocateButton, _dispatchButton, _cancelOrderButton}
            button.Enabled = False
        Next

        Try
            Await operation(order.SalesOrderId)
            _statusLabel.Text = $"{order.OrderNumber} {successMessage}"
            AppLog.Info($"{order.OrderNumber} {successMessage}")
        Catch ex As Exception
            ErrorHandler.Handle(ex, Me, action)
        Finally
            UseWaitCursor = False
        End Try

        Await LoadOrdersAsync()
        Orders_CurrentChanged(Me, EventArgs.Empty)
    End Function

    Private Sub Lines_CellFormatting(sender As Object, e As DataGridViewCellFormattingEventArgs)
        If e.RowIndex < 0 OrElse e.CellStyle Is Nothing OrElse _current Is Nothing OrElse _current.Status <> OrderStatus.Open Then
            Return
        End If

        Dim line = TryCast(_linesGrid.Rows(e.RowIndex).DataBoundItem, SalesOrderLine)
        If line IsNot Nothing AndAlso line.QuantityAvailable < line.Quantity Then
            e.CellStyle.ForeColor = Color.Firebrick
        End If
    End Sub

    ''' <summary>An entry in the status filter. Nothing means all statuses.</summary>
    Private NotInheritable Class StatusFilterItem
        Friend Sub New(status As OrderStatus?)
            Me.Status = status
        End Sub

        Friend ReadOnly Property Status As OrderStatus?

        Public Overrides Function ToString() As String
            Return If(Status.HasValue, OrderStatusRules.Describe(Status.Value), "All")
        End Function
    End Class

End Class
