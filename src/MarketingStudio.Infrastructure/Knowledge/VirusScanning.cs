using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using MarketingStudio.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarketingStudio.Infrastructure.Knowledge;

public sealed class VirusScanOptions
{
    public const string Section = "VirusScan";

    /// <summary>"ClamAV", or "None" for local development without the scanner (refused in Production).</summary>
    public string Provider { get; set; } = "ClamAV";
    public string Host { get; set; } = "clamav";
    public int Port { get; set; } = 3310;
    public int TimeoutSeconds { get; set; } = 60;
}

/// <summary>ClamAV over clamd's INSTREAM protocol (TCP). Any connection problem is "unavailable", never "clean".</summary>
internal sealed class ClamAvScanner(IOptions<VirusScanOptions> options, ILogger<ClamAvScanner> logger) : IVirusScanner
{
    private const int ChunkSize = 64 * 1024;

    public async Task<ScanResult> ScanAsync(Stream content, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(settings.Host, settings.Port, timeout.Token);
            await using var stream = client.GetStream();

            await stream.WriteAsync("zINSTREAM\0"u8.ToArray(), timeout.Token);
            var buffer = new byte[ChunkSize];
            var size = new byte[4];
            int read;
            while ((read = await content.ReadAsync(buffer, timeout.Token)) > 0)
            {
                BinaryPrimitives.WriteUInt32BigEndian(size, (uint)read);
                await stream.WriteAsync(size, timeout.Token);
                await stream.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
            }

            BinaryPrimitives.WriteUInt32BigEndian(size, 0);
            await stream.WriteAsync(size, timeout.Token);

            using var reply = new MemoryStream();
            await stream.CopyToAsync(reply, timeout.Token);
            var text = Encoding.ASCII.GetString(reply.ToArray()).TrimEnd('\0', '\n').Trim();

            if (text.EndsWith(" OK", StringComparison.Ordinal))
            {
                return new ScanResult(ScanOutcome.Clean);
            }

            if (text.EndsWith(" FOUND", StringComparison.Ordinal))
            {
                var signature = text["stream: ".Length..^" FOUND".Length];
                return new ScanResult(ScanOutcome.Infected, signature);
            }

            logger.LogWarning("Unexpected ClamAV reply: {Reply}", text);
            return new ScanResult(ScanOutcome.Unavailable);
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("ClamAV unavailable: {Message}", ex.Message);
            return new ScanResult(ScanOutcome.Unavailable);
        }
    }
}

/// <summary>Development only, when ClamAV isn't running. Start-up refuses it in Production.</summary>
internal sealed class NoVirusScanner : IVirusScanner
{
    public Task<ScanResult> ScanAsync(Stream content, CancellationToken cancellationToken) =>
        Task.FromResult(new ScanResult(ScanOutcome.Clean));
}
