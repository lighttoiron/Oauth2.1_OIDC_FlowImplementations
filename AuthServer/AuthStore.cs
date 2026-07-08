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

    // Holds a reference to all of our currently active sessions
    public static readonly ConcurrentDictionary<string, AuthSession> ActiveSessions = new();

    // Holds the validated OAuth parameters between the GET (login page) and POST (credential submission) so the POST can access them
    public static readonly ConcurrentDictionary<string, PendingAuthRequest> PendingRequests = new();

    // Holds the current pending consent requests while waiting for a user to grant consent for a requested scope
    public static readonly ConcurrentDictionary<string, PendingConsentRequest> PendingConsentRequests = new();

    // Holds the code data for authorization codes, string is the auth code itself.  When someone sends the auth code, we can look up the data directly for that code.
    public static readonly ConcurrentDictionary<string, AuthorizationCodeData> AuthCodes = new();

    // Holds our currently active refresh tokens and their info
    public static readonly ConcurrentDictionary<string, RefreshTokenData> RefreshTokens = new();

    // Contains lists of scopes that a user has consented to, associated with a specific client
    // Key is "{subject}:{clientId}"
    public static readonly ConcurrentDictionary<string, ConsentRecord> ConsentRecords = new();
}