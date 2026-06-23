using System;
using System.Collections.Concurrent;

static class AuthStore
{
    // Create a dictionary of usernames and passwords for testing.
    // In production, you would use a proper user store with hashed passwords, like a database or an identity management system.
    public static readonly Dictionary<string, string> Users = new Dictionary<string, string>
    {
            { "user1", "pass1" },
            { "user2", "pass2" }
    };

    // Holds the validated OAuth parameters between the GET (login page) and POST (credential submission) so the POST can access them
    public static readonly ConcurrentDictionary<string, PendingAuthRequest> PendingRequests = new();

    // Holds the code data for authorization codes, string is the auth code itself.  When someone sends the auth code, we can look up the data directly for that code.
    public static readonly ConcurrentDictionary<string, AuthorizationCodeData> AuthCodes = new();
}