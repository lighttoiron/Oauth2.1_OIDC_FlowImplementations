using System.Text.Json;
using Microsoft.Extensions.Options;

// The discovery endpoint at /.well-known/openid-configuration
// This is an OIDC-defined endpoint with a specific path that returns information about the auth server so clients know which endpoints to call, supported scopes, etc.
static class DiscoveryEndpoint
{
    public static void Map(WebApplication app, JsonSerializerOptions jsonOptions)
    {
        app.MapGet("/.well-known/openid-configuration", (IOptions<AuthServerOptions> options) =>
        {
            var Issuer = options.Value.Issuer;
            
            var doc = new
            {
                // The canonical URL / identity of the auth server.
                // This is embedded in the tokens provided and is used by clients to verify that the tokens come from a known issuer
                issuer = Issuer,
                // These endpoints are used by clients to interact with the auth server.
                // Clients should not hard code these as they may change at the discretion of the auth server.
                authorization_endpoint = $"{Issuer}/authorize",
                // The token endpoint where an authorization code may be exchanged for tokens.
                token_endpoint = $"{Issuer}/token",
                // jwks stands for JSON Web Key Set, which is a standard format for representing public keys used in JWT signing
                jwks_uri = $"{Issuer}/jwks",
                // Add any supported response types here, like code or id_token, etc. (though code is deprecated)
                response_types_supported = new[] { "code" },
                // subject_types are used to indicate how the subject (user) is identified.
                // "public" means that the same subject identifier is returned for the same user across all clients,
                // while "pairwise" means that a different subject identifier is returned for each client (for privacy).
                subject_types_supported = new[] { "public"},
                // Add any supported signing algorithms here, like RS256, HS256, etc.
                id_token_signing_alg_values_supported = new [] { "RS256" },
                // A list of all supported scopes that can be requested by clients.
                // Scopes are used to specify the level of access that the client is requesting from the user.
                scopes_supported = new[] { "openid", "profile", "email", "offline_access" },
                // A list of supported authentication methods for the token endpoint. This indicates how clients can authenticate when making requests to the token endpoint.
                // Common methods include "client_secret_basic", "client_secret_post", "client_secret_jwt", "private_key_jwt", and "none" (for public clients that do not require authentication).
                token_endpoint_auth_methods_supported = new[] { "none" },
                // shows we use a S256 hashing method for PKCE.  plain (where the verifier is sent as-is) is not recommended or secure
                code_challenge_methods_supported = new[] { "S256" }
            };

            return Results.Json(doc, jsonOptions);
        });
    }
}