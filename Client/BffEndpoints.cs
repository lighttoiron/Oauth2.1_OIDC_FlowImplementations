using System.Buffers.Text;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Routing.Tree;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

static class BffEndpoints
{
    private const string LoginAttemptCookieName = "bff_login_attempt";
    private const string SessionCookieName = "bff_session";

    public static void Map(WebApplication app)
    {
        // The initial sign in call endpoint
        // Generates PKCE values, stores a login attempt with the code verifier, and initiates the call to /authorize
        app.MapGet("/bff/login", (HttpContext context, IOptions<BffOptions> options, bool popup = false, string mode = "") =>
        {
            var config = options.Value;

            var scope = mode switch
            {
                "identity" => "openid",
                "full" => "openid offline_access api.read",
                _ => "openid"
            };

            var codeVerifierBytes = RandomNumberGenerator.GetBytes(32);
            var codeVerifier = Base64Url(codeVerifierBytes); 
            var codeChallenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
            var state = Base64Url(RandomNumberGenerator.GetBytes(16));

            var attemptId = Base64Url(RandomNumberGenerator.GetBytes(16));
            BffStore.LoginAttempts[attemptId] = new LoginAttempt(
                codeVerifier,
                state,
                scope,
                popup,
                DateTime.UtcNow.AddMinutes(5)
            );

            context.Response.Cookies.Append(LoginAttemptCookieName, attemptId, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                MaxAge = TimeSpan.FromMinutes(5)
            });

            var query = new Dictionary<string, string?>
            {
                ["response_type"] = "code",
                ["client_id"] = config.ClientId,
                ["redirect_uri"] = config.CallbackRedirectUri,
                ["scope"] = scope,
                ["state"] = state,
                ["code_challenge"] = codeChallenge,
                ["code_challenge_method"] = "S256"
            };

            return Results.Redirect(QueryHelpers.AddQueryString($"{config.AuthServerUrl}/authorize", query));
        });

        // After receiving the authorization code, call the /token endpoint to exchange for the requested tokens
        app.MapGet("/bff/callback", async(
            HttpContext context,
            string? code,
            string? state,
            string? error,
            IOptions<BffOptions> options,
            IHttpClientFactory httpFactory) =>
        {
            
            var config = options.Value;

            if
            (
                !context.Request.Cookies.TryGetValue(LoginAttemptCookieName, out var attemptId) ||
                !BffStore.LoginAttempts.TryRemove(attemptId, out var loginAttempt)
            )
            {
                return Results.BadRequest("Unknown or expired login attempt.");
            }

            context.Response.Cookies.Delete(LoginAttemptCookieName);

            // Redirect the page as appropriate based on whether or not this login attempt is via a popup window
            IResult Finish(bool success, string? errorMessage = null)
            {
                if (!loginAttempt.IsPopup)
                {
                    return success ? Results.Redirect("/") : Results.BadRequest(errorMessage);
                }

                var queryParams = success ? "" : $"?error={Uri.EscapeDataString(errorMessage ?? "login_failed")}";
                return Results.Redirect($"/popup-complete.html{queryParams}");
            }
            //

            // If we received an error message from the authorization server, we can return that message here
            if (error is not null) return Finish(false, error);

            if (state != loginAttempt.State)
            {
                return Results.BadRequest("State mismatch - possible CSRF");
            }

            var http = httpFactory.CreateClient();
            var tokenResponse = await http.PostAsync(
                $"{config.AuthServerUrl}/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["grant_type"] = "authorization_code",
                        ["code"] = code!,
                        ["redirect_uri"] = config.CallbackRedirectUri,
                        ["client_id"] = config.ClientId,
                        ["code_verifier"] = loginAttempt.CodeVerifier
                    }
            ));

            if (!tokenResponse.IsSuccessStatusCode)
            {
                return Results.BadRequest($"Token exchange failed: {await tokenResponse.Content.ReadAsStringAsync()}");
            }

            var tokens = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
            bool hasIdToken = tokens.TryGetProperty("id_token", out var idTokenJson);
            var idToken = hasIdToken ? idTokenJson.GetString() : null;
            bool hasAccessToken = tokens.TryGetProperty("access_token", out var accessTokenJson);
            var accessToken = hasAccessToken ? accessTokenJson.GetString() : null;
            bool hasExpiresIn = tokens.TryGetProperty("expires_in", out var expiresInJson);
            int? expiresIn = hasExpiresIn ? expiresInJson.GetInt32() : null;
            DateTime? accessTokenExpiresAt = expiresIn is not null ? DateTime.UtcNow.AddSeconds((double)expiresIn) : null;
            bool hasScope = tokens.TryGetProperty("scope", out var scopeJson);
            var scope = hasScope ? scopeJson.GetString()! : loginAttempt.Scope; // If no scope is returned, assume we have the initial scopes we requested
            var subject = idToken is not null
                ? DecodeSubjectFromJwt(idToken)
                : accessToken is not null
                    ? DecodeSubjectFromJwt(accessToken)
                    : null;

            if (subject is null)
            {
                return Results.BadRequest("Token response contained no identifiable subject.");
            }

