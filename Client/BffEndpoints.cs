using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Routing.Tree;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

// The BFF Endpoints and their mappings, /bff/login, /bff/login, /bff/callback, /bff/me, /bff/protected, /bff/dumpeverything, /bff/cleareverything
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
            // Get the config and scopes for the login request
            var config = options.Value;

            var scope = mode switch
            {
                "identity" => "openid",
                "full" => "openid offline_access api.read",
                _ => "openid"
            };

            // Set up the PKCE code verifier bytes and hash
            var codeVerifierBytes = RandomNumberGenerator.GetBytes(32);
            var codeVerifier = Base64Url(codeVerifierBytes); 
            var codeChallenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
            var state = Base64Url(RandomNumberGenerator.GetBytes(16));

            // Store the login attempt so that we know which scopes have been requested and granted after login is complete
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

            // Make the request to the auth server for an Authorization Code
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

            // Verify that we have an active login attempt session
            if
            (
                !context.Request.Cookies.TryGetValue(LoginAttemptCookieName, out var attemptId)
                || !BffStore.LoginAttempts.TryRemove(attemptId, out var loginAttempt)
            )
            {
                return Results.BadRequest("Unknown or expired login attempt.");
            }

            // Remove the login attempt cookie since we no longer need to track this attempt
            context.Response.Cookies.Delete(LoginAttemptCookieName);

            // Redirect the page as appropriate based on whether or not this login attempt is via a popup window
            IResult Finish(bool success, string? errorMessage = null)
            {
                // If we are not a popup page, redirect back to the base page
                if (!loginAttempt.IsPopup)
                {
                    return success ? Results.Redirect("/") : Results.Redirect($"/?error={Uri.EscapeDataString(errorMessage ?? "login_failed")}");
                }

                // If we are a popup page, redirect to the popup-complete page
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

            // Post the authorization code to the /token endpoint of the auth server, sending the code verifier
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

            // Extract the tokens and other information from the authorization server response
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

            // Create a session ID for the user, we can pass this to the page and store any sensitive information here in the server
            var sessionId = Base64Url(RandomNumberGenerator.GetBytes(32));
            var bffSession = new BffSession(
                Subject: subject,
                Scope: scope,
                AccessToken: accessToken,
                RefreshToken: refreshToken,
                IdToken: idToken,
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
        
            return Finish(true); // Redirect back to the base page
        });

        // Informs the client page if the user is currently signed in
        app.MapGet("/bff/me", (HttpContext context) =>
        {
            // Get the user session from the session store in this server
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

        // Makes a call to our protected API server
        app.MapGet("/bff/protected", async(
            HttpContext context,
            IOptions<BffOptions> options,
            IHttpClientFactory httpFactory) =>
        {
            // Get the user session to ensure they are signed in before making the call
            var session = GetSession(context);
            if (session is null)
            {
                return Results.Unauthorized();
            }

            if (session.AccessToken is null || session.AccessTokenExpiresAt is null)
            {
                
                context.Response.Cookies.Delete(SessionCookieName);
                return Results.Unauthorized();
            }

            if (session.AccessTokenExpiresAt < DateTime.UtcNow.AddSeconds(10))
            {
                // Attempt to refreshthe session using a refresh token
                var refreshedSession = await RefreshSession(session, options.Value, httpFactory);
                // If we could not refresh, request is unauthorized
                if (refreshedSession is null)
                {
                    context.Response.Cookies.Delete(SessionCookieName);
                    return Results.Unauthorized();
                }

                // If we have refreshed the session, replace our old sesison with the new one
                session = refreshedSession;
                BffStore.Sessions[context.Request.Cookies[SessionCookieName]!] = session;
            }

            // Make a request to the protected ResourceApi endpoint
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

        // A lab-specific endpoint that dumps everything we have from this server and the auth server to the page for easy debugging
        app.MapGet("/bff/dumpeverything", async (
            HttpContext context,
            IOptions<BffOptions> options,
            IHttpClientFactory httpFactory) =>
        {
            // Fetch the data from the auth server
            var http = httpFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Get, $"{options.Value.AuthServerUrl}/dumpeverything");
            var response = await http.SendAsync(request);

            // Deserialize the data from the auth server, placing it in a dictionary
            var authServerData = await response.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>()
                ?? new Dictionary<string, JsonElement>();

            // Add the data from this server to the response
            var allServerData = new
            {
                authServer = authServerData,

                bffServer = new
                {
                    sessions = BffStore.Sessions.Select(kvp => new
                    {
                        sessionId = kvp.Key[..Math.Min(8, kvp.Key.Length)] + "...", // Return only up to the first 8 bytes for readability
                        subject = kvp.Value.Subject,
                        scope = kvp.Value.Scope,
                        hasAccessToken = kvp.Value.AccessToken is not null,
                        hasRefreshToken = kvp.Value.RefreshToken is not null,
                        hasIdToken = kvp.Value.IdToken is not null,
                        accessTokenExpiry = kvp.Value.AccessTokenExpiresAt,
                        sessionExpiry = kvp.Value.ExpiresAt
                    }),

                    loginAttempts = BffStore.LoginAttempts.Select(kvp => new
                    {
                        attemptId = kvp.Key[..Math.Min(8, kvp.Key.Length)] + "...", // Return only up to the first 8 bytes for readability
                        isPopup = kvp.Value.IsPopup,
                        scope = kvp.Value.Scope,
                        expiresAt = kvp.Value.ExpiresAt
                    })
                }
            };

            return Results.Json(allServerData, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        });

        // A lab-specific endpoint that clears everything stored in this server and in the auth server
        app.MapGet("/bff/cleareverything", async (
            HttpContext context,
            IOptions<BffOptions> options,
            IHttpClientFactory httpFactory) =>
        {
            // Clear all BFF internal storage
            BffStore.LoginAttempts.Clear();
            BffStore.Sessions.Clear();

            // Clear all the Auth Server internal storage and cookies
            var http = httpFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Get, $"{options.Value.AuthServerUrl}/cleareverything");
            var response = await http.SendAsync(request);

            // Clear all the session cookies on the front end
            context.Response.Cookies.Delete(SessionCookieName);
            context.Response.Cookies.Delete(LoginAttemptCookieName);

            return Results.Ok();
        });

        // Expires the current user session to allow testing the refresh token grant
        app.MapGet("/bff/expiresession", (HttpContext context) =>
        {
            // If no session cookie exists, it's a bad request that we cannot process
            if (!context.Request.Cookies.TryGetValue(SessionCookieName, out var sessionId))
            {
                return Results.BadRequest(new
                {
                    error = "no_session_cookie",
                    error_message = "No session cookie could be found, there is nothing to expire"

                });
            }

            // If we have no current active user session, there is nothing to do
            if (!BffStore.Sessions.TryGetValue(sessionId, out var session))
            {
                return Results.BadRequest(new
                {
                    error = "no_active_session",
                    error_message = "Session cookie present but no active session for the sessionId could be found."
                });
            }

            // Expire the current session
            if (!BffStore.Sessions.TryUpdate(
                sessionId,
                session with
                {
                    AccessTokenExpiresAt = DateTime.UtcNow.AddSeconds(-1)
                },
                session))
            {
                return Results.BadRequest(new
                {
                    error = "unable_to_update",
                    error_message = "Unable to update the session expiry in the BffStore.  Please try again."
                });
            }

            return Results.Ok(new
            {
                message = "Access token expired successfully.",
                subject = session.Subject,
                hasRefreshToken = session.RefreshToken is not null
            });
        });
    }

    // Gets the current user session by reading the sessionId from the session cookie, if present
    private static BffSession? GetSession(HttpContext context)
    {
        context.Request.Cookies.TryGetValue(SessionCookieName, out var sessionId);
        if (sessionId == null)
        {
            return null;
        }

        var sessionFound = BffStore.Sessions.TryGetValue(sessionId, out var session);

        return  sessionFound ? session : null;
    }

    // Attempts to refresh the current user session by exchanging their refresh token for a new token set
    private static async Task<BffSession?> RefreshSession(
        BffSession session,
        BffOptions config,
        IHttpClientFactory httpFactory)
    {
        if (session.RefreshToken is null)
        {
            return null;
        }

        // Make a call to the /token endpoint with grant_type=refresh_token to try and refresh our tokens
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

        // Get the returned tokens from the endpoint
        var tokens = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Grab the scope of the returned tokens in case they have been updated
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
        s += new string('=', (4 - s.Length % 4) % 4); // The second %4 is needed in case the length is a multiple of 4 already and the () calculation returns 4
        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(s)));
        return doc.RootElement.GetProperty("sub").GetString()!; // We can guarantee that our jwt contains the sub property here, warning can be ignored.

    }

    // Converts the given bytes to a base 64 url string
    private static string Base64Url(byte[] bytes)
    {
        string urlEncodedBytes = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return urlEncodedBytes;
    }
}