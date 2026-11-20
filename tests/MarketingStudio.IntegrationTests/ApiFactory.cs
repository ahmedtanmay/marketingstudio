using System.Collections.Concurrent;
using MarketingStudio.Application.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Testcontainers.MongoDb;

namespace MarketingStudio.IntegrationTests;

/// <summary>
/// Real API in memory against a real MongoDB (single-node replica set, so transactions work).
/// Requires Docker. One container is shared by all tests; each test uses unique emails.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MongoDbContainer _mongo = new MongoDbBuilder("mongo:8.0")
        .WithReplicaSet("rs0")
        .Build();

    private const string StorageAccessKey = "it-access";
    private const string StorageSecretKey = "it-secret-key";

    /// <summary>SeaweedFS S3 gateway (Decision Register D31), as in docker-compose.yml.</summary>
    private readonly IContainer _storage = new ContainerBuilder("chrislusf/seaweedfs:4.54")
        .WithCommand("server", "-dir=/data", "-s3", "-s3.config=/etc/seaweedfs/s3.json", "-master.volumeSizeLimitMB=64")
        .WithResourceMapping(
            System.Text.Encoding.UTF8.GetBytes(
                $$"""{"identities":[{"name":"it","credentials":[{"accessKey":"{{StorageAccessKey}}","secretKey":"{{StorageSecretKey}}"}],"actions":["Admin","Read","Write","List","Tagging"]}]}"""),
            FilePath.Of("/etc/seaweedfs/s3.json"))
        .WithPortBinding(8333, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(8333))
        .Build();

    private readonly string _keyDirectory = Path.Combine(Path.GetTempPath(), $"ms-it-{Guid.NewGuid():N}");

    public const int TrialSeats = 6;

    public CapturingEmailSender Emails { get; } = new();

    public async Task InitializeAsync() => await Task.WhenAll(_mongo.StartAsync(), _storage.StartAsync());

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _mongo.DisposeAsync();
        await _storage.DisposeAsync();
        await base.DisposeAsync();
        if (Directory.Exists(_keyDirectory))
        {
            Directory.Delete(_keyDirectory, recursive: true);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var connection = _mongo.GetConnectionString();
        if (!connection.Contains("directConnection", StringComparison.OrdinalIgnoreCase))
        {
            connection += (connection.Contains('?') ? "&" : "?") + "directConnection=true";
        }

        builder.UseEnvironment("Testing");
        builder.UseSetting("Mongo:ConnectionString", connection);
        builder.UseSetting("Mongo:Database", "marketingstudio_tests");
        builder.UseSetting("Jwt:Issuer", "https://tests.local");
        builder.UseSetting("Jwt:SigningKeyPath", Path.Combine(_keyDirectory, "jwt.pem"));
        builder.UseSetting("Jwt:CreateKeyIfMissing", "true");
        builder.UseSetting("DataProtection:KeysPath", Path.Combine(_keyDirectory, "dp"));
        builder.UseSetting("App:PortalBaseUrl", "https://portal.test");
        builder.UseSetting("Email:SmtpHost", "");
        builder.UseSetting("RateLimiting:AuthPermitPerMinute", "100000");
        builder.UseSetting("Storage:ServiceUrl", $"http://{_storage.Hostname}:{_storage.GetMappedPublicPort(8333)}");
        builder.UseSetting("Storage:AccessKey", StorageAccessKey);
        builder.UseSetting("Storage:SecretKey", StorageSecretKey);
        builder.UseSetting("Storage:Bucket", "marketingstudio-tests");
        builder.UseSetting("Plans:Brands:trial", "3");

        // Room for one user per role in the RBAC fixture; the seat-limit test fills it up.
        builder.UseSetting("Plans:Seats:trial", TrialSeats.ToString(System.Globalization.CultureInfo.InvariantCulture));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
        });
    }

    public HttpClient CreateApiClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
}

[CollectionDefinition(Name)]
public sealed class ApiTestGroup : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}

public sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public IReadOnlyList<EmailMessage> To(string email) =>
        _sent.Where(m => string.Equals(m.To, email, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>Extracts a query parameter from the first link in the latest email to the address.</summary>
    public string LinkParameter(string email, string subjectContains, string parameter)
    {
        var message = To(email).Last(m => m.Subject.Contains(subjectContains, StringComparison.OrdinalIgnoreCase));
        var url = message.TextBody.Split((char[])[' ', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .First(w => w.StartsWith("https://portal.test/", StringComparison.Ordinal));
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query);
        return query[parameter] ?? throw new InvalidOperationException($"No '{parameter}' in {url}");
    }
}
