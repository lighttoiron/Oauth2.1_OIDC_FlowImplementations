record LoginAttempt(
    string CodeVerifier,
    string State,
    string Scope,
    bool IsPopup,
    DateTime ExpiresAt
);