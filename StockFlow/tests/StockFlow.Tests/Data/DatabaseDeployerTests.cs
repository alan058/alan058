using StockFlow.Data.Deployment;

namespace StockFlow.Tests.Data;

public sealed class DatabaseDeployerTests
{
    [Fact]
    public void SplitBatches_SplitsOnGoLinesOnly()
    {
        const string script = "CREATE TABLE GOODS (Id INT);\nGO\nSELECT 'GO' AS Word;\n  go  -- trailing comment\nSELECT 1;\nGO";

        var batches = DatabaseDeployer.SplitBatches(script);

        Assert.Equal(3, batches.Count);
        Assert.Equal("CREATE TABLE GOODS (Id INT);", batches[0]);
        Assert.Equal("SELECT 'GO' AS Word;", batches[1]);
        Assert.Equal("SELECT 1;", batches[2]);
    }

    [Fact]
    public void AllScripts_AreFoundAndInOrder()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "database");
        var migrations = Directory.GetFiles(Path.Combine(root, "migrations"), "*.sql").Select(Path.GetFileName).ToList();

        Assert.NotEmpty(migrations);
        Assert.All(migrations, name => Assert.Matches(@"^\d{4}_[A-Za-z0-9]+\.sql$", name!));
        Assert.Equal(migrations.Count, migrations.Select(m => m![..4]).Distinct().Count());
    }
}
