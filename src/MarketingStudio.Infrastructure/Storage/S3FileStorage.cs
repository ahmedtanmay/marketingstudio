using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using MarketingStudio.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarketingStudio.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>S3 endpoint. Self-hosted default: SeaweedFS S3 gateway (Decision Register D31).</summary>
    public string ServiceUrl { get; set; } = "http://localhost:8333";
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string Bucket { get; set; } = "marketingstudio";
    public string Region { get; set; } = "us-east-1";
}

internal static class S3ClientFactory
{
    public static IAmazonS3 Create(StorageOptions options) => new AmazonS3Client(
        new BasicAWSCredentials(options.AccessKey, options.SecretKey),
        new AmazonS3Config
        {
            ServiceURL = options.ServiceUrl,
            AuthenticationRegion = options.Region,
            ForcePathStyle = true,
            // Self-hosted S3-compatible stores do not all support the SDK's newer default checksums.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        });
}

/// <summary>Any S3-compatible object store. Keys follow {tenantId}/{brandId}/... (Multi-Tenant Design.md).</summary>
internal sealed class S3FileStorage(IAmazonS3 s3, IOptions<StorageOptions> options) : IFileStorage
{
    private string Bucket => options.Value.Bucket;

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken) =>
        await s3.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = Bucket,
                Key = key,
                InputStream = content,
                ContentType = contentType,
                AutoCloseStream = false,
                UseChunkEncoding = false,
            },
            cancellationToken);

    public async Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var response = await s3.GetObjectAsync(Bucket, key, cancellationToken);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new NotFoundException();
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken) =>
        await s3.DeleteObjectAsync(Bucket, key, cancellationToken);

    public async Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken)
    {
        // Guard against wiping more than one tenant/brand by mistake.
        if (prefix.Count(c => c == '/') < 2 || !prefix.EndsWith('/'))
        {
            throw new ArgumentException("Refusing to delete an over-broad prefix.", nameof(prefix));
        }

        string? continuation = null;
        do
        {
            var page = await s3.ListObjectsV2Async(
                new ListObjectsV2Request { BucketName = Bucket, Prefix = prefix, ContinuationToken = continuation },
                cancellationToken);
            foreach (var item in page.S3Objects ?? [])
            {
                await s3.DeleteObjectAsync(Bucket, item.Key, cancellationToken);
            }

            continuation = page.IsTruncated == true ? page.NextContinuationToken : null;
        }
        while (continuation is not null);
    }
}

/// <summary>
/// Creates the bucket at start-up if it does not exist (idempotent). Retries for up to ~60 s because
/// the storage container may still be starting when the API starts.
/// </summary>
internal sealed class StorageBucketInitializer(
    IAmazonS3 s3,
    IOptions<StorageOptions> options,
    ILogger<StorageBucketInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var bucket = options.Value.Bucket;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var buckets = await s3.ListBucketsAsync(cancellationToken);
                if (buckets.Buckets?.Any(b => b.BucketName == bucket) != true)
                {
                    await s3.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, cancellationToken);
                }

                return;
            }
            catch (Exception ex) when (attempt < 30 && ex is AmazonServiceException or HttpRequestException or IOException)
            {
                logger.LogWarning("Object storage not ready (attempt {Attempt}): {Message}", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Signed media links: the asset id, protected and time-limited with ASP.NET Core Data Protection.
/// Tokens can't be forged or extended, and storage stays private.
/// </summary>
internal sealed class DataProtectionMediaUrlSigner(IDataProtectionProvider provider, TimeProvider clock) : IMediaUrlSigner
{
    public const string PathPrefix = "/api/v1/media/files/";

    private readonly ITimeLimitedDataProtector _protector =
        provider.CreateProtector("MarketingStudio.MediaLinks.v1").ToTimeLimitedDataProtector();

    public string CreateUrl(string assetId, TimeSpan lifetime) =>
        PathPrefix + _protector.Protect(assetId, clock.GetUtcNow() + lifetime);

    public string? ReadToken(string token)
    {
        try
        {
            return _protector.Unprotect(token, out var expiration) is { } id && expiration > clock.GetUtcNow() ? id : null;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }
}
