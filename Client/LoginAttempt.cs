// Information regarding current user login attempts
record LoginAttempt(
    string CodeVerifier,
    string State,
    string Scope,
    bool IsPopup,
    DateTime ExpiresAt
);