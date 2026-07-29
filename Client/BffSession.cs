// Session information for currently active user sessions
record BffSession(
    string? Subject,
    string Scope,
    string? AccessToken,
    string? RefreshToken,
    string? IdToken,
    DateTime? AccessTokenExpiresAt,
    DateTime ExpiresAt // This is the absolute expire time for the refresh token family we are using, consider updating if refresh tokens get new lifetimes
);