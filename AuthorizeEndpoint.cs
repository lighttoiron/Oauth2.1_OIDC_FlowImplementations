using System;
using System.Collections.Generic;


// Test this endpoint with the following string to pass the required info in the query params
//  https://localhost:7051/authorize?response_type=code&client_id=test-client&redirect_uri=https://localhost:3000/callback&scope=openid&state=abc123&code_challenge=abc&code_challenge_method=S256

static class AuthorizeEndpoint
{
    public static void Setup(WebApplication app, string issuer)
    {
        // Create a dictoinary of usernames and passwords for testing.  In production, you would use a proper user store with hashed passwords, like a database or an identity management system.
        var users = new Dictionary<string, string>
        {
            { "user1", "password1" },
            { "user2", "password2" }
        };

        // Holds the validated OAuth parameters between the GET (login page) and POST (credential submission) so the POST can access them
        var pendingRequests = new Dictionary<string, PendingAuthRequest>();
        //

        // Set up the GET route
        app.MapGet("/authorize", (
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
                return Results.BadRequest("Invalid or missing response_type.  Only 'code' is supported.");
            }

            if (string.IsNullOrEmpty(client_id))
            {
                return Results.BadRequest("Missing client_id.");
            }

            if (string.IsNullOrEmpty(redirect_uri))
            {
                return Results.BadRequest("Missing redirect_uri.");
            }

            if (string.IsNullOrEmpty(scope) || !scope.Split(' ').Contains("openid")) // Coudl contain other valid scope types as well, just need to ensure one scope to work with at least
            {
                return Results.BadRequest("Missing or invalid scope. 'openid' scope is required.");
            }

            if (string.IsNullOrEmpty(state))
            {
                return Results.BadRequest("Missing state.");
            }

            if (string.IsNullOrEmpty(code_challenge))
            {
                return Results.BadRequest("Missing code_challenge (PKCE).");
            }

            if (code_challenge_method != "S256")
            {
                return Results.BadRequest("Invalid or missing code_challenge_method.  Only 'S256' is supported.");
            }

            // Request is validated, store the user info
            string requestId = Guid.NewGuid().ToString();
            pendingRequests[requestId] = new PendingAuthRequest(
                ClientId: client_id,
                RedirectUri: redirect_uri,
                State: state,
                CodeChallenge: code_challenge,
                Scope: scope,
                Nonce: nonce ?? "" // Returns "" if nonce is null, since nonce is optional in the Authorization Code Flow but we still need to store its value inthe ID token
            );
            //

            // Format the HTML page response here, returning a minimal HTML login form
            // Note the hidden requestId field, this is how the POST handler will know which pending auth request to complete
            // E.g. the user doesn't need to see it, but we need the form to post the requestID back to the server so we can look up the user info there in our pending requests
            string html = $"""
                <!DOCTYPE html>
                <html>
                <head><title>Sign In</title></head>
                <body>
                    <h2>Sign In</h2>
                    <p>Client requesting access: <strong>{client_id}</strong></p>
                    <p>Scope: <strong>{scope}</strong></p>
                    <form method="post" action="/authorize">
                        <input type="hidden" name="requestId" value="{requestId}" />
                        <label>Username: <input type="text" name="username" /></label><br/>
                        <label>Password: <input type="password" name="password" /></label><br/>
                        <button type="submit">Sign In</button>
                    </form>
                </body>
            """;

            return Results.Content(html, "text/html");
        });
        //

    }

    // A simple record containing auth request info, for holding onto that info while we process
    record PendingAuthRequest(
        string ClientId,
        string RedirectUri,
        string State,
        string CodeChallenge,
        string Scope,
        string Nonce
    );
}