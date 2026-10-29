using System.Security.Claims;
using System.Security.Cryptography;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;
using LifeOs.Infrastructure.Integrations;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LifeOs.Api.Auth;

/// <summary>
/// Signs the token each SuperJournal call carries (D19, csdelan/LifeOS#28): ES256, <c>iss</c>
/// <c>lifeos</c>, <c>aud</c> <c>superjournal</c>, <c>sub</c> the person's Supabase user id (the
/// <c>sub</c> LifeOS already validates), <c>roles</c> <c>["owner"]</c>, valid for 5 minutes. A fresh
/// one per call. SuperJournal checks it against the public key in its <c>Jwt:Issuers</c>.
/// <para>
/// The private key is <c>SuperJournal:SigningKey</c> (a Fly secret): a P-256 key as PEM
/// (<c>BEGIN PRIVATE KEY</c> or <c>BEGIN EC PRIVATE KEY</c>, with real or <c>\n</c> line breaks),
/// or the base64 body of either on one line.
/// </para>
/// </summary>
public sealed class SuperJournalTokenSigner : ISuperJournalTokens
{
    public const string Issuer = "lifeos";
    public const string Audience = "superjournal";

    /// <summary>The only role LifeOS claims: the person owns the journals it writes for them.</summary>
    public const string OwnerRole = "owner";

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly SigningCredentials credentials;
    private readonly IClock clock;
    private readonly JsonWebTokenHandler handler = new() { SetDefaultTimesOnTokenCreation = false };

    public SuperJournalTokenSigner(ECDsa key, IClock clock)
    {
        if (key.KeySize != 256)
        {
            throw new InvalidOperationException(
                $"{SuperJournalOptions.SigningKeyKey} must be a P-256 (prime256v1) key for ES256, not a {key.KeySize}-bit one.");
        }

        // Not the shared default factory: it caches signers by the key's thumbprint, so another
        // signer with the same key, whose ECDsa has since been disposed, could be found there.
        credentials = new SigningCredentials(new ECDsaSecurityKey(key), SecurityAlgorithms.EcdsaSha256)
        {
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false },
        };
        this.clock = clock;
    }

    /// <summary>
    /// Reads <c>SuperJournal:SigningKey</c>. Throws <see cref="InvalidOperationException"/>, without
    /// the key in the message, when it's missing, isn't an EC private key, or isn't P-256.
    /// </summary>
    public static ECDsa ParseKey(string? keyText)
    {
        var text = keyText?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            throw new InvalidOperationException(
                $"{SuperJournalOptions.BaseUrlKey} is set, so {SuperJournalOptions.SigningKeyKey} is needed: LifeOS's ES256 private key for SuperJournal.");
        }

        var key = ECDsa.Create();
        try
        {
            if (text.Contains("-----BEGIN", StringComparison.Ordinal))
            {
                key.ImportFromPem(text.Replace("\\n", "\n", StringComparison.Ordinal));
            }
            else
            {
                var der = Convert.FromBase64String(text);
                try
                {
                    key.ImportPkcs8PrivateKey(der, out _);
                }
                catch (CryptographicException)
                {
                    key.ImportECPrivateKey(der, out _);
                }
            }

            // A public key imports too, but can't sign.
            _ = key.ExportParameters(includePrivateParameters: true);
        }
        catch (Exception e) when (e is CryptographicException or FormatException or ArgumentException)
        {
            key.Dispose();
            throw new InvalidOperationException(
                $"{SuperJournalOptions.SigningKeyKey} isn't an EC private key: give the PEM, or its base64 body on one line.", e);
        }

        if (key.KeySize != 256)
        {
            var bits = key.KeySize;
            key.Dispose();
            throw new InvalidOperationException(
                $"{SuperJournalOptions.SigningKeyKey} must be a P-256 (prime256v1) key for ES256, not a {bits}-bit one.");
        }

        return key;
    }

    public string TokenFor(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new ArgumentException("A SuperJournal token needs the person's user id.", nameof(subject));
        }

        var now = clock.UtcNow.UtcDateTime;
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now + Lifetime,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = subject,
                ["roles"] = new[] { OwnerRole },
            },
            SigningCredentials = credentials,
        });
    }
}

/// <summary>
/// Who a SuperJournal call is for, from the request's token (D19): the person behind it (its
/// <c>sub</c>, their Supabase user id) and the source a save records. A web session saves as
/// <c>api</c>; an agent's token through <c>/mcp</c> carries its <c>client_id</c> and saves as
/// <c>mcp:&lt;client_id&gt;</c>, still as the person behind the token. Both sign the same way.
/// </summary>
public static class JournalCallers
{
    public static JournalCaller For(ClaimsPrincipal user)
    {
        var subject = user.FindFirst("sub")?.Value?.Trim();
        if (string.IsNullOrEmpty(subject))
        {
            throw new SuperJournalException(SuperJournalFailure.Forbidden,
                "This sign-in carries no user id (sub), so SuperJournal can't tell whose journals to use.");
        }

        var clientId = AgentClaims.ClientId(user);
        return new JournalCaller(subject, clientId is null ? KernelSources.Api : KernelSources.Agent(clientId));
    }
}
