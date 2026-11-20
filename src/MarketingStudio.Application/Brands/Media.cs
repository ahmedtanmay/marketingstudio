using FluentValidation;
using FluentValidation.Results;
using MarketingStudio.Application.Abstractions;
using MarketingStudio.Domain.Brands;
using MediatR;

namespace MarketingStudio.Application.Brands;

public sealed record MediaDto(
    string Id,
    string BrandId,
    string FileName,
    string ContentType,
    long SizeBytes,
    IReadOnlyList<string> Tags,
    string Url,
    DateTime CreatedAt);

internal static class MediaMapping
{
    /// <summary>Signed links stay valid for an hour; lists are re-fetched long before that.</summary>
    public static readonly TimeSpan UrlLifetime = TimeSpan.FromHours(1);

    public static MediaDto ToDto(this MediaAsset a, IMediaUrlSigner signer) =>
        new(a.Id, a.BrandId, a.FileName, a.ContentType, a.SizeBytes, a.Tags, signer.CreateUrl(a.Id, UrlLifetime), a.CreatedAt);

    public static List<string> CleanTags(IEnumerable<string>? tags) =>
        (tags ?? []).Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0).Distinct().ToList();
}

/// <summary>Identifies an image by its first bytes, never by the file name or the browser's claim.</summary>
public static class ImageSniffer
{
    public static string? Detect(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (header.Length >= 8 && header[..8].SequenceEqual((byte[])[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return "image/png";
        }

        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }

    public static string SafeFileName(string? fileName)
    {
        var name = Path.GetFileName(fileName ?? string.Empty);
        name = new string(name.Where(c => !char.IsControl(c) && c != '"' && c != '\\' && c != '/').ToArray()).Trim();
        if (name.Length == 0)
        {
            name = "image";
        }

        return name.Length > MediaRules.MaxFileNameLength ? name[^MediaRules.MaxFileNameLength..] : name;
    }
}

// ---------------------------------------------------------------- BRD-3 upload

public sealed record UploadMediaCommand(string BrandId, string? FileName, long Length, Stream Content, IReadOnlyList<string>? Tags)
    : IRequest<MediaDto>;

public sealed class UploadMediaCommandHandler(
    IBrandRepository brands,
    IMediaRepository media,
    IFileStorage storage,
    IMediaUrlSigner signer,
    ITenantContext context,
    TimeProvider clock) : IRequestHandler<UploadMediaCommand, MediaDto>
{
    public const string TooLarge = "Images can be at most 10 MB.";
    public const string WrongType = "Upload a JPEG, PNG or WebP image.";

    public async Task<MediaDto> Handle(UploadMediaCommand request, CancellationToken cancellationToken)
    {
        var brand = await brands.RequireAsync(request.BrandId, cancellationToken);
        if (request.Length <= 0 || request.Length > MediaRules.MaxFileBytes)
        {
            throw Invalid(request.Length <= 0 ? WrongType : TooLarge);
        }

        // Buffer (≤ 10 MB) so the type check and the upload see exactly the same bytes.
        using var buffer = new MemoryStream();
        await request.Content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > MediaRules.MaxFileBytes)
        {
            throw Invalid(TooLarge);
        }

        var contentType = ImageSniffer.Detect(buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, 16))) ?? throw Invalid(WrongType);
        var tags = MediaMapping.CleanTags(request.Tags);
        if (tags.Count > MediaRules.MaxTags || tags.Any(t => t.Length > MediaRules.MaxTagLength))
        {
            throw Invalid($"Use at most {MediaRules.MaxTags} tags of up to {MediaRules.MaxTagLength} characters.");
        }

        var asset = new MediaAsset
        {
            TenantId = context.RequireTenantId(),
            BrandId = brand.Id,
            FileName = ImageSniffer.SafeFileName(request.FileName),
            ContentType = contentType,
            SizeBytes = buffer.Length,
            Tags = tags,
            UploadedBy = context.RequireUserId(),
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        asset.StorageKey = MediaAsset.KeyFor(asset.TenantId, asset.BrandId, asset.Id);

        buffer.Position = 0;
        await storage.PutAsync(asset.StorageKey, buffer, contentType, cancellationToken);
        await media.AddAsync(asset, cancellationToken);
        return asset.ToDto(signer);
    }

