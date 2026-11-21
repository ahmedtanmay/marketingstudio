using System.Threading.Channels;
using MarketingStudio.Application.Abstractions;
using MarketingStudio.Application.Knowledge;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MarketingStudio.Infrastructure.Knowledge;

/// <summary>
/// In-process job queue for knowledge work. Progress lives in MongoDB (document status), so after a
/// restart the worker re-queues unfinished documents. Stands in for Hangfire until more job types
/// exist (Decision Register D32).
/// </summary>
internal sealed class KnowledgeJobScheduler : IKnowledgeJobScheduler
{
    private readonly Channel<KnowledgeJob> _channel = Channel.CreateUnbounded<KnowledgeJob>(new UnboundedChannelOptions { SingleReader = false });

    public ChannelReader<KnowledgeJob> Reader => _channel.Reader;

    public void Enqueue(KnowledgeJob job) => _channel.Writer.TryWrite(job);
}

internal sealed class KnowledgeWorker(
    KnowledgeJobScheduler scheduler,
    IServiceScopeFactory scopes,
    ILogger<KnowledgeWorker> logger) : BackgroundService
{
    /// <summary>Documents processed in parallel (embedding and extraction are the slow parts).</summary>
    private const int Concurrency = 2;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverAsync(stoppingToken);
        await Task.WhenAll(Enumerable.Range(0, Concurrency).Select(_ => ConsumeAsync(stoppingToken)));
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in scheduler.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<BackgroundTenantScope>().Set(job.TenantId);
                var outcome = await scope.ServiceProvider.GetRequiredService<KnowledgeProcessor>().RunAsync(job, stoppingToken);
                if (outcome == JobOutcome.RetryLater)
                {
                    _ = RequeueLaterAsync(job, stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Knowledge job {Kind} {SourceId} failed", job.Kind, job.SourceId);
            }
        }
    }

    private async Task RequeueLaterAsync(KnowledgeJob job, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(RetryDelay, stoppingToken);
            scheduler.Enqueue(job);
        }
        catch (OperationCanceledException)
        {
            // Shutting down; recovery re-queues it on the next start.
        }
    }

    private async Task RecoverAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var pending = await scope.ServiceProvider.GetRequiredService<IKnowledgeDocumentRepository>().ListUnfinishedAsync(stoppingToken);
            foreach (var doc in pending)
            {
                scheduler.Enqueue(new KnowledgeJob(KnowledgeJobKind.IngestDocument, doc.TenantId, doc.BrandId, doc.Id));
            }

            if (pending.Count > 0)
            {
                logger.LogInformation("Re-queued {Count} unfinished knowledge documents", pending.Count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not recover unfinished knowledge documents");
        }
    }
}
