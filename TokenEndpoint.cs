using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;

// The token endpoint is used to exhange the client's authorization code for valid tokens, ID tokens, access tokens, etc.
static class TokenEndpoint
{
    public static void Map(WebApplication app, string Issuer)
    {
        app.MapPost("/token", async (
            HttpContext context,
            ISigningKeyProvider keys,
            IOptions<AuthServerOptions> options ) =>
        {
            var ApiAudience = options.Value.ApiAudience;
            
            if (!context.Request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "invalid_request", error_message = "Expected form-encoded body." });
            }

            // read the content of the form we received
            var form = await context.Request.ReadFormAsync();

            // Extract the information we need to validate the request from the form
            var grantType = form["grant_type"].ToString();
            var code = form["code"].ToString();
            var redirectUri = form["redirect_uri"].ToString();
            var clientId = form["client_id"].ToString();
            var codeVerifier = form["code_verifier"].ToString();

            // Validate the request based on the form information provided
            if (grantType != "authorization_code")
            {
                return Results.BadRequest(new {});
            }

            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(codeVerifier))
            {
                return Results.BadRequest(new { error = "invalid_request", error_message = "Missing Code or Code Verifier in the request." });
            }

            // The auth code is single use, pull it out of our ConcurrentDictionary regardless of whether or not it is valid
            // Further attempts to use the code should always fail.
            if (!AuthorizeEndpoint.AuthCodes.TryRemove(code, out var authCodeData))
            {
                return Results.BadRequest(new { error = "invalid_grant", error_message = "Invalid auth code.  Code is either unknown, already used, or expired." } );
            }

            // Validate the provided auth code information against the stored auth code data
            if (authCodeData.ExpiresAt < DateTime.UtcNow)
            {
                return Results.BadRequest(new{ error = "invalid_grant", error_message = "Authorization code has expired." });
            }

            if (authCodeData.RedirectUri != redirectUri || authCodeData.ClientId != clientId)
            {
                return Results.BadRequest(new { error = "invalid_grant", error_message = "redirect_uri or client_id mismatch."});
            }

            // PKCE verification, make sure that the client is the same one who initially requested the auth code from /authorize
            // Hash the code_verifier the client sent us and compare to the code_challenge sent to the /authorize endpoint
            // Hash the provided code verifier
            var verifierHash = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.ASCII.GetBytes(codeVerifier));
            // Convert to base64 string for comparison.  This string cleanup should match how the client generates their code_challenge
            var computedChallenge = Convert.ToBase64String(verifierHash)
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');

            if (computedChallenge != authCodeData.CodeChallenge)
            {
                return Results.BadRequest(new { error = "invalid_grant", error_message="PKCE verification failed.  The computed hash did not match the code_challenge provided to /authorize." });
            }

            var tokenHandler = new JwtSecurityTokenHandler();
            // Note: usually this would be handled in a secure key store like Azure Key Vault, not done in the server backend where we should never store the private key
            var signingCredentials = new SigningCredentials(keys.PrivateKey, SecurityAlgorithms.RsaSha256);

            var now = DateTime.UtcNow;
            var accessTokenExpiry = now.AddMinutes(15); // Access token will be valid for 15 minutes
            var idTokenExpiry = now.AddMinutes(15); // ID token will be valid for 15 minutes

            // --- Access token: the protected API will verify this on every request ---
            // Contains who the user is (sub), what they are allowed to do (scope),
            //  and who issued the token / who the token is for (iss, aud)
            var accessTokenClaims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, authCodeData.Subject),
                new Claim("scope", authCodeData.Scope),
                new Claim("client_id", authCodeData.ClientId)
            };

            var accessToken = tokenHandler.CreateEncodedJwt(new SecurityTokenDescriptor
            {
                Issuer = Issuer,
                Audience = ApiAudience, // token is for the API audience
                Subject = new ClaimsIdentity(accessTokenClaims),
                IssuedAt = now,
                NotBefore = now,
                Expires = accessTokenExpiry,
                SigningCredentials = signingCredentials
            });

            // --- ID token: proof of authenticatoin, only used on the front end of the client, not passed to the API ---
            // aud here is the CLIENT aud value, not the API aud value since the client will consume this
            var idTokenClaims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, authCodeData.Subject)
            };

            if (!string.IsNullOrEmpty(authCodeData.Nonce))
            {
                idTokenClaims.Add(new Claim(JwtRegisteredClaimNames.Nonce, authCodeData.Nonce));
            }

            var idToken = tokenHandler.CreateEncodedJwt(new SecurityTokenDescriptor
            {
                Issuer = Issuer,
                Audience = authCodeData.ClientId, // Client Id here, not API
                Subject = new ClaimsIdentity(idTokenClaims),
                IssuedAt = now,
                NotBefore = now,
                Expires = idTokenExpiry,
                SigningCredentials = signingCredentials
            });

            // The provided code is a valid, unused, unexpired, PKCE verified code.  Return success.
            return Results.Ok(new
            {
                access_token = accessToken,
                id_token = idToken,
                token_type = "Bearer",
                expires_in = (int)(accessTokenExpiry - now).TotalSeconds
            });
        });
    }
}