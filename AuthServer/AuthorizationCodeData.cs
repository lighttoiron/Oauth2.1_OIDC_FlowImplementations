using System;

// This is the data that makes up an authorization code
record AuthorizationCodeData(
    string ClientId,
    string RedirectUri,
    string CodeChallenge,
    string Scope,
    string Nonce,
    string Subject, // The authenticated user's identity, becomes the 'sub'  claim later in the ID token.
    DateTime ExpiresAt
);