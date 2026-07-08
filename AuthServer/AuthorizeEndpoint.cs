using System;
using System.Collections.Generic;
using System.Collections.Concurrent; // Thread-safe collections
using System.Linq;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Routing.Tree;
using Microsoft.IdentityModel.Tokens;


// Test this endpoint with the following string to pass the required info in the query params

static class AuthorizeEndpoint
{
    static readonly HashSet<string> _supportedScopes = [
        "openid",
        "offline_access",
        "api.read"
    ];

    static readonly HashSet<string> _autoGrantedScopes =
    [
        "openid",
        "offline_access"
    ];

    private static readonly Dictionary<string, (string Name, string Description)> _scopeDescriptions = new ()
    {
        ["api.read"] = ("api.read Access", "Allow {0} to access api.read on your behalf.")
    };

    public static void Map(WebApplication app)
    {
        // Set up the GET route
        app.MapGet("/authorize", (HttpContext context,
           string? response_type,
           string? client_id,
           string? redirect_uri,
           string? scope,
           string? state,
           string? code_challenge,
           string? code_challenge_method,
           string? nonce ) =>
        {
            // VALIDATE the required parameters
            // In a production server, you must also validate client_id against a registered client store, 
            //  and validate that the redirect_uri matches what is registered for that client_id to prevent open redirector attacks.
            //  You should also validate that the requested scopes are valid and allowed for that client.
            // For our app, we are accepting any client_id BUT we could change this for testing to reject any bad client ids

            if (response_type != "code")
            {
                return Results.BadRequest(new { error = "invalid_request", error_message = "Invalid or missing response_type.  Only 'code' is supported." });
            }

            if (string.IsNullOrEmpty(client_id))
            {
                return Results.BadRequest(new { error = "invalid_request", error_message = "Missing client_id." });
            }

            if (string.IsNullOrEmpty(redirect_uri))
            {
                return Results.BadRequest(new { error = "invalid_request", error_message = "Missing redirect_uri." });
            }

            if (string.IsNullOrEmpty(scope) || !ContainsAnySupportedScope(scope)) // Could contain other valid scope types as well, just need to ensure one scope to work with at least
            {
                return Results.BadRequest(new { error = "invalid_request", error_message = $"Missing or invalid scope. Must include at least one of: {string.Join(' ', _supportedScopes)}" });
            }

            if (string.IsNullOrEmpty(state))
            {
                return Results.BadRequest(new { error = "invalid_request", error_message = "Missing state."});
            }

            if (string.IsNullOrEmpty(code_challenge))
            {
                return Results.BadRequest(new { error = "invalid_request", error_message = "Missing code_challenge (PKCE)." });
            }

            if (code_challenge_method != "S256")
            {
                return Results.BadRequest(new { error = "invalid_request", error_message = "Invalid or missing code_challenge_method.  Only 'S256' is supported." });
            }

            // Sanitize the requested scopes, including only supported scopes regardless of what is sent
            var requestedScopes = scope.Split(' ')
                .Where(s => _supportedScopes.Contains(s))
                .ToHashSet();
            var cleanScope = string.Join(' ', requestedScopes);

            // Check if the user has an active session with this authentication server
            AuthSession? authSession = null;
            if (
                context.Request.Cookies.TryGetValue("authenticated_session", out var sessionId)
                && AuthStore.ActiveSessions.TryGetValue(sessionId, out var foundSession)
                && foundSession.ExpiresAt > DateTime.UtcNow)
            {
                authSession = foundSession;
            }
            
            if (authSession is null)
            {
                // Request is validated but login is required, store the user info as a pending request
                string requestId = Guid.NewGuid().ToString();
                AuthStore.PendingRequests[requestId] = new PendingAuthRequest(
                    ClientId: client_id,
                    RedirectUri: redirect_uri,
                    State: state,
                    CodeChallenge: code_challenge,
                    Scope: cleanScope,
                    Nonce: nonce ?? "", // Returns "" if nonce is null, since nonce is optional in the Authorization Code Flow but we still need to store its value in the ID token
                    ExpiresAt: DateTime.UtcNow.AddMinutes(5) // Say 5 minutes is enough time for a user to log in before needing to start over
                );

                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.Pragma = "no-cache";
                return Results.Content(BuildLoginHtml(requestId, client_id, cleanScope), "text/html");
            }

            // The user has a currently active session, no need to prompt for login
            return HandleConsent(
                context, authSession!.Subject, client_id, redirect_uri,
                cleanScope, requestedScopes, state, code_challenge, nonce ?? ""
            );
        });
        //

        // Map the POST route for handling the form submission from GET
        app.MapPost("/authorize", async (HttpContext context) =>
        {
            // 
            if (!context.Request.HasFormContentType)
            {
                return Results.BadRequest(new
                {
                    error = "invlid_request",
                    error_message = "Expected form-encoded request body."
                });
            }

            // Read the incomming request if it is a form, and parse it into an IFormCollection
            // Reading a form is async because, theoretically, request bodies can be huge and they arrive as a stream.
            //  We can't know the contents of the whole body until the entire stream is delivered, so we use ReadFormAsync to wait for it to arrive then parse it.
            var form = await context.Request.ReadFormAsync();

            // Get the information we need from the form
            var requestId = form["requestId"].ToString();
            var username = form["username"].ToString();
            var password = form["password"].ToString();

            // Verify that we have a pending request for the given requestId
            if (!AuthStore.PendingRequests.TryRemove(requestId, out var pending))
            {
                return Results.BadRequest(new {
                    error = "invalid_grant",
                    error_message = "Unknown or expired login request - requestId not found."
                });
            }

            if (pending.ExpiresAt < DateTime.UtcNow)
            {
                return Results.BadRequest(new
                {
                    error = "invalid_request",
                    error_message = "Login request expired - please try again."
                });
            }

            // Verify that the username matches a stored password for that username
            if (!AuthStore.Users.TryGetValue(username, out var expectedPassword) || password != expectedPassword)
            {
                return Results.Content(BuildLoginHtml(requestId, pending.ClientId, pending.Scope, error: "Invalid username or password."), "text/html");
            }

            // Credentials are valid, store a session for the user so future login is not needed
            var sessionId = GenerateOpaqueToken();
            AuthStore.ActiveSessions[sessionId] = new AuthSession(
                Subject: username,
                ExpiresAt: DateTime.UtcNow.AddHours(8)
            );

            context.Response.Cookies.Append("authenticated_session", sessionId, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                MaxAge = TimeSpan.FromHours(8)
            });

            var requestedScopes = pending.Scope.Split(' ').ToHashSet();
            return HandleConsent(
                context, username, pending.ClientId, pending.RedirectUri,
                pending.Scope, requestedScopes, pending.State,
                pending.CodeChallenge, pending.Nonce
            );
        });

