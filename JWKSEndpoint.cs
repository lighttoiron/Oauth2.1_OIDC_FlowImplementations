using System;
using Microsoft.IdentityModel.Tokens;
using System.Text.Json;

static class JWKSEndpoint
{
    public static void Map(WebApplication app, JsonSerializerOptions jsonOptions)
    {
        app.MapGet("/jwks", (ISigningKeyProvider keys) =>
        {
            // JsonWebKeyConverter turns our RsaSecurityKey into the standard JWK format
            // We only expose the PUBLIC key here, the private key never leaves the auth server
            var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(keys.PublicKey);
            jwk.Use = "sig"; // sig means this key is used for signing JWTs, not encryption

            return Results.Json(new { keys = new[] { jwk } }, jsonOptions);
        });
        // NOTE: tje JSON object returned has these fields:
        // kty: "RSA" - key type
        // use: "sig" - this key is for signing, not encryption
        // kid: "lab-key-1" - this is the key id, used by the API to look up which key to use to verify when a token arrives (tokens will have a matching kid in their header)
        // n and e - the RSA public key modulus and exponent, base64 URL encoded.  These are the values needed to reconstruct the RSA public key for verifying JWT signatures.
    }
}