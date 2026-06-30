record LoginAttempt(
    string CodeVerifier,
    string State,
    bool IsPopup,
    DateTime ExpiresAt
);