''' <summary>
''' Entry point. Sets up structured, last-chance error handling before any form
''' is shown, so no exception can reach the user as a raw .NET crash dialog.
''' </summary>
Friend Module Program

    <STAThread>
    Friend Sub Main()
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException)
        AddHandler Application.ThreadException, AddressOf OnThreadException
        AddHandler AppDomain.CurrentDomain.UnhandledException, AddressOf OnUnhandledException

        Dim services As AppServices
        Try
            services = AppServices.Create(DesktopSettings.Load())
        Catch ex As Exception
            ErrorHandler.ShowStartupFailure(ex)
            Return
        End Try

        AppLog.Info($"Started. Data provider: {services.ProviderDescription}")
        Application.Run(New MainForm(services))
        AppLog.Info("Closed.")
    End Sub

    ''' <summary>Exceptions on the UI thread that no handler caught.</summary>
    Private Sub OnThreadException(sender As Object, e As Threading.ThreadExceptionEventArgs)
        ErrorHandler.Handle(e.Exception, Nothing, "processing your request")
    End Sub

    ''' <summary>Exceptions on background threads. The process is ending, so log and inform.</summary>
    Private Sub OnUnhandledException(sender As Object, e As UnhandledExceptionEventArgs)
        Dim ex = TryCast(e.ExceptionObject, Exception)
        Dim incident = AppLog.Failure("Fatal unhandled exception", If(ex, New Exception(Convert.ToString(e.ExceptionObject))))
        MessageBox.Show($"StockFlow has to close because of an unexpected error.{Environment.NewLine}{Environment.NewLine}Reference: {incident}",
                        "StockFlow", MessageBoxButtons.OK, MessageBoxIcon.Error)
    End Sub

End Module