        app.MapPost("/consent", async (HttpContext context) =>
        {
            if (!context.Request.HasFormContentType)
            {
                return Results.BadRequest(new
                {
                    error = "invalid_request",
                    error_message = "Expected form-encoded request body."
                });
            }

            var form = await context.Request.ReadFormAsync();
            var consentRequestId = form["consentRequestId"].ToString();
            var decision = form["decision"].ToString();

            if (!AuthStore.PendingConsentRequests.TryRemove(consentRequestId, out var pendingConsentRequest))
            {
                return Results.BadRequest(new
                {
                    error = "invalid_grant",
                    error_message = "Unknown or expired consent request."
                });
            }

            if (pendingConsentRequest.ExpiresAt < DateTime.UtcNow)
            {
                return Results.BadRequest(new
                {
                    error = "invalid_grant",
                    error_message = "Consent request expired - please try again."
                });
            }

            if (decision == "deny")
            {
                var denyUrl = $"{pendingConsentRequest.RedirectUri}?"
                + $"error=access_denied"
                + $"&error_message={Uri.EscapeDataString("User denied access.")}"
                + $"$state={Uri.EscapeDataString(pendingConsentRequest.State)}";

                return Results.Redirect(denyUrl);
            }

            // We know the user has consented to the scopes shown on the consent page
            // Get the list of scopes the user explicitly consented to
            HashSet<string> userConsentedScopes = pendingConsentRequest.NeedsConsent;
            userConsentedScopes.IntersectWith(_supportedScopes);

            // Get the full list of scopes that were requested that have been consented to explicitly or are implicitly allowed
            var allGrantedScopes = userConsentedScopes;
            allGrantedScopes.UnionWith(pendingConsentRequest.RequestedScopes.Intersect(_autoGrantedScopes));

            var consentKey = $"{pendingConsentRequest.Subject}:{pendingConsentRequest.ClientId}";
            AuthStore.ConsentRecords.AddOrUpdate(
                consentKey,
                new ConsentRecord(
                    pendingConsentRequest.Subject, pendingConsentRequest.ClientId,
                    allGrantedScopes,
                    DateTime.UtcNow),
                    (_, existing) => existing with
                    {
                        GrantedScopes = existing.GrantedScopes
                            .Union(allGrantedScopes)
                            .ToHashSet(),
                        ConsentedAt = DateTime.UtcNow
                    }
            );
    
            
            var grantedScope = string.Join(' ', allGrantedScopes); // Make sure to issue the auth code for ALL granted scopes, both implicitly and explicitly granted
            return IssueCode(
                pendingConsentRequest.ClientId, pendingConsentRequest.RedirectUri, grantedScope,
                pendingConsentRequest.State, pendingConsentRequest.CodeChallenge,
                pendingConsentRequest.Nonce, pendingConsentRequest.Subject
            );
        });
    }

    private static IResult HandleConsent(
        HttpContext context,
        string subject,
        string clientId,
        string redirectUri,
        string scope,
        HashSet<string> requestedScopes,
        string state,
        string codeChallenge,
        string nonce
    )
    {
        var consentKey = $"{subject}:{clientId}";

        var scopesNeedingConsent = requestedScopes // All requested scopes
            .Except(_autoGrantedScopes) // Except those that are automatically included for every call
            .Where( s =>
                !AuthStore.ConsentRecords.TryGetValue(consentKey, out var consentRecord) // If the user has not granted any scopes for this clientId
                || !consentRecord.GrantedScopes.Contains(s)) // Or if a particular requested scope has not yet been granted
            .ToHashSet();

        if (!scopesNeedingConsent.Any())
        {
            return IssueCode(clientId, redirectUri, scope, state, codeChallenge, nonce, subject);
        }

        var consentRequestId = Guid.NewGuid().ToString();
        AuthStore.PendingConsentRequests[consentRequestId] = new PendingConsentRequest(
            Subject: subject,
            ClientId: clientId,
            RedirectUri: redirectUri,
            RequestedScopes: requestedScopes,
            NeedsConsent: scopesNeedingConsent,
            AutoGranted: _autoGrantedScopes,
            State: state,
            CodeChallenge: codeChallenge,
            Nonce: nonce,
            ExpiresAt: DateTime.UtcNow.AddMinutes(5)
        );

        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";

        return Results.Content(BuildConsentHtml(consentRequestId, clientId, subject, scopesNeedingConsent), "text/html");
    }

    // Generates, stores, and issues the Authorization Code
    // Redirects back to the client after the code has been generated.
    private static IResult IssueCode(
        string clientId,
        string redirectUri,
        string scope,
        string state,
        string codeChallenge,
        string nonce,
        string subject
    )
    {
        var code = GenerateOpaqueToken();
        AuthStore.AuthCodes[code] = new AuthorizationCodeData(
            ClientId: clientId,
            RedirectUri: redirectUri,
            CodeChallenge: codeChallenge,
            Scope: scope,
            Nonce: nonce,
            Subject: subject,
            ExpiresAt: DateTime.UtcNow.AddSeconds(60)
        );

        var redirectUrl = $"{redirectUri}?"
            + $"code={Uri.EscapeDataString(code)}"
            + $"&state={Uri.EscapeDataString(state)}";

        return Results.Redirect(redirectUrl);
    }

    private static string GenerateOpaqueToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static bool ContainsAnySupportedScope(string scope)
    {
        string[] scopes = scope.Split(' ');
        return scopes.Intersect(_supportedScopes).Any();
    }

    private static string BuildLoginHtml(string requestId, string clientId, string scope, string? error = null)
    {
        var errorHTML = error is not null
            ? $"<p class=\"error\">{error}</p>"
            : "";
    
            // Format the HTML page response here, returning a minimal HTML login form
            // Note the hidden requestId field, this is how the POST handler will know which pending auth request to complete
            // E.g. the user doesn't need to see it, but we need the form to post the requestID back to the server so we can look up the user info there in our pending requests
            // The inserted Javascript in the header will force a page reload if the user gets a cached version of the page by clicking the back button
            // This way if they ever access a cached version of the page via back (which will have an old requestId baked in) we manually refresh
            string html = $$"""
                <!DOCTYPE html>
                <html lang="en">
                <head>
                    <title>Sign In</title>
                    <script>
                        window.addEventListener('pageshow', (event) => {
                            if (event.persisted) {
                                location.reload();
                            }
                        })
                    </script>
                </head>
                <body>
                    <h2>Sign In</h2>
                    <p>Client requesting access: <strong>{{clientId}}</strong></p>
                    <p>Scope: <strong>{{scope}}</strong></p>
                    <form method="post" action="/authorize">
                        <input type="hidden" name="requestId" value="{{requestId}}" />
                        <label>Username: <input type="text" name="username" value="user1"></input></label><br/>
                        <label>Password: <input type="password" name="password" value="pass1"></input></label><br/>
                        <button type="submit">Sign In</button>
                    </form>
                </body>
                </html>
            """;

            return html;
    }

    private static string BuildConsentHtml(
        string consentRequestId,
        string clientId,
        string subject,
        HashSet<string> scopesNeedingConsent
    )
    {
        var scopeItems = scopesNeedingConsent
            .Select(s =>
            {
                var (name, descTemplate) = _scopeDescriptions.TryGetValue(s, out var d)
                     ? d
                     : (s, $"Grant access to {s}");

                var description = string.Format(descTemplate, clientId);

                return $"<li><strong>{name}</strong> - {description}</li>";
            });

        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <title>Authorize Access</title>
                <script>
                    window.addEventListener('pageshow', (event) => {
                        if (event.persisted) location.reload();
                    });
                </script>
            </head>
            <body>
                <h2>AuthorizeAccess</h2>
                <p>Signed in as <strong>{{subject}}</strong></p>
                <p><strong>{{clientId}}</strong> is requesting the following permissions:</p>
                <form method="post" action="/consent">
                    <input type="hidden" name="consentRequestId" value="{{consentRequestId}}" />
                    <ul>
                        {{string.Join('\n', scopeItems)}}
                    </ul>
                    <button type="submit" name="decision" value="approve">Approve</button>
                    <button type="submit" name="decision" valiue="deny">Deny</button>
                </form>
            </body>
            </html>
        """;
    }
}