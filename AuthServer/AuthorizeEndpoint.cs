using System.Security.Cryptography;

// The /authorize endpoint is the initial entry point when a user is trying to log in and grant delegated authority to the client
// Also maps the /consent endpoint (which shares some logic with the /authorize endpoint) where users can grant delegated consent
static class AuthorizeEndpoint
{
    public static readonly string AuthenticatedSessionCookieName = "authenticated_session";

    // A full list of all the supported scopes we allow
    static readonly HashSet<string> _supportedScopes = [
        "openid",
        "offline_access",
        "api.read"
    ];

    // A list of scopes that are automatically granted, the user does not need to provide explicit permission for these scopes
    static readonly HashSet<string> _autoGrantedScopes =
    [
        "openid",
        "offline_access"
    ];

    // Plain text descriptions of scopes, presented to the user when they are being asked to grant consent
    private static readonly Dictionary<string, (string Name, string Description)> _scopeDescriptions = new ()
    {
        ["api.read"] = ("api.read Access", "Allow {0} to access api.read on your behalf.")
    };

    // Map all of the /authorize endpoints (e.g. MapGet, MapPost)
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

            // Could contain other valid scope types as well, just need to ensure one scope to work with at least
            if (string.IsNullOrEmpty(scope) || !ContainsAnySupportedScope(scope))
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
            if (context.Request.Cookies.TryGetValue(AuthenticatedSessionCookieName, out var sessionId))
            {
                // If the user has a session cookie, verify that we have an active session and that it hasn't expired yet
                if (AuthStore.ActiveSessions.TryGetValue(sessionId, out var foundSession)
                    && foundSession.ExpiresAt > DateTime.UtcNow)
                {
                    authSession = foundSession;
                }
                else
                {
                    // If we have an authenticated_session cookie but no active session internally, delete the cookie
                    context.Response.Cookies.Delete(AuthenticatedSessionCookieName);

                    // If the session has expired, remove it from ActiveSessions
                    if (!string.IsNullOrEmpty(sessionId))
                    {
                        AuthStore.ActiveSessions.TryRemove(sessionId, out _);
                    }
                }
            }
            
