using System.Data;
using Microsoft.Data.SqlClient;
using StockFlow.Core.Exceptions;

namespace StockFlow.Data.SqlServer;

/// <summary>
/// Runs stored procedures with consistent connection handling, timeouts,
/// retry on transient errors and exception translation. Every repository
/// goes through here, so there is one place to look when diagnosing
/// database problems.
/// </summary>
public sealed class SqlExecutor
{
    private readonly DataOptions _options;

    public SqlExecutor(DataOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new ArgumentException("A connection string is required.", nameof(options));
        }
    }

    /// <summary>Runs a procedure and maps its result sets with <paramref name="read"/>.</summary>
    public Task<T> QueryAsync<T>(
        string procedure,
        Action<SqlParameterCollection>? addParameters,
        Func<SqlDataReader, CancellationToken, Task<T>> read,
        CancellationToken cancellationToken)
    {
        return ExecuteWithRetryAsync(procedure, async command =>
        {
            addParameters?.Invoke(command.Parameters);
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.Default, cancellationToken).ConfigureAwait(false);
            return await read(reader, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <summary>Runs a procedure that returns no rows.</summary>
    public Task ExecuteAsync(string procedure, Action<SqlParameterCollection>? addParameters, CancellationToken cancellationToken)
    {
        return ExecuteWithRetryAsync(procedure, async command =>
        {
            addParameters?.Invoke(command.Parameters);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    private async Task<T> ExecuteWithRetryAsync<T>(string procedure, Func<SqlCommand, Task<T>> body, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var connectionOpened = false;
            try
            {
                await using var connection = new SqlConnection(_options.ConnectionString);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                connectionOpened = true;

                await using var command = connection.CreateCommand();
                command.CommandType = CommandType.StoredProcedure;
                command.CommandText = procedure;
                command.CommandTimeout = _options.CommandTimeoutSeconds;

                return await body(command).ConfigureAwait(false);
            }
            catch (SqlException ex) when (attempt < _options.MaxRetries && SqlErrorTranslator.IsSafeToRetry(ex, connectionOpened))
            {
                // Exponential back-off with jitter: 200ms, 400ms, 800ms (+ up to 100ms).
                var delay = TimeSpan.FromMilliseconds((200 * Math.Pow(2, attempt)) + Random.Shared.Next(0, 100));
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (SqlException ex)
            {
                throw SqlErrorTranslator.Translate(ex, procedure);
            }
        }
    }
}
