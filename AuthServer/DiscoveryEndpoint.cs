using System;
using System.Text.Json;
using Microsoft.Extensions.Options;

static class DiscoveryEndpoint
{
    public static void Map(WebApplication app, JsonSerializerOptions jsonOptions)
    {
        app.MapGet("/.well-known/openid-configuration", (IOptions<AuthServerOptions> options) =>
        {
            var Issuer = options.Value.Issuer;
            
            var doc = new
            {
                issuer = Issuer, // The canonical URL / identity of the auth server.  This is embedded in the tokens provided and is used by clients to verify that the tokens come from a known issuer
                authorization_endpoint = $"{Issuer}/authorize", // These endpoints are used by clients to interact with the auth server.  Clients should not hard code these as they may change at the discretion of the auth server.
                token_endpoint = $"{Issuer}/token",
                jwks_uri = $"{Issuer}/jwks", // jwks stands for JSON Web Key Set, which is a standard format for representing public keys used in JWT signing
                response_types_supported = new[] { "code" }, // Add any supported response types here, like code or id_token, etc.  Use code only where possible.
                subject_types_supported = new[] { "public"}, // subject_types are used to indicate how the subject (user) is identified. "public" means that the same subject identifier is returned for the same user across all clients, while "pairwise" means that a different subject identifier is returned for each client (for privacy).
                id_token_signing_alg_values_supported = new [] { "RS256" }, // Add any supported signing algorithms here, like RS256, HS256, etc.
                scopes_supported = new[] { "openid", "profile", "email" }, // A list of all supported scopes that can be requested by clients. Scopes are used to specify the level of access that the client is requesting from the user.
                token_endpoint_auth_methods_supported = new[] { "none" }, // A list of supported authentication methods for the token endpoint. This indicates how clients can authenticate when making requests to the token endpoint. Common methods include "client_secret_basic", "client_secret_post", "client_secret_jwt", "private_key_jwt", and "none" (for public clients that do not require authentication).
                code_challenge_methods_supported = new[] { "S256" } // shows we use a S256 hashing method for PKCE.  plain (where the verifier is sent as-is) is not recommended or secure
            };

            return Results.Json(doc, jsonOptions);
        });
    }
}