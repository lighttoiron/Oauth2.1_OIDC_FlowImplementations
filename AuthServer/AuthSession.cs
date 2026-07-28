// Auth session info, used to store how long a user has a valid session for.
// Mapped with an opaque session token so each user has a unique token we can use to verify their session.
record AuthSession(
    string Subject,
    DateTime ExpiresAt
);