    private static ValidationException Invalid(string message) => new([new ValidationFailure("file", message)]);
}

// ---------------------------------------------------------------- list / tags / delete

public sealed record ListMediaQuery(string BrandId, string? Tag) : IRequest<IReadOnlyList<MediaDto>>;

public sealed class ListMediaQueryHandler(IBrandRepository brands, IMediaRepository media, IMediaUrlSigner signer)
    : IRequestHandler<ListMediaQuery, IReadOnlyList<MediaDto>>
{
    public async Task<IReadOnlyList<MediaDto>> Handle(ListMediaQuery request, CancellationToken cancellationToken)
    {
        await brands.RequireAsync(request.BrandId, cancellationToken);
        var tag = string.IsNullOrWhiteSpace(request.Tag) ? null : request.Tag.Trim().ToLowerInvariant();
        return (await media.ListAsync(request.BrandId, tag, cancellationToken))
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => a.ToDto(signer))
            .ToList();
    }
}

public sealed record UpdateMediaTagsCommand(string BrandId, string AssetId, IReadOnlyList<string> Tags) : IRequest<MediaDto>;

public sealed class UpdateMediaTagsCommandValidator : AbstractValidator<UpdateMediaTagsCommand>
{
    public UpdateMediaTagsCommandValidator()
    {
        RuleFor(x => x.Tags).NotNull().Must(t => t.Count <= MediaRules.MaxTags)
            .WithMessage($"Use at most {MediaRules.MaxTags} tags.");
        RuleForEach(x => x.Tags).MaximumLength(MediaRules.MaxTagLength);
    }
}

public sealed class UpdateMediaTagsCommandHandler(IMediaRepository media, IMediaUrlSigner signer)
    : IRequestHandler<UpdateMediaTagsCommand, MediaDto>
{
    public async Task<MediaDto> Handle(UpdateMediaTagsCommand request, CancellationToken cancellationToken)
    {
        var asset = await media.GetByIdAsync(request.BrandId, request.AssetId, cancellationToken)
            ?? throw new NotFoundException("Image not found.");
        asset.Tags = MediaMapping.CleanTags(request.Tags);
        await media.UpdateAsync(asset, cancellationToken);
        return asset.ToDto(signer);
    }
}

public sealed record DeleteMediaCommand(string BrandId, string AssetId) : IRequest;

public sealed class DeleteMediaCommandHandler(IMediaRepository media, IProductRepository products, IFileStorage storage)
    : IRequestHandler<DeleteMediaCommand>
{
    public async Task Handle(DeleteMediaCommand request, CancellationToken cancellationToken)
    {
        var asset = await media.GetByIdAsync(request.BrandId, request.AssetId, cancellationToken)
            ?? throw new NotFoundException("Image not found.");
        await products.RemoveImageAsync(asset.BrandId, asset.Id, cancellationToken);
        await media.DeleteAsync(asset.BrandId, asset.Id, cancellationToken);
        await storage.DeleteAsync(asset.StorageKey, cancellationToken);
    }
}

// ---------------------------------------------------------------- signed delivery

public sealed record MediaFile(Stream Content, string ContentType, string FileName);

/// <summary>Anonymous delivery through a signed, expiring link. Invalid or expired links look like "not found".</summary>
public sealed record OpenMediaFileQuery(string Token) : IRequest<MediaFile>;

public sealed class OpenMediaFileQueryHandler(IMediaUrlSigner signer, IMediaRepository media, IFileStorage storage)
    : IRequestHandler<OpenMediaFileQuery, MediaFile>
{
    public async Task<MediaFile> Handle(OpenMediaFileQuery request, CancellationToken cancellationToken)
    {
        var assetId = signer.ReadToken(request.Token) ?? throw new NotFoundException();
        var asset = await media.FindForDeliveryAsync(assetId, cancellationToken) ?? throw new NotFoundException();
        var stream = await storage.OpenReadAsync(asset.StorageKey, cancellationToken);
        return new MediaFile(stream, asset.ContentType, asset.FileName);
    }
}