            bool hasRefreshToken = tokens.TryGetProperty("refresh_token", out var refreshTokenJson);
            var refreshToken = hasRefreshToken ? refreshTokenJson.GetString() : null;
            // Note: this is an arbitrary guess and not tied to the actual expiriry of the refresh tokens or anything else, this could get out of sync if our auth server changes policy.
            // We could have the server return us a custom property to give us the proper value, but it's not part of the OAuth2.0 spec
            var sessionExpiresAt = DateTime.UtcNow.AddDays(30);

            var sessionId = Base64Url(RandomNumberGenerator.GetBytes(32));
            var bffSession = new BffSession(
                Subject: subject,
                Scope: scope,
                AccessToken: accessToken,
                RefreshToken: refreshToken,
                AccessTokenExpiresAt: accessTokenExpiresAt,
                ExpiresAt: sessionExpiresAt
            );

            BffStore.Sessions[sessionId] = bffSession;

            context.Response.Cookies.Append(SessionCookieName, sessionId, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                MaxAge = TimeSpan.FromDays(30)
            });
        
            return Finish(true); // Redirect back to the base page from this server
        });

        // Informs the client page if the user is currently signed in
        app.MapGet("/bff/me", (HttpContext context) =>
        {
            var session = GetSession(context);

            if (session is null)
            {
                return Results.Ok(new { authenticated = false });
            }

            var scopes = session.Scope.Split(' ').ToHashSet();
            return Results.Ok(
                new {
                    authenticated = true,
                    subject = session.Subject,
                    scope = session.Scope,
                    hasApiAccess = scopes.Contains("api.read"),
                    hasRefreshToken = session.RefreshToken is not null
                });
        });

        app.MapGet("/bff/protected", async(
            HttpContext context,
            IOptions<BffOptions> options,
            IHttpClientFactory httpFactory) =>
        {
            var session = GetSession(context);
            if (session is null)
            {
                return Results.Unauthorized();
            }

            if (session.AccessToken is null || session.AccessTokenExpiresAt is null)
            {
                return Results.Json(
                    new
                    {
                        error = "insifficient_scope",
                        error_message = "No valid API access code was associated with this session.  Access denied."
                    },
                    statusCode: 403);
            }

            if (session.AccessTokenExpiresAt < DateTime.UtcNow.AddSeconds(10))
            {
                var refreshedSession = await RefreshSession(session, options.Value, httpFactory);
                if (refreshedSession is null)
                {
                    context.Response.Cookies.Delete(SessionCookieName);
                    return Results.Unauthorized();
                }

                session = refreshedSession;
                BffStore.Sessions[context.Request.Cookies[SessionCookieName]!] = session;
            }

            var http = httpFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Get, $"{options.Value.ResourceApiUrl}/protected");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.AccessToken);

            var apiResponse = await http.SendAsync(request);
            return Results.Content(
                await apiResponse.Content.ReadAsStringAsync(),
                "application/json",
                statusCode: (int)apiResponse.StatusCode
            );
        });

        app.MapGet("/bff/dumpeverything", async (
            HttpContext context,
            IOptions<BffOptions> options,
            IHttpClientFactory httpFactory) =>
        {
            var http = httpFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Get, $"{options.Value.AuthServerUrl}/dumpeverything");

            var response = await http.SendAsync(request);
            return Results.Content(
                await response.Content.ReadAsStringAsync(),
                "application/json",
                statusCode: (int)response.StatusCode
            );
        });
    }

    private static BffSession? GetSession(HttpContext context)
    {
        var sessionCookie = context.Request.Cookies.TryGetValue(SessionCookieName, out var sessionId);
        if (sessionId == null)
        {
            return null;
        }

        var sessionFound = BffStore.Sessions.TryGetValue(sessionId, out var session);

        return  sessionFound ? session : null;
    }

    private static async Task<BffSession?> RefreshSession(
        BffSession session,
        BffOptions config,
        IHttpClientFactory httpFactory)
    {
        if (session.RefreshToken is null)
        {
            return null;
        }

        var http = httpFactory.CreateClient();
        var response = await http.PostAsync(
            $"{config.AuthServerUrl}/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = session.RefreshToken,
                ["client_id"] = config.ClientId
            }
        ));

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var tokens = await response.Content.ReadFromJsonAsync<JsonElement>();

        bool hasScope = tokens.TryGetProperty("scope", out var scopeJson);
        var scope = hasScope ? scopeJson.GetString()! : session.Scope; // Fall back to originally requested scope if no scope is explicitly provided

        return session with
        {
            Scope = scope,
            AccessToken = tokens.GetProperty("access_token").GetString()!,
            RefreshToken = tokens.GetProperty("refresh_token").GetString()!,
            AccessTokenExpiresAt = DateTime.UtcNow.AddSeconds(tokens.GetProperty("expires_in").GetInt32()),
        };
    }

    // This decoding logic can also be handled by the System.IdentityModel.Tokens.Jwt library
    private static string DecodeSubjectFromJwt(string jwt)
    {
        var payload = jwt.Split('.')[1];
        var s = payload.Replace('-', '+').Replace('_', '/');
        s += new string('=', (4 - s.Length % 4) % 4); // TODO: the second %4 is redundant, no?
        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(s)));
        return doc.RootElement.GetProperty("sub").GetString()!; // We can guarantee that our jwt contains the sub property here, warning can be ignored.

    }

    private static string Base64Url(byte[] bytes)
    {
        string urlEncodedBytes = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return urlEncodedBytes;
    }
}