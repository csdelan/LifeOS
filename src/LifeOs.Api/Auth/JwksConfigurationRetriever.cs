using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace LifeOs.Api.Auth;

/// <summary>
/// Loads signing keys from a JWKS document (Supabase
/// <c>/auth/v1/.well-known/jwks.json</c>) into an OpenID configuration the
/// JwtBearer handler can consume. GoTrue does not always expose a full OIDC
/// discovery document, so we do not rely on <c>.well-known/openid-configuration</c>.
/// </summary>
internal sealed class JwksConfigurationRetriever(string issuer)
    : IConfigurationRetriever<OpenIdConnectConfiguration>
{
    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
        string address,
        IDocumentRetriever retriever,
        CancellationToken cancel)
    {
        var document = await retriever.GetDocumentAsync(address, cancel);
        var jwks = new JsonWebKeySet(document);
        var config = new OpenIdConnectConfiguration
        {
            Issuer = issuer,
            JwksUri = address,
        };
        foreach (var key in jwks.GetSigningKeys())
        {
            config.SigningKeys.Add(key);
        }

        return config;
    }
}
