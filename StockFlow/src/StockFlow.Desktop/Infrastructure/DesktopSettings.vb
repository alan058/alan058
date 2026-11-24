Imports System.IO
Imports System.Text.Json
Imports System.Text.Json.Serialization
Imports StockFlow.Data

''' <summary>Reads appsettings.json from the application folder.</summary>
Friend NotInheritable Class DesktopSettings

    Friend Const FileName As String = "appsettings.json"
    Friend Const ConnectionEnvironmentVariable As String = "STOCKFLOW_DESKTOP_CONNECTION"

    Friend Property Database As New DataOptions()

    Friend Shared Function Load() As DesktopSettings
        Dim settings As New DesktopSettings()
        Dim filePath = Path.Combine(AppContext.BaseDirectory, FileName)

        If File.Exists(filePath) Then
            Dim options As New JsonSerializerOptions With {
                .PropertyNameCaseInsensitive = True,
                .ReadCommentHandling = JsonCommentHandling.Skip,
                .AllowTrailingCommas = True
            }
            options.Converters.Add(New JsonStringEnumConverter())

            Dim parsed = JsonSerializer.Deserialize(Of SettingsFile)(File.ReadAllText(filePath), options)
            If parsed?.StockFlow?.Data IsNot Nothing Then
                settings.Database = parsed.StockFlow.Data
            End If
        End If

        Dim connection = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)
        If Not String.IsNullOrWhiteSpace(connection) Then
            settings.Database.Provider = DataProvider.SqlServer
            settings.Database.ConnectionString = connection
        End If

        settings.Database.Validate()
        Return settings
    End Function

    ' Shapes of the JSON file. Public so System.Text.Json can create them.
    Public NotInheritable Class SettingsFile
        Public Property StockFlow As StockFlowSection
    End Class

    Public NotInheritable Class StockFlowSection
        Public Property Data As DataOptions
    End Class

End Class
