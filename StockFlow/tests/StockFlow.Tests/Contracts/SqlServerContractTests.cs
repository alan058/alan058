using Microsoft.Data.SqlClient;
using StockFlow.Core.Abstractions;
using StockFlow.Data;
using StockFlow.Data.Deployment;

namespace StockFlow.Tests.Contracts;

/// <summary>
/// Runs the contract tests against a real SQL Server. Set STOCKFLOW_TEST_SQL to
/// a connection string for a server you can create databases on, for example:
///   Server=localhost,1433;User Id=sa;Password=...;TrustServerCertificate=True
/// Without it these tests are skipped (CI always sets it).
/// </summary>
public sealed class SqlServerContractTests : RepositoryContractTests, IClassFixture<SqlServerDatabaseFixture>
{
    private readonly SqlServerDatabaseFixture _fixture;

    public SqlServerContractTests(SqlServerDatabaseFixture fixture) => _fixture = fixture;

    protected override bool IsAvailable => _fixture.Repositories is not null;

    protected override StockFlowRepositories Repositories => _fixture.Repositories!;
}

/// <summary>Deploys a throw-away database once for the test class and drops it afterwards.</summary>
public sealed class SqlServerDatabaseFixture : IAsyncLifetime
{
    public const string EnvironmentVariable = "STOCKFLOW_TEST_SQL";

    private string? _serverConnectionString;
    private string? _databaseName;

    public StockFlowRepositories? Repositories { get; private set; }

    public async Task InitializeAsync()
    {
        _serverConnectionString = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(_serverConnectionString))
        {
            return;
        }

        _databaseName = $"StockFlowTest_{Guid.NewGuid():N}";
        var builder = new SqlConnectionStringBuilder(_serverConnectionString) { InitialCatalog = _databaseName };

        var deployer = new DatabaseDeployer(builder.ConnectionString, Path.Combine(AppContext.BaseDirectory, "database"))
        {
            SeedDemoData = true,
        };
        await deployer.DeployAsync();

        Repositories = StockFlowDataFactory.Create(new DataOptions
        {
            Provider = DataProvider.SqlServer,
            ConnectionString = builder.ConnectionString,
        });
    }

    public async Task DisposeAsync()
    {
        if (_databaseName is null || _serverConnectionString is null)
        {
            return;
        }

        SqlConnection.ClearAllPools();
        var builder = new SqlConnectionStringBuilder(_serverConnectionString) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}];",
            connection);
        await command.ExecuteNonQueryAsync();
    }
}
