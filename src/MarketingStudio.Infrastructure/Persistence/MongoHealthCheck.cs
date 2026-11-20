using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MarketingStudio.Infrastructure.Persistence;

internal sealed class MongoHealthCheck(MongoContext context) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext healthContext,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await context.Database.RunCommandAsync<BsonDocument>(
                new BsonDocument("ping", 1),
                cancellationToken: cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is MongoException or TimeoutException)
        {
            return HealthCheckResult.Unhealthy("MongoDB is not reachable.", ex);
        }
    }
}
