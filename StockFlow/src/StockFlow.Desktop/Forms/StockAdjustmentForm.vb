Imports StockFlow.Core.Services

Friend Enum StockDialogMode
    Receive
    Adjust
End Enum

''' <summary>Receive stock from a supplier, or post a correction with a reason.</summary>
Friend NotInheritable Class StockAdjustmentForm
    Inherits Form

    Private ReadOnly _services As AppServices
    Private ReadOnly _mode As StockDialogMode
    Private ReadOnly _product As ProductSummary
    Private ReadOnly _warehouseCombo As New ComboBox()
    Private ReadOnly _quantity As New NumericUpDown()
    Private ReadOnly _noteBox As New TextBox()
    Private ReadOnly _okButton As New Button()
    Private ReadOnly _cancelButton As New Button()
    Private _busy As Boolean

    Friend Sub New(services As AppServices, mode As StockDialogMode, product As ProductSummary, warehouses As IReadOnlyList(Of LookupItem))
        _services = services
        _mode = mode
        _product = product
        BuildLayout(warehouses)
    End Sub

    ''' <summary>Summary of what was posted, shown in the main window's status bar.</summary>
    Friend Property ResultMessage As String = String.Empty

    Private ReadOnly Property IsReceipt As Boolean
        Get
            Return _mode = StockDialogMode.Receive
        End Get
    End Property

    Private Sub BuildLayout(warehouses As IReadOnlyList(Of LookupItem))
        Text = If(IsReceipt, "Receive stock", "Adjust stock")
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        AutoSize = True
        AutoSizeMode = AutoSizeMode.GrowAndShrink
        Font = New Font("Segoe UI", 9.0F)

        Dim layout As New TableLayoutPanel With {.ColumnCount = 2, .AutoSize = True, .Padding = New Padding(12), .Dock = DockStyle.Fill}
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 300))

        _warehouseCombo.DropDownStyle = ComboBoxStyle.DropDownList
        _warehouseCombo.Dock = DockStyle.Fill
        For Each warehouse In warehouses
            _warehouseCombo.Items.Add(warehouse)
        Next
        If _warehouseCombo.Items.Count > 0 Then
            _warehouseCombo.SelectedIndex = 0
        End If

        _quantity.Maximum = InventoryService.MaxQuantityPerMovement
        _quantity.Minimum = If(IsReceipt, 1, -InventoryService.MaxQuantityPerMovement)
        _quantity.Value = If(IsReceipt, 1, 0)
        _quantity.ThousandsSeparator = True
        _quantity.Width = 120

        _noteBox.Dock = DockStyle.Fill
        _noteBox.MaxLength = If(IsReceipt, 50, 200)

        AddRow(layout, "Product", New Label With {.Text = $"{_product.Sku}  {_product.Name}", .AutoSize = True, .Font = New Font(Font, FontStyle.Bold)})
        AddRow(layout, "Warehouse", _warehouseCombo)
        AddRow(layout, If(IsReceipt, "Quantity received", "Change (+/-)"), _quantity)
        AddRow(layout, If(IsReceipt, "Delivery / PO ref", "Reason (required)"), _noteBox)

        If Not IsReceipt Then
            layout.Controls.Add(New Label With {
                .Text = "Stock reserved for allocated orders cannot be adjusted away.",
                .AutoSize = True, .ForeColor = SystemColors.GrayText, .Margin = New Padding(3, 6, 3, 3)}, 1, layout.RowCount)
            layout.RowCount += 1
        End If

        _okButton.Text = If(IsReceipt, "Receive", "Post adjustment")
        _okButton.AutoSize = True
        _cancelButton.Text = "Cancel"
        _cancelButton.DialogResult = DialogResult.Cancel
        Dim buttons As New FlowLayoutPanel With {.FlowDirection = FlowDirection.RightToLeft, .AutoSize = True, .Dock = DockStyle.Fill, .Margin = New Padding(0, 12, 0, 0)}
        buttons.Controls.Add(_cancelButton)
        buttons.Controls.Add(_okButton)
        layout.Controls.Add(buttons, 0, layout.RowCount)
        layout.SetColumnSpan(buttons, 2)
        layout.RowCount += 1

        Controls.Add(layout)
        AcceptButton = _okButton
        CancelButton = _cancelButton

        AddHandler _okButton.Click, AddressOf OkButton_Click
        AddHandler FormClosing, AddressOf Form_FormClosing
    End Sub

    Private Shared Sub AddRow(layout As TableLayoutPanel, caption As String, editor As Control)
        Dim row = layout.RowCount
        layout.Controls.Add(New Label With {.Text = caption, .AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(3, 6, 12, 3)}, 0, row)
        layout.Controls.Add(editor, 1, row)
        layout.RowCount = row + 1
    End Sub

    Private Async Sub OkButton_Click(sender As Object, e As EventArgs)
        Dim warehouse = TryCast(_warehouseCombo.SelectedItem, LookupItem)
        If warehouse Is Nothing Then
            MessageBox.Show(Me, "Choose a warehouse.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        Dim quantity = CInt(_quantity.Value)
        SetBusy(True)
        Try
            Dim level As StockLevel
            If IsReceipt Then
                level = Await _services.Inventory.ReceiveAsync(_product.ProductId, warehouse.Id, quantity, _noteBox.Text, _services.CurrentUser)
            Else
                level = Await _services.Inventory.AdjustAsync(_product.ProductId, warehouse.Id, quantity, _noteBox.Text, _services.CurrentUser)
            End If

            ResultMessage = $"{_product.Sku} at {level.WarehouseCode}: {quantity:+#,0;-#,0} posted. On hand {level.QuantityOnHand:N0}, available {level.QuantityAvailable:N0}."
            AppLog.Info(ResultMessage)
            SetBusy(False)
            DialogResult = DialogResult.OK
        Catch ex As Exception
            SetBusy(False)
            ErrorHandler.Handle(ex, Me, If(IsReceipt, "receiving stock", "adjusting stock"))
        End Try
    End Sub

    Private Sub Form_FormClosing(sender As Object, e As FormClosingEventArgs)
        ' Do not let the dialog close while the database call is still running.
        If _busy Then
            e.Cancel = True
        End If
    End Sub

    Private Sub SetBusy(busy As Boolean)
        _busy = busy
        _okButton.Enabled = Not busy
        _cancelButton.Enabled = Not busy
        UseWaitCursor = busy
    End Sub

End Class
