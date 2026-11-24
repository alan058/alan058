Imports StockFlow.Core.Abstractions
Imports StockFlow.Core.Services
Imports StockFlow.Data

''' <summary>
''' The services the forms use. Built once at start-up and passed to each form
''' (simple constructor injection, no global state), which keeps forms testable
''' and makes their dependencies obvious.
''' </summary>
Friend NotInheritable Class AppServices

    Private Sub New(repositories As StockFlowRepositories, provider As DataProvider)
        Products = New ProductService(repositories.Products)
        Inventory = New InventoryService(repositories.Inventory)
        Orders = New OrderService(repositories.Orders)
        Reports = repositories.Reports
        CurrentUser = $"{Environment.UserDomainName}\{Environment.UserName}"
        ProviderDescription = If(provider = DataProvider.SqlServer, "SQL Server", "In-memory demo data (changes are not saved)")
    End Sub

    Friend ReadOnly Property Products As ProductService
    Friend ReadOnly Property Inventory As InventoryService
    Friend ReadOnly Property Orders As OrderService
    Friend ReadOnly Property Reports As IReportRepository

    ''' <summary>Recorded on every stock movement for the audit trail.</summary>
    Friend ReadOnly Property CurrentUser As String

    Friend ReadOnly Property ProviderDescription As String

    Friend Shared Function Create(settings As DesktopSettings) As AppServices
        ArgumentNullException.ThrowIfNull(settings)
        Dim repositories = StockFlowDataFactory.Create(settings.Database)
        Return New AppServices(repositories, settings.Database.Provider)
    End Function

End Class
