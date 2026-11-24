using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace StockFlow.Data.Deployment;

/// <summary>
/// Deploys the database from the /database folder:
///
///   migrations/       run ONCE each, in name order, tracked in dbo.SchemaVersion.
///                     A migration that was edited after it ran is an error:
///                     changes must go in a new migration.
///   programmability/  procedures, run on EVERY deploy (CREATE OR ALTER), so the
///                     database always matches source control.
///   seed/             demo data, only with SeedDemoData and only into an empty database.
///
/// Each script runs in its own transaction, so a failed deploy leaves the
/// database at the last good script instead of half-applied.
/// </summary>
public sealed partial class DatabaseDeployer
{
    private readonly string _connectionString;
    private readonly string _scriptsRoot;
    private readonly Action<string> _log;

    public DatabaseDeployer(string connectionString, string scriptsRoot, Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A connection string is required.", nameof(connectionString));
        }

        if (!Directory.Exists(scriptsRoot))
        {
            throw new DirectoryNotFoundException($"Database scripts folder not found: {scriptsRoot}");
        }

        _connectionString = connectionString;
        _scriptsRoot = scriptsRoot;
        _log = log ?? (_ => { });
    }

    public bool SeedDemoData { get; set; }

    public async Task DeployAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseExistsAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await EnsureVersionTableAsync(connection, cancellationToken).ConfigureAwait(false);
        var applied = await GetAppliedMigrationsAsync(connection, cancellationToken).ConfigureAwait(false);

        foreach (var file in ScriptFiles("migrations"))
        {
            var name = Path.GetFileName(file);
            var script = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            var checksum = Checksum(script);

            if (applied.TryGetValue(name, out var appliedChecksum))
            {
                if (!string.Equals(appliedChecksum, checksum, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Migration {name} has changed since it was applied. Never edit an applied migration; add a new one instead.");
                }

                continue;
            }

            _log($"Applying migration {name}");
            await RunScriptAsync(connection, script, name, recordAs: (name, checksum), cancellationToken).ConfigureAwait(false);
        }

        foreach (var file in ScriptFiles("programmability"))
        {
            var name = Path.GetFileName(file);
            _log($"Deploying {name}");
            var script = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            await RunScriptAsync(connection, script, name, recordAs: null, cancellationToken).ConfigureAwait(false);
        }

        if (SeedDemoData)
        {
            await SeedAsync(connection, cancellationToken).ConfigureAwait(false);
        }

        _log("Database is up to date.");
    }

    /// <summary>Splits a script into batches on lines containing only GO (as SSMS and sqlcmd do).</summary>
    public static IReadOnlyList<string> SplitBatches(string script)
    {
        return GoSeparator().Split(script)
            .Select(b => b.Trim())
            .Where(b => b.Length > 0)
            .ToList();
    }

    private async Task SeedAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using (var check = new SqlCommand("SELECT COUNT(*) FROM dbo.Product", connection))
        {
            var count = (int)(await check.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
            if (count > 0)
            {
                _log("Products already exist; skipping demo data.");
                return;
            }
        }

        foreach (var file in ScriptFiles("seed"))
        {
            var name = Path.GetFileName(file);
            _log($"Seeding {name}");
            var script = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            await RunScriptAsync(connection, script, name, recordAs: null, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task EnsureDatabaseExistsAsync(CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder(_connectionString);
        var database = builder.InitialCatalog;
        if (string.IsNullOrWhiteSpace(database))
        {
            throw new InvalidOperationException("The connection string must name a database (Initial Catalog / Database).");
        }

        builder.InitialCatalog = "master";
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new SqlCommand(
            "IF DB_ID(@Name) IS NULL BEGIN DECLARE @Sql NVARCHAR(400) = N'CREATE DATABASE ' + QUOTENAME(@Name); EXEC (@Sql); END",
            connection);
        command.Parameters.AddWithValue("@Name", database);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureVersionTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            IF OBJECT_ID(N'dbo.SchemaVersion', N'U') IS NULL
                CREATE TABLE dbo.SchemaVersion
                (
                    ScriptName NVARCHAR(255) NOT NULL CONSTRAINT PK_SchemaVersion PRIMARY KEY,
                    Checksum   CHAR(64)      NOT NULL,
                    AppliedAt  DATETIME2(0)  NOT NULL CONSTRAINT DF_SchemaVersion_AppliedAt DEFAULT (SYSUTCDATETIME()),
                    AppliedBy  NVARCHAR(128) NOT NULL CONSTRAINT DF_SchemaVersion_AppliedBy DEFAULT (SUSER_SNAME())
                );
            """;
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Dictionary<string, string>> GetAppliedMigrationsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var applied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var command = new SqlCommand("SELECT ScriptName, Checksum FROM dbo.SchemaVersion", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            applied[reader.GetString(0)] = reader.GetString(1);
        }

        return applied;
    }

    private static async Task RunScriptAsync(SqlConnection connection, string script, string name, (string Name, string Checksum)? recordAs, CancellationToken cancellationToken)
    {
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var batchNumber = 0;
        try
        {
            foreach (var batch in SplitBatches(script))
            {
                batchNumber++;
                await using var command = new SqlCommand(batch, connection, transaction) { CommandTimeout = 300 };
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            if (recordAs is { } record)
            {
                await using var insert = new SqlCommand(
                    "INSERT dbo.SchemaVersion (ScriptName, Checksum) VALUES (@Name, @Checksum)", connection, transaction);
                insert.Parameters.AddWithValue("@Name", record.Name);
                insert.Parameters.AddWithValue("@Checksum", record.Checksum);
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqlException ex)
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw new InvalidOperationException($"{name}, batch {batchNumber}: {ex.Message}", ex);
        }
    }

    private IEnumerable<string> ScriptFiles(string folder)
    {
        var path = Path.Combine(_scriptsRoot, folder);
        return Directory.Exists(path)
            ? Directory.GetFiles(path, "*.sql").OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            : Enumerable.Empty<string>();
    }

    /// <summary>Line endings are normalised so a Windows/Linux checkout does not look like an edit.</summary>
    private static string Checksum(string script)
    {
        var normalised = script.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalised)));
    }

    [GeneratedRegex(@"^\s*GO\s*(?:--.*)?$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoSeparator();
}
