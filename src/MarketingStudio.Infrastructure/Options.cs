namespace MarketingStudio.Infrastructure;

public sealed class MongoOptions
{
    public const string Section = "Mongo";
    public string ConnectionString { get; set; } = "mongodb://localhost:27017/?replicaSet=rs0";
    public string Database { get; set; } = "marketingstudio";
}

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public string Issuer { get; set; } = "https://app.marketingstudio.ai";
    public string Audience { get; set; } = "marketingstudio-api";
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>PEM file with the ECDSA P-256 private key (Docker Secret in production).</summary>
    public string SigningKeyPath { get; set; } = "/run/secrets/jwt_signing_key";

    /// <summary>Development only: generate a key at <see cref="SigningKeyPath"/> when the file is missing.</summary>
    public bool CreateKeyIfMissing { get; set; }
}

public sealed class RefreshTokenOptions
{
    public const string Section = "RefreshTokens";
    public int LifetimeDays { get; set; } = 7;
    public int AbsoluteSessionDays { get; set; } = 30;
}

public sealed class AppLinkOptions
{
    public const string Section = "App";
    public string PortalBaseUrl { get; set; } = "https://app.marketingstudio.ai";
}

public sealed class EmailOptions
{
    public const string Section = "Email";

    /// <summary>When empty, emails are only logged (recipient and subject — never links or tokens).</summary>
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 1025;
    public bool UseSsl { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string From { get; set; } = "AI Marketing Studio <no-reply@marketingstudio.ai>";
}

public sealed class DataProtectionSettings
{
    public const string Section = "DataProtection";

    /// <summary>Directory for the Data Protection key ring (a persistent volume in production).</summary>
    public string? KeysPath { get; set; }
}
