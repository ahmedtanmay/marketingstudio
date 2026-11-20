namespace MarketingStudio.Domain.Common;

/// <summary>
/// Generates string identifiers (UUID v7, no dashes) that sort by creation time at millisecond
/// resolution. IDs created within the same millisecond are unique but NOT ordered among themselves,
/// so never rely on ID order for sequencing — use a timestamp field.
/// Kept in the domain so entities can be created without a database round-trip.
/// </summary>
public static class IdGenerator
{
    public static string NewId() => Guid.CreateVersion7().ToString("N");

    public static string NewId(DateTimeOffset timestamp) => Guid.CreateVersion7(timestamp).ToString("N");
}
