Imports System.IO
Imports System.Text

''' <summary>
''' Minimal daily file log in %LOCALAPPDATA%\StockFlow\Logs. Support can ask a
''' user for the file, or find an incident by the reference shown in the error
''' message. Logging never throws: a full disk must not crash the app.
''' </summary>
Friend Module AppLog

    Private ReadOnly SyncRoot As New Object()

    Friend ReadOnly Property LogFolder As String =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StockFlow", "Logs")

    Friend ReadOnly Property CurrentLogFile As String
        Get
            Return Path.Combine(LogFolder, $"stockflow-desktop-{Date.Today:yyyyMMdd}.log")
        End Get
    End Property

    Friend Sub Info(message As String)
        Write("INFO ", message, Nothing)
    End Sub

    Friend Sub Warning(message As String, Optional ex As Exception = Nothing)
        Write("WARN ", message, ex)
    End Sub

    ''' <summary>Logs an error and returns a short incident reference to show the user.</summary>
    Friend Function Failure(message As String, ex As Exception) As String
        Dim incident = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant()
        Write("ERROR", $"[{incident}] {message}", ex)
        Return incident
    End Function

    Private Sub Write(level As String, message As String, ex As Exception)
        Try
            Dim entry As New StringBuilder()
            entry.Append(Date.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).
                  Append(" "c).Append(level).
                  Append(" "c).Append(Environment.UserName).
                  Append(" "c).AppendLine(message)
            If ex IsNot Nothing Then
                entry.AppendLine(ex.ToString())
            End If

            SyncLock SyncRoot
                Directory.CreateDirectory(LogFolder)
                File.AppendAllText(CurrentLogFile, entry.ToString())
            End SyncLock
        Catch
            ' Deliberately ignored: logging must never take the application down.
        End Try
    End Sub

End Module
