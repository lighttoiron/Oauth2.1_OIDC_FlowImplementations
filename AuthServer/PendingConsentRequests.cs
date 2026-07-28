// Information needed to respond to a user granting or denying delegated permission for a client
record PendingConsentRequest(
    string Subject,
    string ClientId,
    string RedirectUri,
    HashSet<string> RequestedScopes,
    HashSet<string> NeedsConsent,
    HashSet<string> AutoGranted,
    string State,
    string CodeChallenge,
    string Nonce,
    DateTime ExpiresAt
);