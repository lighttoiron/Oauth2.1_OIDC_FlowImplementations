record BffSession(
    string Subject,
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAt,
    DateTime ExpiresAt // This is the absolute expire time for the refresh token family we are using, consider updating if refresh tokens get new lifetimes
);