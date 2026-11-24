Imports System.Reflection

''' <summary>Consistent set-up for the read-only data grids used throughout the app.</summary>
Friend Module GridHelper

    Friend Sub ConfigureReadOnly(grid As DataGridView)
        grid.Dock = DockStyle.Fill
        grid.ReadOnly = True
        grid.AllowUserToAddRows = False
        grid.AllowUserToDeleteRows = False
        grid.AllowUserToResizeRows = False
        grid.AutoGenerateColumns = False
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect
        grid.MultiSelect = False
        grid.RowHeadersVisible = False
        grid.BackgroundColor = SystemColors.Window
        grid.BorderStyle = BorderStyle.None
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(247, 248, 250)
        EnableDoubleBuffering(grid)
    End Sub

    Friend Function AddColumn(grid As DataGridView,
                              propertyName As String,
                              header As String,
                              width As Integer,
                              Optional format As String = Nothing,
                              Optional alignRight As Boolean = False,
                              Optional fill As Boolean = False) As DataGridViewTextBoxColumn
        Dim column As New DataGridViewTextBoxColumn With {
            .Name = propertyName,
            .DataPropertyName = propertyName,
            .HeaderText = header,
            .Width = width,
            .SortMode = DataGridViewColumnSortMode.NotSortable
        }

        If format IsNot Nothing Then
            column.DefaultCellStyle.Format = format
        End If

        If alignRight Then
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight
            column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight
        End If

        If fill Then
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        End If

        grid.Columns.Add(column)
        Return column
    End Function

    ''' <summary>
    ''' DataGridView.DoubleBuffered is Protected, so it is set by reflection.
    ''' Without it large grids flicker and repaint slowly, especially over
    ''' Remote Desktop, which users report as "the app is slow".
    ''' </summary>
    Private Sub EnableDoubleBuffering(grid As DataGridView)
        Dim prop = GetType(DataGridView).GetProperty("DoubleBuffered", BindingFlags.Instance Or BindingFlags.NonPublic)
        prop?.SetValue(grid, True)
    End Sub

End Module
