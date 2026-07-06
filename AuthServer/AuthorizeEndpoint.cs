using System;
using System.Collections.Generic;
using System.Collections.Concurrent; // Thread-safe collections


// Test this endpoint with the following string to pass the required info in the query params

static class AuthorizeEndpoint
{
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

            if (string.IsNullOrEmpty(scope) || !scope.Split(' ').Contains("openid")) // Coudl contain other valid scope types as well, just need to ensure one scope to work with at least
            {
                return Results.BadRequest(new { error = "invalid_request", error_message = "Missing or invalid scope. 'openid' scope is required." });
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

            // Request is validated, store the user info
            string requestId = Guid.NewGuid().ToString();
            AuthStore.PendingRequests[requestId] = new PendingAuthRequest(
                ClientId: client_id,
                RedirectUri: redirect_uri,
                State: state,
                CodeChallenge: code_challenge,
                Scope: scope,
                Nonce: nonce ?? "", // Returns "" if nonce is null, since nonce is optional in the Authorization Code Flow but we still need to store its value inthe ID token
                ExpiresAt: DateTime.UtcNow.AddMinutes(5) // Say 5 minutes is enough time for a user to log in before needing to start over
            );
            //

            // Format the HTML page response here, returning a minimal HTML login form
            // Note the hidden requestId field, this is how the POST handler will know which pending auth request to complete
            // E.g. the user doesn't need to see it, but we need the form to post the requestID back to the server so we can look up the user info there in our pending requests
            // The inserted Javascript in the header will force a page reload if the user gets a cached version of the page by clicking the back button
            // This way if they ever access a cached version of the page via back (which will have an old requestId baked in) we manually refresh
            string html = $$"""
                <!DOCTYPE html>
                <html>
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
                    <p>Client requesting access: <strong>{{client_id}}</strong></p>
                    <p>Scope: <strong>{{scope}}</strong></p>
                    <form method="post" action="/authorize">
                        <input type="hidden" name="requestId" value="{{requestId}}" />
                        <label>Username: <input type="text" name="username" value="user1"></input></label><br/>
                        <label>Password: <input type="password" name="password" value="pass1"></input></label><br/>
                        <button type="submit">Sign In</button>
                    </form>
                </body>
            """;

            // Request to the browser that this page not be cached.  Sign in pages carry one-time tokens and should never be cached.
            // This should prevent browsers heuristically caching our page
            context.Response.Headers.CacheControl = "no-store"; // Tells the browser to never cache this response
            context.Response.Headers.Pragma = "no-cache"; // Tells older HTTP/1.0 caches to never cache this response, legacy support

            return Results.Content(html, "text/html");
        });
        //

        // Map the POST route for handling the form submission from GET
        app.MapPost("/authorize", async (HttpContext context) =>
        {
            // 
            if (!context.Request.HasFormContentType)
            {
                return Results.BadRequest("Expected form-encoded request body.");
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
                Console.WriteLine($"RequestId: {requestId}");
                foreach (var kvp in AuthStore.PendingRequests)
                {
                    Console.WriteLine($"{kvp.Value}");
                }
                return Results.BadRequest(new { error = "invalid_grant", error_message = "Unknown or expired login request - requestId not found." });
            }

            // Verify that the username matches a stored password for that username
            if (!AuthStore.Users.TryGetValue(username, out var expectedPassword) || password != expectedPassword)
            {
                return Results.Content("<h2>Invalid username or password</h2>", "text/html");
            }

            // Generate the authorization code here.  This is just a random string we will hold on to and share with our token endpoint later
            // This will be a short-lived single-use authoriation code
            // 32 random bytes, base64url-encoded, unguessable and url safe
            var codeBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            var code = Convert.ToBase64String(codeBytes)
                // = is used as padding in base64 encoded strings, we don't need to decode our string back to its original bytes so we can remove the padding that keeps the encoded length at a multiple of 4
                // we need to remove +, /, and = from our string because it is passed by url, where those characters have meaning
                .Replace('+', '-').Replace('/','_').TrimEnd('=');


            // Store what the /token endpoint will need to validate this code later
            // Codes expire fast, typically 60 seconds is enough time for the redirect round trip since no user interaction is needed
            AuthStore.AuthCodes[code] = new AuthorizationCodeData(
                ClientId: pending.ClientId,
                RedirectUri: pending.RedirectUri,
                CodeChallenge: pending.CodeChallenge,
                Scope: pending.Scope,
                Nonce: pending.Nonce,
                Subject: username, // used by the token endpoint to know which sub claim to put in the issued tokens
                ExpiresAt: DateTime.UtcNow.AddSeconds(60) // not compared, but we can use this to remove this from our ConcurrentDictionary after a minute of inactivity so we don't get memory leaks if our requiestID is never requested
            );

            // Redirect back to the client with the code and the original state value.
            // The client compares this state against what it originally sent to detect CSRF (Cross Site Request Forgery)
            // Basically, the client app generates and stores locally a random state value
            // If an attacker tricked a user into clicking a link, the attacker could inject their own account credentials sign in response into the response for the user,
            //  signing the user in to the attackers account silently.  The user would think they were logged in to their account, but it would be the attackers account and they could see what actions the user takes while logged in
            // nonce: used to verify ID token, binds ID token to the browser
            // state: used to verify authorization response, binds authorization response to the request
            // Note: EscapeDataString is not necessary since our ToBase64String does not include characters that need escaping, but it is good practice when inserting strings into a url
            var redirectUrl = $"{pending.RedirectUri}?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(pending.State)}";
            return Results.Redirect(redirectUrl);
        });
    }
}