Imports StockFlow.Core.Exceptions

''' <summary>
''' The one place the desktop app decides how to report an exception:
'''   StockFlowException          expected business rule; show its message, log as info
'''   DataAccessException         database busy/unreachable; ask the user to retry
'''   anything else               a defect; log full details with an incident reference
''' </summary>
Friend Module ErrorHandler

    Friend Sub Handle(ex As Exception, owner As IWin32Window, action As String)
        Dim actual = Unwrap(ex)

        Dim business = TryCast(actual, StockFlowException)
        If business IsNot Nothing Then
            AppLog.Info($"Rejected while {action}: {business.Message}")
            Show(owner, business.Message, MessageBoxIcon.Warning)
            Return
        End If

        Dim dataAccess = TryCast(actual, DataAccessException)
        If dataAccess IsNot Nothing AndAlso dataAccess.IsTransient Then
            Dim reference = AppLog.Failure($"Database unavailable while {action}", dataAccess)
            Show(owner, $"The database is busy or cannot be reached. Please try again in a moment.{Environment.NewLine}{Environment.NewLine}Reference: {reference}", MessageBoxIcon.Warning)
            Return
        End If

        Dim incident = AppLog.Failure($"Unexpected error while {action}", actual)
        Show(owner,
             $"Something went wrong while {action}.{Environment.NewLine}{Environment.NewLine}" &
             $"The details were logged. If it keeps happening, give support this reference: {incident}",
             MessageBoxIcon.Error)
    End Sub

    Friend Sub ShowStartupFailure(ex As Exception)
        Dim incident = AppLog.Failure("Start-up failed", ex)
        MessageBox.Show(
            $"StockFlow could not start:{Environment.NewLine}{ex.Message}{Environment.NewLine}{Environment.NewLine}" &
            $"Check {DesktopSettings.FileName} next to the program. Reference: {incident}",
            "StockFlow", MessageBoxButtons.OK, MessageBoxIcon.Error)
    End Sub

    ''' <summary>Task-based code can wrap the real exception in an AggregateException.</summary>
    Private Function Unwrap(ex As Exception) As Exception
        Dim aggregate = TryCast(ex, AggregateException)
        If aggregate IsNot Nothing AndAlso aggregate.InnerExceptions.Count = 1 Then
            Return Unwrap(aggregate.InnerExceptions(0))
        End If
        Return ex
    End Function

    Private Sub Show(owner As IWin32Window, message As String, icon As MessageBoxIcon)
        If owner Is Nothing Then
            MessageBox.Show(message, "StockFlow", MessageBoxButtons.OK, icon)
        Else
            MessageBox.Show(owner, message, "StockFlow", MessageBoxButtons.OK, icon)
        End If
    End Sub

End Module