            // If the user has no active session, show the login page
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
                    Nonce: nonce ?? "", // Use "" if nonce is null, since nonce is optional in the Authorization Code Flow but we still need to store its value in the ID token
                    ExpiresAt: DateTime.UtcNow.AddMinutes(5) // Say 5 minutes is enough time for a user to log in before needing to start over
                );

                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.Pragma = "no-cache";
                return Results.Content(BuildLoginHtml(requestId, client_id, cleanScope), "text/html");
            }

            // The user has a currently active session, no need to prompt for login just allow them to grant consent
            return HandleConsent(
                context, authSession!.Subject, client_id, redirect_uri,
                cleanScope, requestedScopes, state, code_challenge, nonce ?? ""
            );
        });
        //

        // Map the POST route for handling the form submission from GET
        app.MapPost("/authorize", async (HttpContext context) =>
        {
            // If we arrived here from somewhere other than a form post, it's a bad request
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

            // If we don't have a pending request with the given request ID, delete any session cookies and return bad request
            if (!AuthStore.PendingRequests.TryGetValue(requestId, out var pending))
            {
                context.Response.Cookies.Delete(AuthenticatedSessionCookieName);

                return Results.BadRequest(new {
                    error = "invalid_grant",
                    error_message = "Unknown or expired login request - requestId not found."
                });
            }

            // If the pending session has expired, it's a bad request
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
                // If the username/password combo is incorrect, show the page again to allow sign in a second time
                // Also displays a message to the user indicating that the username/password combo was incorrect
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.Pragma = "no-cache";
                return Results.Content(BuildLoginHtml(requestId, pending.ClientId, pending.Scope, error: "Invalid username or password."), "text/html");
            }

            // Valid sign in attempt, remove the pending request from our store
            AuthStore.PendingRequests.TryRemove(requestId, out _);

            // Credentials are valid, store a session for the user so future login is not needed
            var sessionId = GenerateOpaqueToken();
            AuthStore.ActiveSessions[sessionId] = new AuthSession(
                Subject: username,
                ExpiresAt: DateTime.UtcNow.AddHours(8)
            );

            // Set the user session cookie to persist their session
            context.Response.Cookies.Append(AuthenticatedSessionCookieName, sessionId, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                MaxAge = TimeSpan.FromHours(8)
            });

            // Everything looks good, display the user consent page
            var requestedScopes = pending.Scope.Split(' ').ToHashSet();
            return HandleConsent(
                context, username, pending.ClientId, pending.RedirectUri,
                pending.Scope, requestedScopes, pending.State,
                pending.CodeChallenge, pending.Nonce
            );
        });

        // Maps the /consent endpoint where users can grant consent for any delegated permissions being requested by the client
        app.MapPost("/consent", async (HttpContext context) =>
        {
            // If this request did not come from a submitted form it is a bad request
            if (!context.Request.HasFormContentType)
            {
                return Results.BadRequest(new
                {
                    error = "invalid_request",
                    error_message = "Expected form-encoded request body."
                });
            }

            // Read the needed information from the submitted form
            var form = await context.Request.ReadFormAsync();
            var consentRequestId = form["consentRequestId"].ToString();
            var decision = form["decision"].ToString();

            // If we do not have a pending consent request for this user, it's a bad request
            if (!AuthStore.PendingConsentRequests.TryRemove(consentRequestId, out var pendingConsentRequest))
            {
                return Results.BadRequest(new
                {
                    error = "invalid_grant",
                    error_message = "Unknown or expired consent request."
                });
            }

            // If the consent request has expired, this is a bad request
            if (pendingConsentRequest.ExpiresAt < DateTime.UtcNow)
            {
                return Results.BadRequest(new
                {
                    error = "invalid_grant",
                    error_message = "Consent request expired - please try again."
                });
            }

            // If the user denied permission, redirect them back with an error message indicating that the user denied permissions
            if (decision == "deny")
            {
                var denyUrl = $"{pendingConsentRequest.RedirectUri}?"
                + $"error=access_denied"
                + $"&error_message={Uri.EscapeDataString("User denied access.")}"
                + $"&state={Uri.EscapeDataString(pendingConsentRequest.State)}";

                return Results.Redirect(denyUrl);
            }

            // We know the user has consented to the scopes shown on the consent page
            // Get the list of scopes the user explicitly consented to
            HashSet<string> userConsentedScopes = pendingConsentRequest.NeedsConsent;
            userConsentedScopes.IntersectWith(_supportedScopes);

            // Get the full list of scopes that were requested that have been consented to explicitly or are implicitly allowed
            var allGrantedScopes = userConsentedScopes;
            allGrantedScopes.UnionWith(pendingConsentRequest.RequestedScopes.Intersect(_autoGrantedScopes));

            // Store the consented to permissions in our store with the format subject:clientId (which permissions has the user granted for which client)
            var consentKey = $"{pendingConsentRequest.Subject}:{pendingConsentRequest.ClientId}";
            AuthStore.ConsentRecords.AddOrUpdate(
                consentKey,
                new ConsentRecord(
                    pendingConsentRequest.Subject,
                    pendingConsentRequest.ClientId,
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
    
            // Make sure to issue the auth code for ALL granted scopes, both implicitly and explicitly granted
            var grantedScope = string.Join(' ', allGrantedScopes);
            // Issue the authorization code that /token can consume to provide access tokens
            return IssueCode(
                pendingConsentRequest.ClientId, pendingConsentRequest.RedirectUri, grantedScope,
                pendingConsentRequest.State, pendingConsentRequest.CodeChallenge,
                pendingConsentRequest.Nonce, pendingConsentRequest.Subject
            );
        });
    }

    // Returns the consent page the user needs to grant delegated permissions
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

        // Get a list of all scopes needing user consent
        var scopesNeedingConsent = requestedScopes // All requested scopes
            .Except(_autoGrantedScopes) // Except those that are automatically included for every call
            .Where( s =>
                !AuthStore.ConsentRecords.TryGetValue(consentKey, out var consentRecord) // If the user has not granted any scopes for this clientId
                || !consentRecord.GrantedScopes.Contains(s)) // Or if a particular requested scope has not yet been granted
            .ToHashSet();

        // If no scopes need consent, issue the auth code for the requested scopes
        if (!scopesNeedingConsent.Any())
        {
            return IssueCode(clientId, redirectUri, scope, state, codeChallenge, nonce, subject);
        }

        // Add a pending consent request so we know which client scopes are being requested for the user when they grant or deny permission
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
        // This code is stored here in the server and is associated with the requested permissions
        // When this code is presented to the /token endpoint, it will distribute the actual tokens requested
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

    // Generates a random opaque token, used to remember users in between calls from /authorize to /token
    private static string GenerateOpaqueToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    // True if the string contains at least one supported scope
    // In this lab we choose to ignore any extraneous scopes requested
    private static bool ContainsAnySupportedScope(string scope)
    {
        string[] scopes = scope.Split(' ');
        return scopes.Intersect(_supportedScopes).Any();
    }

    // Builds a simple login page that allows the user to sign in
    private static string BuildLoginHtml(string requestId, string clientId, string scope, string? error = null)
    {
        // The error message to be shown if the user attempts to sign in with an invalid username/password
        var errorHTML = error is not null
            ? $"<p class=\"error\">{error}</p>"
            : "";
    
        // Format the HTML page response here, returning a minimal HTML login form
        // Note the hidden requestId field, this is how the POST handler will know which pending auth request to complete
        // The user doesn't need to see it but we need the form to post the requestID back to the server so we can look up the user info in our pending request store
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
                {{errorHTML}}
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

    // Builds the consent page that allows users to grant delegated permission to the client
    private static string BuildConsentHtml(
        string consentRequestId,
        string clientId,
        string subject,
        HashSet<string> scopesNeedingConsent)
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
                    <button type="submit" name="decision" value="deny">Deny</button>
                </form>
            </body>
            </html>
        """;
    }
}