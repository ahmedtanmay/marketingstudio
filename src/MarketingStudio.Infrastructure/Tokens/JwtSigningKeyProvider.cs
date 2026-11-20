using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MarketingStudio.Infrastructure.Tokens;

/// <summary>
/// Loads the ECDSA P-256 signing key (ES256) used for access tokens. In production the PEM file is a
/// Docker Secret; in development it can be generated on first run.
/// </summary>
public sealed class JwtSigningKeyProvider
{
    public JwtSigningKeyProvider(IOptions<JwtOptions> options)
    {
        var settings = options.Value;
        var ecdsa = ECDsa.Create();

        if (File.Exists(settings.SigningKeyPath))
        {
            ecdsa.ImportFromPem(File.ReadAllText(settings.SigningKeyPath));
        }
        else if (settings.CreateKeyIfMissing)
        {
            ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var directory = Path.GetDirectoryName(Path.GetFullPath(settings.SigningKeyPath));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(settings.SigningKeyPath, ecdsa.ExportPkcs8PrivateKeyPem());
        }
        else
        {
            throw new InvalidOperationException(
                $"JWT signing key not found at '{settings.SigningKeyPath}'. "
                + "Provide it as a Docker Secret, or set Jwt:CreateKeyIfMissing=true in development.");
        }

        if (ecdsa.KeySize != 256)
        {
            throw new InvalidOperationException("The JWT signing key must be an ECDSA P-256 key.");
        }

        var keyId = Base64UrlEncoder.Encode(SHA256.HashData(ecdsa.ExportSubjectPublicKeyInfo()))[..16];
        Key = new ECDsaSecurityKey(ecdsa) { KeyId = keyId };
        Credentials = new SigningCredentials(Key, SecurityAlgorithms.EcdsaSha256);
    }

    public ECDsaSecurityKey Key { get; }
    public SigningCredentials Credentials { get; }
}
