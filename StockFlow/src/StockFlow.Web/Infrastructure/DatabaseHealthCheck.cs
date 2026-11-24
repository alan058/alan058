using Microsoft.Extensions.Diagnostics.HealthChecks;
using StockFlow.Core.Abstractions;

namespace StockFlow.Web.Infrastructure;

/// <summary>Reports Unhealthy when the database cannot be reached. Used by load balancers and release smoke checks.</summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly IReportRepository _reports;

    public DatabaseHealthCheck(IReportRepository reports) => _reports = reports;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _reports.PingAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Database ping returned no result.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database unreachable.", ex);
        }
    }
}
