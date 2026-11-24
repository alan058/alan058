using StockFlow.Data.Deployment;

// Usage:
//   stockflow-dbdeploy --connection "<connection string>" [--scripts <folder>] [--seed-demo]
// The connection string can also come from the STOCKFLOW_DB environment variable,
// which keeps passwords out of shell history and CI logs.

string? connection = Environment.GetEnvironmentVariable("STOCKFLOW_DB");
string scripts = Path.Combine(AppContext.BaseDirectory, "database");
bool seed = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--connection" when i + 1 < args.Length:
            connection = args[++i];
            break;
        case "--scripts" when i + 1 < args.Length:
            scripts = args[++i];
            break;
        case "--seed-demo":
            seed = true;
            break;
        case "--help" or "-h":
            PrintUsage();
            return 0;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            PrintUsage();
            return 2;
    }
}

if (string.IsNullOrWhiteSpace(connection))
{
    Console.Error.WriteLine("No connection string. Pass --connection or set STOCKFLOW_DB.");
    return 2;
}

try
{
    var deployer = new DatabaseDeployer(connection, scripts, message => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}"))
    {
        SeedDemoData = seed,
    };
    await deployer.DeployAsync();
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Deployment FAILED: {ex.Message}");
    return 1;
}

static void PrintUsage() =>
    Console.WriteLine("stockflow-dbdeploy --connection \"<connection string>\" [--scripts <folder>] [--seed-demo]");
