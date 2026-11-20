namespace MarketingStudio.Domain.Common;

/// <summary>
/// Generates time-ordered string identifiers (UUID v7, no dashes).
/// Kept in the domain so entities can be created without a database round-trip.
/// </summary>
public static class IdGenerator
{
    public static string NewId() => Guid.CreateVersion7().ToString("N");
}
