using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

// The token endpoint is used to exhange the client's authorization code for valid tokens, ID tokens, access tokens, etc.
static class TokenEndpoint
{
    public static void Map(WebApplication app)
    {
        // Implement the token endpoint
        app.MapPost("/token", async (
            HttpContext context,
            ISigningKeyProvider keys,
            IOptions<AuthServerOptions> options ) =>
        {
            if (!context.Request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "invalid_request", error_message = "Expected form-encoded body." });
            }

            // read the content of the form we received
            var form = await context.Request.ReadFormAsync();
            
            // Extract the information we need to validate the request from the form
            var grantType = form["grant_type"].ToString();
            
            return grantType switch
            {
                "authorization_code" => HandleAuthorizationCodeGrant(form, keys, options),
                "refresh_token" => HandleRefreshTokenGrant(form, keys, options),
                _ => Results.BadRequest(new { error = "unsupported_grant_type", error_message = "only authorization_code and refresh_token are currently supported." })
            };
        });
    }

    private static IResult HandleAuthorizationCodeGrant(IFormCollection form, ISigningKeyProvider keys, IOptions<AuthServerOptions> options)
    {
        var code = form["code"].ToString();
        var redirectUri = form["redirect_uri"].ToString();
        var clientId = form["client_id"].ToString();
        var codeVerifier = form["code_verifier"].ToString();

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(codeVerifier))
        {
            return Results.BadRequest(new { error = "invalid_request", error_message = "Missing Code or Code Verifier in the request." });
        }

        // The auth code is single use, pull it out of our ConcurrentDictionary regardless of whether or not it is valid
        // Further attempts to use the code should always fail.
        if (!AuthStore.AuthCodes.TryRemove(code, out var authCodeData))
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

        var (accessToken, idToken, expiresIn) = IssueTokens(options.Value, keys, authCodeData.Subject, authCodeData.Scope, authCodeData.ClientId);

        var response = new Dictionary<string, object?>();
        if (accessToken is not null)
        {
            response["access_token"] = accessToken;
            response["token_type"] = "Bearer";
            response["expires_in"] = expiresIn;
            response["scope"] = authCodeData.Scope;
        };

        if (idToken is not null)
        {
            response["id_token"] = idToken;
        }

        // If a refresh token is requested, build it here
        if (authCodeData.Scope.Split(' ').Contains("offline_access"))
        {
            var familyId = Guid.NewGuid().ToString();
            var refreshToken = GenerateOpaqueToken();

            AuthStore.RefreshTokens[refreshToken] = new RefreshTokenData(
                ClientId: authCodeData.ClientId,
                Subject: authCodeData.Subject,
                Scope: authCodeData.Scope,
                FamilyId: familyId,
                ExpiresAt: DateTime.UtcNow.AddDays(30)
            );

            response["refresh_token"] = refreshToken;
        }

        // The provided code is a valid, unused, unexpired, PKCE verified code.  Return success.
        return Results.Ok(response);
    }

    private static IResult HandleRefreshTokenGrant(IFormCollection form, ISigningKeyProvider keys, IOptions<AuthServerOptions> options)
    {
        var refreshTokenValue = form["refresh_token"].ToString();
        var clientId = form["client_id"].ToString();
        var requestedScope = form["scope"].ToString();

        if (string.IsNullOrEmpty(refreshTokenValue))
        {
            return Results.BadRequest(new { error = "invalid_request", error_message = "refresh_token missing or empty." });
        }

        if (!AuthStore.RefreshTokens.TryGetValue(refreshTokenValue, out var refreshTokenData))
        {
            return Results.BadRequest(new { error = "invalid_grant", error_message = "Unknown or expired refresh token." });
        }

        if (refreshTokenData.Used)
        {
            // This token was already used, assume attacker stole this token and invalidate this token family
            RevokeFamily(refreshTokenData.FamilyId);
            return Results.BadRequest(new { error = "invalid_grant", error_message = "Refresh token reuse detected - session revoked." });
        }

        if (refreshTokenData.ExpiresAt < DateTime.UtcNow)
        {
            AuthStore.RefreshTokens.TryRemove(refreshTokenValue, out _);
            return Results.BadRequest(new { error = "invalid_grant", error_message = "Refresh token has expired." });
        }

        if (clientId != refreshTokenData.ClientId)
        {
            return Results.BadRequest(new { error = "invalid_grant", error_message =  "client_id mismatch." });
        }

        var usedToken = refreshTokenData with { Used = true };
        // We use TryUpdate here to ensure that our refreshTokenData still remains in the dictionary unchanged
        // If this fails, it means another request already redeemed this token (or, in rare cases, the token expired and was cleaned up automatically)
        //  and we are a second attempt to use it
        if (!AuthStore.RefreshTokens.TryUpdate(refreshTokenValue, usedToken, refreshTokenData))
        {
            RevokeFamily(refreshTokenData.FamilyId);
            return Results.BadRequest(new { error = "invalid_grant", error_message = "Refresh token reuse or expiry detected - session revoked." });
        }

        var effectiveScope = refreshTokenData.Scope;
        if (!string.IsNullOrWhiteSpace(requestedScope))
        {
            var allowed = effectiveScope.Split(' ').ToHashSet<string>();
            // If the requested scopes contain any scopes not included in the existing refresh token, deny the refresh
            if (requestedScope.Split(' ').Any(s => !allowed.Contains(s)))
            {
                return Results.BadRequest(new { error = "invalid_grant", error_message = "Requesting scope not included in the existing granted scopes."});
            }
        }

        var (accessToken, idToken, expiresIn) = IssueTokens(
            options.Value, keys, refreshTokenData.Subject, effectiveScope, refreshTokenData.ClientId);
        
        var newRefreshToken = GenerateOpaqueToken();
        AuthStore.RefreshTokens[newRefreshToken] = new RefreshTokenData(
            ClientId: refreshTokenData.ClientId,
            Subject: refreshTokenData.Subject,
            Scope: effectiveScope,
            FamilyId: refreshTokenData.FamilyId,
            ExpiresAt: refreshTokenData.ExpiresAt
        );

        var response = new Dictionary<string, object>
        {
          ["access_token"] = accessToken!,
          ["refresh_token"] = newRefreshToken,
          ["token_type"] = "Bearer",
          ["expires_in"] = expiresIn,
          ["scope"] = effectiveScope
        };

        if (idToken is not null)
        {
            response["id_token"] = idToken;
        }

        return Results.Ok(response);
    }

    private static (string? accessToken, string? idToken, int expiresIn) IssueTokens(
        AuthServerOptions config, ISigningKeyProvider keys, string subject, string scope, string clientId, string? nonce = null)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        // Note: usually this would be handled in a secure key store like Azure Key Vault, not done in the server backend where we should never store the private key
        var signingCredentials = new SigningCredentials(keys.PrivateKey, SecurityAlgorithms.RsaSha256);
        var ApiAudience = config.ApiAudience;
        var scopes = scope.Split(' ');

        var now = DateTime.UtcNow;
        var idTokenExpiry = now.AddMinutes(15); // ID token will be valid for 15 minutes

        string? accessToken = null;
        var accessTokenExpiry = now.AddMinutes(15); // Access token will be valid for 15 minutes
        if (scopes.Contains("api.read"))
        {
            // --- Access token: the protected API will verify this on every request ---
            // Contains who the user is (sub), what they are allowed to do (scope),
            //  and who issued the token / who the token is for (iss, aud)
            var accessTokenClaims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, subject),
                new Claim("scope", scope),
                new Claim("client_id", clientId)
            };

            accessToken = tokenHandler.CreateEncodedJwt(new SecurityTokenDescriptor
            {
                Issuer = config.Issuer,
                Audience = ApiAudience, // token is for the API audience
                Subject = new ClaimsIdentity(accessTokenClaims),
                IssuedAt = now,
                NotBefore = now,
                Expires = accessTokenExpiry,
                SigningCredentials = signingCredentials
            });
        }

        // --- ID token: proof of authentication, only used on the front end of the client, not passed to the API ---
        // aud here is the CLIENT aud value, not the API aud value since the client will consume this
        // If openid is not included in the scope list, do not return an id token
        string? idToken = null;
        if (scope.Split(' ').Contains("openid"))
        {
            var idTokenClaims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, subject)
            };

            if (!string.IsNullOrEmpty(nonce))
            {
                idTokenClaims.Add(new Claim(JwtRegisteredClaimNames.Nonce, nonce));
            }

            idToken = tokenHandler.CreateEncodedJwt(new SecurityTokenDescriptor
            {
                Issuer = config.Issuer,
                Audience = clientId, // Client Id here, not API
                Subject = new ClaimsIdentity(idTokenClaims),
                IssuedAt = now,
                NotBefore = now,
                Expires = idTokenExpiry,
                SigningCredentials = signingCredentials
            });
        }

        return (accessToken, idToken, (int)(accessTokenExpiry - now).TotalSeconds);
    }

    private static void RevokeFamily(string familyId)
    {
        foreach(var kvp in AuthStore.RefreshTokens)
        {
            if (kvp.Value.FamilyId == familyId)
            {
                AuthStore.RefreshTokens.TryRemove(kvp.Key, out _);
            }
        }
    }

    private static string GenerateOpaqueToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}