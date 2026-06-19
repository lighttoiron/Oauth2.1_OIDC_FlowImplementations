// A simple record containing auth request info, for holding onto that info while we process
record PendingAuthRequest(
    string ClientId,
    string RedirectUri,
    string State,
    string CodeChallenge,
    string Scope,
    string Nonce,
    DateTime ExpiresAt // For our server, used to clean up old pending requests in case they are not redeemed by the client
);