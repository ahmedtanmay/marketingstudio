using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;

namespace MarketingStudio.Infrastructure.Persistence;

/// <summary>
/// Persistence conventions, kept out of the Domain so it has no MongoDB dependency:
/// camelCase field names, enums as strings, unknown fields ignored (safe for rolling deploys).
/// </summary>
internal static class MongoMappings
{
    private static int _registered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
        {
            return;
        }

        // Money as Decimal128 (exact), never as double or string.
        BsonSerializer.TryRegisterSerializer(new DecimalSerializer(BsonType.Decimal128));
        BsonSerializer.TryRegisterSerializer(new NullableSerializer<decimal>(new DecimalSerializer(BsonType.Decimal128)));

        var pack = new ConventionPack
        {
            new CamelCaseElementNameConvention(),
            new IgnoreExtraElementsConvention(true),
            new EnumRepresentationConvention(BsonType.String),
        };

        ConventionRegistry.Register(
            "MarketingStudio",
            pack,
            type => type.Namespace?.StartsWith("MarketingStudio.", StringComparison.Ordinal) == true);
    }
}
