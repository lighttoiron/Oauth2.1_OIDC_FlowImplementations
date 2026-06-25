record RefreshTokenData(
    string ClientId,
    string Subject,
    string Scope,
    string FamilyId, // This FamilyId is shared by every token that's part of a given refresh token chain
                     // E.g. Token A gets exchanged for token B, B exchanged for C, A B and C all share a family ID
    DateTime ExpiresAt, // This is an absoluteExpirationData for us, all tokens in the same family will share the same ExpiresAt time in this app (sliding expiry time is also an option for your project)
    bool Used = false
);
